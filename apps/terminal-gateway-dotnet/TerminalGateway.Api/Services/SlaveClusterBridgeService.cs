using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class SlaveClusterBridgeService : BackgroundService
{
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

    private readonly GatewayOptions _options;
    private readonly InstanceManager _instances;
    private readonly FileApiService _files;

    public SlaveClusterBridgeService(GatewayOptions options, InstanceManager instances, FileApiService files)
    {
        _options = options;
        _instances = instances;
        _files = files;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.Equals(_options.GatewayRole, "slave", StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.MasterUrl))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            HubConnection? connection = null;
            Action<string, object>? rawHandler = null;
            Action<string, object>? exitHandler = null;
            try
            {
                var target = new Uri(new Uri(_options.MasterUrl.TrimEnd('/') + "/"), "hubs/cluster");
                connection = new HubConnectionBuilder()
                    .WithUrl(target)
                    .WithAutomaticReconnect()
                    .Build();

                connection.On<ClusterCommandEnvelope>("ClusterCommand", async cmd =>
                {
                    var result = await ExecuteCommandAsync(cmd, stoppingToken);
                    await connection.InvokeAsync("SubmitCommandResult", result, stoppingToken);
                });

                await connection.StartAsync(stoppingToken);
                await connection.InvokeAsync("RegisterNode", new ClusterRegisterNodeRequest
                {
                    Token = _options.ClusterToken,
                    NodeId = _options.NodeId,
                    NodeName = _options.NodeName,
                    NodeLabel = _options.NodeLabel,
                    NodeRole = "slave",
                    InstanceCount = _instances.List().Count
                }, stoppingToken);

                rawHandler = (instanceId, payload) => _ = PublishRuntimeEventAsync(connection, payload, stoppingToken);
                exitHandler = (instanceId, payload) => _ = PublishRuntimeEventAsync(connection, payload, stoppingToken);
                _instances.Raw += rawHandler;
                _instances.Exited += exitHandler;

                while (!stoppingToken.IsCancellationRequested && connection.State == HubConnectionState.Connected)
                {
                    await connection.InvokeAsync("Heartbeat", new ClusterHeartbeatRequest
                    {
                        Token = _options.ClusterToken,
                        NodeId = _options.NodeId,
                        InstanceCount = _instances.List().Count
                    }, stoppingToken);

                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            finally
            {
                if (rawHandler is not null)
                {
                    _instances.Raw -= rawHandler;
                }

                if (exitHandler is not null)
                {
                    _instances.Exited -= exitHandler;
                }

                if (connection is not null)
                {
                    try
                    {
                        await connection.DisposeAsync();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    private async Task PublishRuntimeEventAsync(HubConnection connection, object payload, CancellationToken cancellationToken)
    {
        if (connection.State != HubConnectionState.Connected)
        {
            return;
        }

        JsonElement serialized;
        try
        {
            serialized = JsonSerializer.SerializeToElement(payload);
        }
        catch
        {
            return;
        }

        var instanceId = ReadString(serialized, "instance_id");
        var type = ReadString(serialized, "type") ?? "term.unknown";
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        var seq = ReadLong(serialized, "seq");
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            await connection.InvokeAsync("PublishTerminalEvent", new ClusterTerminalEventEnvelope
            {
                Token = _options.ClusterToken,
                EventId = $"evt-{Guid.NewGuid():N}",
                NodeId = _options.NodeId,
                InstanceId = instanceId,
                Seq = seq,
                Ts = ts,
                Type = type,
                Payload = serialized
            }, cancellationToken);
        }
        catch
        {
        }
    }

    private async Task<ClusterCommandResult> ExecuteCommandAsync(ClusterCommandEnvelope command, CancellationToken cancellationToken)
    {
        try
        {
            switch (command.Type)
            {
                case "instance.create":
                {
                    var request = command.Payload.Deserialize<CreateInstanceRequest>(CaseInsensitiveJson) ?? new CreateInstanceRequest();
                    var created = await _instances.CreateAsync(request, _options.FilesBasePath, cancellationToken);
                    return Ok(command, new { instance_id = created.Id });
                }
                case "instance.input":
                {
                    var instanceId = ReadString(command.Payload, "instance_id") ?? ReadString(command.Payload, "instanceId");
                    if (string.IsNullOrWhiteSpace(instanceId) || !_instances.WriteStdin(instanceId, ReadString(command.Payload, "data") ?? string.Empty))
                    {
                        return Fail(command, "instance not found");
                    }

                    return Ok(command, new { ok = true });
                }
                case "instance.resize":
                {
                    var instanceId = ReadString(command.Payload, "instance_id") ?? ReadString(command.Payload, "instanceId");
                    var cols = ReadInt(command.Payload, "cols");
                    var rows = ReadInt(command.Payload, "rows");
                    if (string.IsNullOrWhiteSpace(instanceId) || _instances.Resize(instanceId, cols, rows) is null)
                    {
                        return Fail(command, "instance not found");
                    }

                    return Ok(command, new { ok = true });
                }
                case "instance.sync":
                {
                    var instanceId = ReadString(command.Payload, "instance_id") ?? ReadString(command.Payload, "instanceId");
                    if (string.IsNullOrWhiteSpace(instanceId))
                    {
                        return Fail(command, "instance_id is required");
                    }

                    var syncType = (ReadString(command.Payload, "type") ?? "raw").Trim().ToLowerInvariant();
                    if (syncType == "raw")
                    {
                        var reqId = ReadString(command.Payload, "req_id")
                            ?? ReadString(command.Payload, "reqId")
                            ?? $"raw-sync-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                        var sinceSeq = ReadInt(command.Payload, "since_seq");
                        if (sinceSeq <= 0)
                        {
                            sinceSeq = ReadInt(command.Payload, "sinceSeq");
                        }

                        var replay = _instances.RawReplayEvent(instanceId, sinceSeq > 0 ? sinceSeq : null, reqId);
                        if (replay is null)
                        {
                            return Fail(command, "instance not found");
                        }

                        return Ok(command, replay);
                    }

                    if (syncType == "history")
                    {
                        var before = ReadString(command.Payload, "before") ?? "h-1";
                        var limit = Math.Clamp(ReadInt(command.Payload, "limit"), 1, 500);
                        var reqId = ReadString(command.Payload, "req_id") ?? ReadString(command.Payload, "reqId") ?? $"history-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                        var chunk = _instances.HistoryChunk(instanceId, reqId, before, limit);
                        if (chunk is null)
                        {
                            return Fail(command, "instance not found");
                        }

                        return Ok(command, chunk);
                    }

                    var snapshot = _instances.Snapshot(instanceId, advanceSeq: true);
                    if (snapshot is null)
                    {
                        return Fail(command, "instance not found");
                    }

                    return Ok(command, snapshot);
                }
                case "instance.terminate":
                {
                    var instanceId = ReadString(command.Payload, "instance_id") ?? ReadString(command.Payload, "instanceId");
                    if (string.IsNullOrWhiteSpace(instanceId) || !_instances.Terminate(instanceId))
                    {
                        return Fail(command, "instance not found");
                    }

                    return Ok(command, new { ok = true });
                }
                case "files.upload":
                {
                    var instanceId = ReadString(command.Payload, "instance_id") ?? ReadString(command.Payload, "instanceId");
                    var fileName = ReadString(command.Payload, "file_name") ?? ReadString(command.Payload, "fileName");
                    var contentBase64 = ReadString(command.Payload, "content_base64") ?? ReadString(command.Payload, "contentBase64");
                    if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentBase64))
                    {
                        return Fail(command, "invalid upload payload");
                    }

                    var state = _instances.Get(instanceId);
                    if (state is null)
                    {
                        return Fail(command, "instance not found");
                    }

                    byte[] bytes;
                    try
                    {
                        bytes = Convert.FromBase64String(contentBase64);
                    }
                    catch
                    {
                        return Fail(command, "invalid upload content");
                    }

                    var uploaded = await _files.SaveUploadBytesAsync(_options.FilesBasePath, state.Cwd, fileName, bytes, cancellationToken);
                    return Ok(command, uploaded);
                }
                default:
                    return Fail(command, $"unsupported command: {command.Type}");
            }
        }
        catch (Exception ex)
        {
            return Fail(command, ex.Message);
        }
    }

    private ClusterCommandResult Ok(ClusterCommandEnvelope command, object payload)
    {
        return new ClusterCommandResult
        {
            CommandId = command.CommandId,
            NodeId = _options.NodeId,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(payload)
        };
    }

    private ClusterCommandResult Fail(ClusterCommandEnvelope command, string error)
    {
        return new ClusterCommandResult
        {
            CommandId = command.CommandId,
            NodeId = _options.NodeId,
            Ok = false,
            Error = error,
            Payload = JsonSerializer.SerializeToElement(new { })
        };
    }

    private static string? ReadString(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int ReadInt(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : 0;
    }

    private static long ReadLong(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt64()
            : 0;
    }
}
