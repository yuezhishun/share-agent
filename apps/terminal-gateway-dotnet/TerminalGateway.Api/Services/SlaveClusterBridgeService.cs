using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class SlaveClusterBridgeService : BackgroundService
{
    private readonly GatewayOptions _options;
    private readonly InstanceManager _instances;
    private readonly ClusterCommandExecutor _executor;

    public SlaveClusterBridgeService(GatewayOptions options, InstanceManager instances, ClusterCommandExecutor executor)
    {
        _options = options;
        _instances = instances;
        _executor = executor;
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
        return await _executor.ExecuteAsync(command, cancellationToken);
    }

    private static string? ReadString(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
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
