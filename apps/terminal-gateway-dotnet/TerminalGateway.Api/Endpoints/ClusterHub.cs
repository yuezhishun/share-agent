using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public sealed class ClusterHub : Hub
{
    private readonly GatewayOptions _options;
    private readonly NodeRegistry _nodeRegistry;
    private readonly ClusterCommandBroker _broker;
    private readonly ClusterCommandExecutor _executor;
    private readonly ClusterEventDeduplicator _events;
    private readonly RemoteInstanceRegistry _remoteInstances;
    private readonly IHubContext<TerminalHub> _terminalHub;
    private readonly ClusterTerminalSubscriptionService _subscriptions;
    private readonly ILogger<ClusterHub> _logger;

    public ClusterHub(
        GatewayOptions options,
        NodeRegistry nodeRegistry,
        ClusterCommandBroker broker,
        ClusterCommandExecutor executor,
        ClusterEventDeduplicator events,
        RemoteInstanceRegistry remoteInstances,
        IHubContext<TerminalHub> terminalHub,
        ClusterTerminalSubscriptionService subscriptions,
        ILogger<ClusterHub> logger)
    {
        _options = options;
        _nodeRegistry = nodeRegistry;
        _broker = broker;
        _executor = executor;
        _events = events;
        _remoteInstances = remoteInstances;
        _terminalHub = terminalHub;
        _subscriptions = subscriptions;
        _logger = logger;
    }

    public Task RegisterNode(ClusterRegisterNodeRequest request)
    {
        EnsureMasterMode();
        EnsureToken(request.Token);
        _nodeRegistry.RegisterRemoteNode(request, Context.ConnectionId);
        return Task.CompletedTask;
    }

    public Task Heartbeat(ClusterHeartbeatRequest request)
    {
        EnsureMasterMode();
        EnsureToken(request.Token);
        _nodeRegistry.Heartbeat(request, Context.ConnectionId);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (_nodeRegistry.TryGetNodeByConnectionId(Context.ConnectionId, out var node))
        {
            _subscriptions.RemoveNodeSubscriptions(node.NodeId);
        }
        _nodeRegistry.MarkDisconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task SubmitCommandResult(ClusterCommandResult result)
    {
        EnsureMasterMode();
        _broker.Complete(result);
        return Task.CompletedTask;
    }

    public async Task<ClusterCommandResult> RequestCommand(ClusterProxyCommandRequest request)
    {
        EnsureMasterMode();
        EnsureToken(request.Token);

        var sourceNodeId = (request.SourceNodeId ?? string.Empty).Trim();
        if (sourceNodeId.Length == 0)
        {
            throw new HubException("source_node_id is required");
        }

        if (!_nodeRegistry.TryGetNodeByConnectionId(Context.ConnectionId, out var boundNode)
            || !string.Equals(boundNode.NodeId, sourceNodeId, StringComparison.Ordinal))
        {
            throw new HubException("source node mismatch");
        }

        var targetNodeId = NormalizeTargetNodeId(request.TargetNodeId);

        var command = new ClusterCommandEnvelope
        {
            CommandId = string.IsNullOrWhiteSpace(request.CommandId) ? Guid.NewGuid().ToString("N") : request.CommandId,
            NodeId = targetNodeId,
            SourceNodeId = sourceNodeId,
            TargetNodeId = targetNodeId,
            Type = string.IsNullOrWhiteSpace(request.Type) ? throw new HubException("type is required") : request.Type,
            Payload = request.Payload
        };

        ClusterCommandResult result;
        try
        {
            result = string.Equals(targetNodeId, _options.NodeId, StringComparison.Ordinal)
                ? await _executor.ExecuteAsync(command, Context.ConnectionAborted)
                : await _broker.SendAsync(sourceNodeId, targetNodeId, command.Type, command.Payload, Context.ConnectionAborted);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex,
                "Cluster RequestCommand timed out. source_node_id={SourceNodeId} target_node_id={TargetNodeId} type={Type}",
                sourceNodeId,
                targetNodeId,
                command.Type);
            throw new HubException($"cluster command timed out for node {targetNodeId}");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Cluster RequestCommand failed. source_node_id={SourceNodeId} target_node_id={TargetNodeId} type={Type}",
                sourceNodeId,
                targetNodeId,
                command.Type);
            throw new HubException(ex.Message);
        }
        TrackInstanceOwnership(command, result);
        return result;
    }

    public async Task PublishTerminalEvent(ClusterTerminalEventEnvelope envelope)
    {
        EnsureMasterMode();
        EnsureToken(envelope.Token);

        var nodeId = (envelope.NodeId ?? string.Empty).Trim();
        var instanceId = (envelope.InstanceId ?? string.Empty).Trim();
        if (nodeId.Length == 0 || instanceId.Length == 0)
        {
            throw new HubException("node_id and instance_id are required");
        }

        if (!_nodeRegistry.TryGetNodeByConnectionId(Context.ConnectionId, out var boundNode)
            || !string.Equals(boundNode.NodeId, nodeId, StringComparison.Ordinal))
        {
            throw new HubException("node mismatch");
        }

        if (!_events.TryAccept(envelope, out var hasGap))
        {
            return;
        }

        _remoteInstances.Upsert(instanceId, nodeId);

        await _terminalHub.Clients.Group(TerminalHub.BuildInstanceGroup(instanceId))
            .SendAsync("TerminalEvent", ConvertPayload(envelope.Payload));
        await _subscriptions.ForwardClusterEventAsync(instanceId, nodeId, envelope.Payload);

        if (string.Equals(envelope.Type, "term.exit", StringComparison.Ordinal))
        {
            _remoteInstances.Remove(instanceId);
        }

        if (hasGap)
        {
            await _terminalHub.Clients.Group(TerminalHub.BuildInstanceGroup(instanceId))
                .SendAsync("TerminalEvent", new
                {
                    v = 1,
                    type = "term.sync.required",
                    instance_id = instanceId,
                    node_id = nodeId,
                    node_name = boundNode.NodeName,
                    reason = "seq_gap",
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
        }
    }

    public Task SubscribeInstanceEvents(ClusterInstanceSubscriptionRequest request)
    {
        EnsureMasterMode();
        EnsureToken(request.Token);

        var sourceNodeId = RequireBoundSourceNode(request.SourceNodeId);
        var instanceId = NormalizeRequiredValue(request.InstanceId, "instance_id");
        _subscriptions.Subscribe(sourceNodeId, instanceId);
        return Task.CompletedTask;
    }

    public Task UnsubscribeInstanceEvents(ClusterInstanceSubscriptionRequest request)
    {
        EnsureMasterMode();
        EnsureToken(request.Token);

        var sourceNodeId = RequireBoundSourceNode(request.SourceNodeId);
        var instanceId = NormalizeRequiredValue(request.InstanceId, "instance_id");
        _subscriptions.Unsubscribe(sourceNodeId, instanceId);
        return Task.CompletedTask;
    }

    public Task SyncNodeInstances(ClusterNodeInstancesSyncRequest request)
    {
        EnsureMasterMode();
        EnsureToken(request.Token);

        var sourceNodeId = RequireBoundSourceNode(request.SourceNodeId);
        var items = (request.Items ?? Array.Empty<InstanceSummary>())
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => new InstanceSummary
            {
                Id = item.Id,
                Command = item.Command,
                Cwd = item.Cwd,
                Cols = item.Cols,
                Rows = item.Rows,
                CreatedAt = item.CreatedAt,
                Status = item.Status,
                Clients = item.Clients,
                NodeId = sourceNodeId,
                NodeName = string.IsNullOrWhiteSpace(item.NodeName) ? sourceNodeId : item.NodeName,
                NodeRole = string.IsNullOrWhiteSpace(item.NodeRole) ? "slave" : item.NodeRole,
                NodeOnline = item.NodeOnline
            })
            .ToList();

        _remoteInstances.SyncNode(sourceNodeId, items);
        return Task.CompletedTask;
    }

    private void EnsureMasterMode()
    {
        if (!string.Equals(_options.GatewayRole, "master", StringComparison.Ordinal))
        {
            throw new HubException("cluster hub only available in master mode");
        }
    }

    private void EnsureToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(_options.ClusterToken))
        {
            return;
        }

        if (!string.Equals(token, _options.ClusterToken, StringComparison.Ordinal))
        {
            throw new HubException("unauthorized cluster token");
        }
    }

    private void TrackInstanceOwnership(ClusterCommandEnvelope command, ClusterCommandResult result)
    {
        var targetNodeId = (command.TargetNodeId ?? command.NodeId ?? string.Empty).Trim();
        if (string.Equals(command.Type, "instance.create", StringComparison.Ordinal)
            && result.Ok
            && result.Payload.ValueKind == JsonValueKind.Object
            && result.Payload.TryGetProperty("instance_id", out var instanceIdProp))
        {
            var instanceId = instanceIdProp.GetString();
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                if (result.Payload.TryGetProperty("summary", out var summaryElement))
                {
                    _remoteInstances.Upsert(ReadInstanceSummary(summaryElement, instanceId, targetNodeId.Length == 0 ? _options.NodeId : targetNodeId));
                    return;
                }
                _remoteInstances.Upsert(instanceId, targetNodeId.Length == 0 ? _options.NodeId : targetNodeId);
            }
            return;
        }

        if (string.Equals(command.Type, "instance.terminate", StringComparison.Ordinal)
            && result.Ok
            && command.Payload.ValueKind == JsonValueKind.Object
            && command.Payload.TryGetProperty("instance_id", out var terminatedIdProp))
        {
            var instanceId = terminatedIdProp.GetString();
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                _remoteInstances.Remove(instanceId);
            }
        }
    }

    private string NormalizeTargetNodeId(string? targetNodeId)
    {
        var normalized = (targetNodeId ?? string.Empty).Trim();
        return normalized.Length == 0 ? _options.NodeId : normalized;
    }

    private string RequireBoundSourceNode(string? sourceNodeId)
    {
        var normalized = NormalizeRequiredValue(sourceNodeId, "source_node_id");
        if (!_nodeRegistry.TryGetNodeByConnectionId(Context.ConnectionId, out var boundNode)
            || !string.Equals(boundNode.NodeId, normalized, StringComparison.Ordinal))
        {
            throw new HubException("source node mismatch");
        }

        return normalized;
    }

    private static string NormalizeRequiredValue(string? value, string name)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new HubException($"{name} is required");
        }

        return normalized;
    }

    private static object ConvertPayload(JsonElement payload)
    {
        var type = payload.TryGetProperty("type", out var typeValue) && typeValue.ValueKind == JsonValueKind.String
            ? typeValue.GetString()
            : string.Empty;

        if (string.Equals(type, "term.snapshot", StringComparison.Ordinal))
        {
            return new
            {
                v = 1,
                type = "term.snapshot",
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
                v = 1,
                type = "term.raw",
                instance_id = ReadString(payload, "instance_id"),
                node_id = ReadString(payload, "node_id"),
                node_name = ReadString(payload, "node_name"),
                seq = ReadLong(payload, "seq"),
                ts = ReadLong(payload, "ts"),
                replay = false,
                data = ReadString(payload, "data") ?? string.Empty
            };
        }

        if (string.Equals(type, "term.sync.complete", StringComparison.Ordinal))
        {
            return new
            {
                v = 1,
                type = "term.sync.complete",
                instance_id = ReadString(payload, "instance_id"),
                req_id = ReadString(payload, "req_id"),
                to_seq = ReadLong(payload, "to_seq"),
                ts = ReadLong(payload, "ts")
            };
        }

        if (string.Equals(type, "term.exit", StringComparison.Ordinal))
        {
            return new
            {
                v = 1,
                type = "term.exit",
                instance_id = ReadString(payload, "instance_id"),
                node_id = ReadString(payload, "node_id"),
                node_name = ReadString(payload, "node_name"),
                exit_code = ReadNullableLong(payload, "exit_code"),
                ts = ReadLong(payload, "ts")
            };
        }

        if (string.Equals(type, "term.owner.changed", StringComparison.Ordinal))
        {
            return new
            {
                v = 1,
                type = "term.owner.changed",
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

    private static string? ReadString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long ReadLong(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number)
            ? number
            : 0;
    }

    private static long? ReadNullableLong(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number)
            ? number
            : null;
    }

    private static JsonElement ReadElement(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var value) ? value : default;
    }

    private static InstanceSummary ReadInstanceSummary(JsonElement summary, string fallbackInstanceId, string fallbackNodeId)
    {
        return new InstanceSummary
        {
            Id = ReadString(summary, "id") ?? fallbackInstanceId,
            Command = ReadString(summary, "command") ?? "remote-shell",
            Cwd = ReadString(summary, "cwd") ?? string.Empty,
            Cols = ReadInt(summary, "cols"),
            Rows = ReadInt(summary, "rows"),
            CreatedAt = ReadString(summary, "created_at") ?? DateTimeOffset.UtcNow.ToString("O"),
            Status = ReadString(summary, "status") ?? "running",
            Clients = ReadInt(summary, "clients"),
            NodeId = ReadString(summary, "node_id") ?? fallbackNodeId,
            NodeName = ReadString(summary, "node_name") ?? fallbackNodeId,
            NodeRole = ReadString(summary, "node_role") ?? "slave",
            NodeOnline = ReadBool(summary, "node_online", true)
        };
    }

    private static int ReadInt(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static bool ReadBool(JsonElement payload, string propertyName, bool fallback)
    {
        return payload.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }
}
