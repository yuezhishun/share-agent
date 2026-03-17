using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
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

    public ClusterHub(
        GatewayOptions options,
        NodeRegistry nodeRegistry,
        ClusterCommandBroker broker,
        ClusterCommandExecutor executor,
        ClusterEventDeduplicator events,
        RemoteInstanceRegistry remoteInstances,
        IHubContext<TerminalHub> terminalHub)
    {
        _options = options;
        _nodeRegistry = nodeRegistry;
        _broker = broker;
        _executor = executor;
        _events = events;
        _remoteInstances = remoteInstances;
        _terminalHub = terminalHub;
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

        var targetNodeId = (request.TargetNodeId ?? string.Empty).Trim();
        if (!string.Equals(targetNodeId, _options.NodeId, StringComparison.Ordinal))
        {
            throw new HubException("reverse command target must be the current master node");
        }

        var command = new ClusterCommandEnvelope
        {
            CommandId = string.IsNullOrWhiteSpace(request.CommandId) ? Guid.NewGuid().ToString("N") : request.CommandId,
            NodeId = targetNodeId,
            SourceNodeId = sourceNodeId,
            TargetNodeId = targetNodeId,
            Type = string.IsNullOrWhiteSpace(request.Type) ? throw new HubException("type is required") : request.Type,
            Payload = request.Payload
        };

        var result = await _executor.ExecuteAsync(command, Context.ConnectionAborted);
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
            .SendAsync("TerminalEvent", envelope.Payload);

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
                    type = "term.route",
                    instance_id = instanceId,
                    node_id = nodeId,
                    node_name = boundNode.NodeName,
                    action = "resync_requested",
                    reason = "seq_gap",
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
        }
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
        if (!string.Equals(targetNodeId, _options.NodeId, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(command.Type, "instance.create", StringComparison.Ordinal)
            && result.Ok
            && result.Payload.ValueKind == JsonValueKind.Object
            && result.Payload.TryGetProperty("instance_id", out var instanceIdProp))
        {
            var instanceId = instanceIdProp.GetString();
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                _remoteInstances.Upsert(instanceId, _options.NodeId);
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
}
