using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using TerminalGateway.Api.Endpoints;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class SlaveClusterBridgeService : BackgroundService
{
    private readonly GatewayOptions _options;
    private readonly InstanceManager _instances;
    private readonly ClusterCommandExecutor _executor;
    private readonly object _connectionGate = new();
    private readonly object _subscriptionGate = new();
    private readonly IHubContext<TerminalHubV2> _terminalHubV2;

    private volatile HubConnection? _connection;
    private volatile string? _lastError;
    private readonly Dictionary<string, int> _remoteInstanceSubscriptions = new(StringComparer.Ordinal);

    public SlaveClusterBridgeService(GatewayOptions options, InstanceManager instances, ClusterCommandExecutor executor, IHubContext<TerminalHubV2> terminalHubV2)
    {
        _options = options;
        _instances = instances;
        _executor = executor;
        _terminalHubV2 = terminalHubV2;
    }

    public bool IsEnabled =>
        string.Equals(_options.GatewayRole, "slave", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(_options.MasterUrl);

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public string? LastError => _lastError;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled)
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
                var target = new Uri(new Uri(_options.MasterUrl!.TrimEnd('/') + "/"), "hubs/cluster");
                connection = new HubConnectionBuilder()
                    .WithUrl(target)
                    .WithAutomaticReconnect()
                    .Build();

                connection.On<ClusterCommandEnvelope>("ClusterCommand", async cmd =>
                {
                    var result = await ExecuteCommandAsync(cmd, stoppingToken);
                    await connection.InvokeAsync("SubmitCommandResult", result, stoppingToken);
                });
                connection.On<ClusterTerminalEventEnvelope>("ForwardTerminalEvent", envelope => _ = PublishForwardedEventAsync(envelope));

                await connection.StartAsync(stoppingToken);
                SetConnection(connection);
                _lastError = null;
                await connection.InvokeAsync("RegisterNode", new ClusterRegisterNodeRequest
                {
                    Token = _options.ClusterToken,
                    NodeId = _options.NodeId,
                    NodeName = _options.NodeName,
                    NodeLabel = _options.NodeLabel,
                    NodeRole = "slave",
                    InstanceCount = _instances.List().Count
                }, stoppingToken);
                await SyncLocalInstancesAsync(connection, stoppingToken);
                await ResubscribeRemoteInstancesAsync(connection, stoppingToken);

                rawHandler = (instanceId, payload) => _ = PublishRuntimeEventAsync(connection, payload, stoppingToken);
                exitHandler = (instanceId, payload) => _ = PublishRuntimeEventAsync(connection, payload, stoppingToken);
                _instances.Raw += rawHandler;
                _instances.Exited += exitHandler;
                _instances.StateChanged += rawHandler;

                while (!stoppingToken.IsCancellationRequested && connection.State == HubConnectionState.Connected)
                {
                    await connection.InvokeAsync("Heartbeat", new ClusterHeartbeatRequest
                    {
                        Token = _options.ClusterToken,
                        NodeId = _options.NodeId,
                        InstanceCount = _instances.List().Count
                    }, stoppingToken);
                    await SyncLocalInstancesAsync(connection, stoppingToken);

                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            finally
            {
                ClearConnection(connection);
                if (rawHandler is not null)
                {
                    _instances.Raw -= rawHandler;
                    _instances.StateChanged -= rawHandler;
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

    public async Task<(bool Ok, IReadOnlyList<NodeSummary> Items, string? Error)> GetMasterNodesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await RequestCommandAsync(string.Empty, "cluster.nodes", new
            {
                include_other_slaves = _options.SlaveViewOtherSlaves
            }, cancellationToken);
            return result.Ok
                ? (true, ReadNodeItems(result.Payload), null)
                : (false, Array.Empty<NodeSummary>(), result.Error ?? "master node query failed");
        }
        catch (Exception ex)
        {
            return (false, Array.Empty<NodeSummary>(), ex.Message);
        }
    }

    public async Task<(bool Ok, IReadOnlyList<InstanceSummary> Items, string? Error)> GetMasterInstancesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await RequestCommandAsync(string.Empty, "cluster.instances", new
            {
                include_other_slaves = _options.SlaveViewOtherSlaves
            }, cancellationToken);
            return result.Ok
                ? (true, ReadInstanceItems(result.Payload), null)
                : (false, Array.Empty<InstanceSummary>(), result.Error ?? "master instance query failed");
        }
        catch (Exception ex)
        {
            return (false, Array.Empty<InstanceSummary>(), ex.Message);
        }
    }

    public async Task AcquireRemoteInstanceSubscriptionAsync(string instanceId, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var normalizedInstanceId = Normalize(instanceId);
        if (normalizedInstanceId.Length == 0)
        {
            return;
        }

        var shouldSubscribe = false;
        lock (_subscriptionGate)
        {
            _remoteInstanceSubscriptions.TryGetValue(normalizedInstanceId, out var current);
            _remoteInstanceSubscriptions[normalizedInstanceId] = current + 1;
            shouldSubscribe = current == 0;
        }

        if (!shouldSubscribe)
        {
            return;
        }

        var connection = _connection;
        if (connection is null || connection.State != HubConnectionState.Connected)
        {
            return;
        }

        await connection.InvokeAsync("SubscribeInstanceEvents", new ClusterInstanceSubscriptionRequest
        {
            Token = _options.ClusterToken,
            SourceNodeId = _options.NodeId,
            InstanceId = normalizedInstanceId
        }, cancellationToken);
    }

    public async Task ReleaseRemoteInstanceSubscriptionAsync(string instanceId, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var normalizedInstanceId = Normalize(instanceId);
        if (normalizedInstanceId.Length == 0)
        {
            return;
        }

        var shouldUnsubscribe = false;
        lock (_subscriptionGate)
        {
            if (!_remoteInstanceSubscriptions.TryGetValue(normalizedInstanceId, out var current))
            {
                return;
            }

            if (current <= 1)
            {
                _remoteInstanceSubscriptions.Remove(normalizedInstanceId);
                shouldUnsubscribe = true;
            }
            else
            {
                _remoteInstanceSubscriptions[normalizedInstanceId] = current - 1;
            }
        }

        if (!shouldUnsubscribe)
        {
            return;
        }

        var connection = _connection;
        if (connection is null || connection.State != HubConnectionState.Connected)
        {
            return;
        }

        await connection.InvokeAsync("UnsubscribeInstanceEvents", new ClusterInstanceSubscriptionRequest
        {
            Token = _options.ClusterToken,
            SourceNodeId = _options.NodeId,
            InstanceId = normalizedInstanceId
        }, cancellationToken);
    }

    public async Task<ClusterCommandResult> RequestCommandAsync(string targetNodeId, string type, object payload, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("slave cluster bridge is not enabled");
        }

        var connection = _connection;
        if (connection is null || connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException(_lastError is null
                ? "master cluster bridge is not connected"
                : $"master cluster bridge is not connected: {_lastError}");
        }

        try
        {
            return await connection.InvokeAsync<ClusterCommandResult>("RequestCommand", new ClusterProxyCommandRequest
            {
                Token = _options.ClusterToken,
                SourceNodeId = _options.NodeId,
                TargetNodeId = targetNodeId,
                Type = type,
                Payload = JsonSerializer.SerializeToElement(payload)
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            throw;
        }
    }

    public async Task TrySyncLocalInstancesAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var connection = _connection;
        if (connection is null || connection.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await SyncLocalInstancesAsync(connection, cancellationToken);
        }
        catch
        {
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

    private void SetConnection(HubConnection connection)
    {
        lock (_connectionGate)
        {
            _connection = connection;
        }
    }

    private void ClearConnection(HubConnection? connection)
    {
        lock (_connectionGate)
        {
            if (ReferenceEquals(_connection, connection))
            {
                _connection = null;
            }
        }
    }

    private static IReadOnlyList<NodeSummary> ReadNodeItems(JsonElement root)
    {
        if (!TryGetItems(root, out var items))
        {
            return Array.Empty<NodeSummary>();
        }

        var results = new List<NodeSummary>();
        foreach (var item in items.EnumerateArray())
        {
            results.Add(new NodeSummary
            {
                NodeId = ReadString(item, "node_id") ?? string.Empty,
                NodeName = ReadString(item, "node_name") ?? string.Empty,
                NodeRole = ReadString(item, "node_role") ?? string.Empty,
                NodeLabel = ReadString(item, "node_label"),
                IsCurrent = ReadBool(item, "is_current", false),
                NodeOnline = ReadBool(item, "node_online", false),
                InstanceCount = ReadInt(item, "instance_count"),
                LastSeenAt = ReadString(item, "last_seen_at") ?? string.Empty
            });
        }

        return results;
    }

    private static IReadOnlyList<InstanceSummary> ReadInstanceItems(JsonElement root)
    {
        if (!TryGetItems(root, out var items))
        {
            return Array.Empty<InstanceSummary>();
        }

        var results = new List<InstanceSummary>();
        foreach (var item in items.EnumerateArray())
        {
            results.Add(new InstanceSummary
            {
                Id = ReadString(item, "id") ?? string.Empty,
                Command = ReadString(item, "command") ?? string.Empty,
                Cwd = ReadString(item, "cwd") ?? string.Empty,
                Cols = ReadInt(item, "cols"),
                Rows = ReadInt(item, "rows"),
                CreatedAt = ReadString(item, "created_at") ?? string.Empty,
                Status = ReadString(item, "status") ?? string.Empty,
                Clients = ReadInt(item, "clients"),
                NodeId = ReadString(item, "node_id") ?? string.Empty,
                NodeName = ReadString(item, "node_name") ?? string.Empty,
                NodeRole = ReadString(item, "node_role") ?? string.Empty,
                NodeOnline = ReadBool(item, "node_online", false)
            });
        }

        return results;
    }

    private static bool TryGetItems(JsonElement root, out JsonElement items)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out items) && items.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        items = default;
        return false;
    }

    private async Task ResubscribeRemoteInstancesAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        string[] instanceIds;
        lock (_subscriptionGate)
        {
            instanceIds = _remoteInstanceSubscriptions.Keys.ToArray();
        }

        foreach (var instanceId in instanceIds)
        {
            await connection.InvokeAsync("SubscribeInstanceEvents", new ClusterInstanceSubscriptionRequest
            {
                Token = _options.ClusterToken,
                SourceNodeId = _options.NodeId,
                InstanceId = instanceId
            }, cancellationToken);
        }
    }

    private async Task SyncLocalInstancesAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != HubConnectionState.Connected)
        {
            return;
        }

        await connection.InvokeAsync("SyncNodeInstances", new ClusterNodeInstancesSyncRequest
        {
            Token = _options.ClusterToken,
            SourceNodeId = _options.NodeId,
            Items = _instances.List()
        }, cancellationToken);
    }

    private async Task PublishForwardedEventAsync(ClusterTerminalEventEnvelope envelope)
    {
        var instanceId = Normalize(envelope.InstanceId);
        if (instanceId.Length == 0)
        {
            return;
        }

        await _terminalHubV2.Clients.Group(TerminalHubV2.BuildInstanceGroup(instanceId))
            .SendAsync("TerminalEvent", ConvertPayloadForV2(envelope.Payload));
    }

    private static object ConvertPayloadForV2(JsonElement payload)
    {
        var type = ReadString(payload, "type") ?? string.Empty;

        if (string.Equals(type, "term.snapshot", StringComparison.Ordinal))
        {
            return new
            {
                v = 2,
                type = "term.v2.snapshot",
                instance_id = ReadString(payload, "instance_id"),
                node_id = ReadString(payload, "node_id"),
                node_name = ReadString(payload, "node_name"),
                seq = ReadLong(payload, "seq"),
                ts = ReadLong(payload, "ts"),
                size = ReadElement(payload, "size"),
                cursor = ReadElement(payload, "cursor"),
                render_epoch = ReadLong(payload, "render_epoch"),
                instance_epoch = ReadLong(payload, "instance_epoch"),
                rows = ReadElement(payload, "rows")
            };
        }

        if (string.Equals(type, "term.raw", StringComparison.Ordinal))
        {
            return new
            {
                v = 2,
                type = "term.v2.raw",
                instance_id = ReadString(payload, "instance_id"),
                node_id = ReadString(payload, "node_id"),
                node_name = ReadString(payload, "node_name"),
                seq = ReadLong(payload, "seq"),
                ts = ReadLong(payload, "ts"),
                replay = false,
                data = ReadString(payload, "data") ?? string.Empty
            };
        }

        if (string.Equals(type, "term.exit", StringComparison.Ordinal))
        {
            return new
            {
                v = 2,
                type = "term.exit",
                instance_id = ReadString(payload, "instance_id"),
                node_id = ReadString(payload, "node_id"),
                node_name = ReadString(payload, "node_name"),
                exit_code = ReadNullableLong(payload, "exit_code") ?? ReadNullableLong(payload, "code"),
                ts = ReadLong(payload, "ts")
            };
        }

        if (string.Equals(type, "term.owner.changed", StringComparison.Ordinal))
        {
            return new
            {
                v = 2,
                type = "term.v2.owner.changed",
                instance_id = ReadString(payload, "instance_id"),
                node_id = ReadString(payload, "node_id"),
                node_name = ReadString(payload, "node_name"),
                owner_connection_id = ReadString(payload, "owner_connection_id"),
                render_epoch = ReadLong(payload, "render_epoch"),
                instance_epoch = ReadLong(payload, "instance_epoch"),
                ts = ReadLong(payload, "ts")
            };
        }

        return payload;
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

    private static long? ReadNullableLong(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var prop)
            && prop.TryGetInt64(out var number)
            ? number
            : null;
    }

    private static bool ReadBool(JsonElement payload, string name, bool fallback)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var prop)
            && prop.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? prop.GetBoolean()
            : fallback;
    }

    private static JsonElement ReadElement(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var prop)
            ? prop
            : default;
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim();
    }
}
