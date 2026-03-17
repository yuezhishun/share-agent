using System.Text.Json;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class ClusterCommandExecutor
{
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions ClusterJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly GatewayOptions _options;
    private readonly InstanceManager _instances;
    private readonly FileApiService _files;
    private readonly ProcessApiService _processes;

    public ClusterCommandExecutor(GatewayOptions options, InstanceManager instances, FileApiService files, ProcessApiService processes)
    {
        _options = options;
        _instances = instances;
        _files = files;
        _processes = processes;
    }

    public async Task<ClusterCommandResult> ExecuteAsync(ClusterCommandEnvelope command, CancellationToken cancellationToken)
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
                case "process.run":
                {
                    var request = command.Payload.Deserialize<RunProcessRequest>(CaseInsensitiveJson) ?? new RunProcessRequest();
                    return Ok(command, await _processes.RunAsync(request, cancellationToken));
                }
                case "process.start":
                {
                    var request = command.Payload.Deserialize<RunProcessRequest>(CaseInsensitiveJson) ?? new RunProcessRequest();
                    return Ok(command, await _processes.StartManagedAsync(request, cancellationToken));
                }
                case "process.list":
                    return Ok(command, new { items = _processes.ListManaged() });
                case "process.get":
                {
                    var processId = ReadString(command.Payload, "process_id") ?? ReadString(command.Payload, "processId");
                    if (string.IsNullOrWhiteSpace(processId))
                    {
                        return Fail(command, "process_id is required");
                    }

                    return Ok(command, _processes.GetManaged(processId));
                }
                case "process.output":
                {
                    var processId = ReadString(command.Payload, "process_id") ?? ReadString(command.Payload, "processId");
                    if (string.IsNullOrWhiteSpace(processId))
                    {
                        return Fail(command, "process_id is required");
                    }

                    return Ok(command, new { items = _processes.GetOutput(processId) });
                }
                case "process.wait":
                {
                    var processId = ReadString(command.Payload, "process_id") ?? ReadString(command.Payload, "processId");
                    if (string.IsNullOrWhiteSpace(processId))
                    {
                        return Fail(command, "process_id is required");
                    }

                    var timeoutMs = ReadNullableInt(command.Payload, "timeout_ms") ?? ReadNullableInt(command.Payload, "timeoutMs");
                    return Ok(command, await _processes.WaitManagedAsync(processId, timeoutMs));
                }
                case "process.stop":
                {
                    var processId = ReadString(command.Payload, "process_id") ?? ReadString(command.Payload, "processId");
                    if (string.IsNullOrWhiteSpace(processId))
                    {
                        return Fail(command, "process_id is required");
                    }

                    var body = command.Payload.Deserialize<StopManagedProcessRequest>(CaseInsensitiveJson) ?? new StopManagedProcessRequest();
                    return Ok(command, await _processes.StopManagedAsync(processId, body.Force == true));
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
            NodeId = NormalizeTargetNodeId(command),
            SourceNodeId = NormalizeSourceNodeId(command),
            TargetNodeId = NormalizeTargetNodeId(command),
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(payload, ClusterJson)
        };
    }

    private ClusterCommandResult Fail(ClusterCommandEnvelope command, string error)
    {
        return new ClusterCommandResult
        {
            CommandId = command.CommandId,
            NodeId = NormalizeTargetNodeId(command),
            SourceNodeId = NormalizeSourceNodeId(command),
            TargetNodeId = NormalizeTargetNodeId(command),
            Ok = false,
            Error = error,
            Payload = JsonSerializer.SerializeToElement(new { }, ClusterJson)
        };
    }

    private string NormalizeSourceNodeId(ClusterCommandEnvelope command)
    {
        var value = (command.SourceNodeId ?? string.Empty).Trim();
        return value.Length == 0 ? _options.NodeId : value;
    }

    private string NormalizeTargetNodeId(ClusterCommandEnvelope command)
    {
        var value = (command.TargetNodeId ?? command.NodeId ?? string.Empty).Trim();
        return value.Length == 0 ? _options.NodeId : value;
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

    private static int? ReadNullableInt(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : null;
    }
}
