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
    private readonly ClusterEventDeduplicator _events;
    private readonly RemoteInstanceRegistry _remoteInstances;
    private readonly IHubContext<TerminalHub> _terminalHub;

    public ClusterHub(
        GatewayOptions options,
        NodeRegistry nodeRegistry,
        ClusterCommandBroker broker,
        ClusterEventDeduplicator events,
        RemoteInstanceRegistry remoteInstances,
        IHubContext<TerminalHub> terminalHub)
    {
        _options = options;
        _nodeRegistry = nodeRegistry;
        _broker = broker;
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
}
