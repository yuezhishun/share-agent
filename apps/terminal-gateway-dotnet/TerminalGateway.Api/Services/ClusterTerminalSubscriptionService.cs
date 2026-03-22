using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using TerminalGateway.Api.Endpoints;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class ClusterTerminalSubscriptionService
{
    private readonly NodeRegistry _nodes;
    private readonly IHubContext<ClusterHub> _clusterHub;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _instanceSubscribers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _nodeSubscriptions = new(StringComparer.Ordinal);

    public ClusterTerminalSubscriptionService(InstanceManager manager, NodeRegistry nodes, IHubContext<ClusterHub> clusterHub)
    {
        _nodes = nodes;
        _clusterHub = clusterHub;

        manager.Raw += (_, payload) => EnqueueForward(payload);
        manager.Exited += (_, payload) => EnqueueForward(payload);
        manager.StateChanged += (_, payload) => EnqueueForward(payload);
    }

    public void Subscribe(string nodeId, string instanceId)
    {
        var normalizedNodeId = Normalize(nodeId);
        var normalizedInstanceId = Normalize(instanceId);
        if (normalizedNodeId.Length == 0 || normalizedInstanceId.Length == 0)
        {
            return;
        }

        var subscribers = _instanceSubscribers.GetOrAdd(normalizedInstanceId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        subscribers[normalizedNodeId] = 0;

        var instances = _nodeSubscriptions.GetOrAdd(normalizedNodeId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        instances[normalizedInstanceId] = 0;
    }

    public void Unsubscribe(string nodeId, string instanceId)
    {
        var normalizedNodeId = Normalize(nodeId);
        var normalizedInstanceId = Normalize(instanceId);
        if (normalizedNodeId.Length == 0 || normalizedInstanceId.Length == 0)
        {
            return;
        }

        if (_instanceSubscribers.TryGetValue(normalizedInstanceId, out var subscribers))
        {
            subscribers.TryRemove(normalizedNodeId, out _);
            if (subscribers.IsEmpty)
            {
                _instanceSubscribers.TryRemove(normalizedInstanceId, out _);
            }
        }

        if (_nodeSubscriptions.TryGetValue(normalizedNodeId, out var instances))
        {
            instances.TryRemove(normalizedInstanceId, out _);
            if (instances.IsEmpty)
            {
                _nodeSubscriptions.TryRemove(normalizedNodeId, out _);
            }
        }
    }

    public void RemoveNodeSubscriptions(string nodeId)
    {
        var normalizedNodeId = Normalize(nodeId);
        if (normalizedNodeId.Length == 0)
        {
            return;
        }

        if (!_nodeSubscriptions.TryRemove(normalizedNodeId, out var instances))
        {
            return;
        }

        foreach (var instanceId in instances.Keys)
        {
            if (_instanceSubscribers.TryGetValue(instanceId, out var subscribers))
            {
                subscribers.TryRemove(normalizedNodeId, out _);
                if (subscribers.IsEmpty)
                {
                    _instanceSubscribers.TryRemove(instanceId, out _);
                }
            }
        }
    }

    public Task ForwardClusterEventAsync(string instanceId, string sourceNodeId, JsonElement payload)
    {
        return ForwardAsync(instanceId, payload, sourceNodeId);
    }

    private void EnqueueForward(object payload)
    {
        var serialized = JsonSerializer.SerializeToElement(payload);
        var instanceId = ReadString(serialized, "instance_id");
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        _ = ForwardAsync(instanceId, serialized, excludeNodeId: null);
    }

    private async Task ForwardAsync(string instanceId, JsonElement payload, string? excludeNodeId)
    {
        var normalizedInstanceId = Normalize(instanceId);
        if (normalizedInstanceId.Length == 0 || !_instanceSubscribers.TryGetValue(normalizedInstanceId, out var subscribers))
        {
            return;
        }

        var type = ReadString(payload, "type") ?? "term.unknown";
        var seq = ReadLong(payload, "seq");
        var excluded = Normalize(excludeNodeId);
        foreach (var subscriberNodeId in subscribers.Keys)
        {
            if (excluded.Length > 0 && string.Equals(subscriberNodeId, excluded, StringComparison.Ordinal))
            {
                continue;
            }

            if (!_nodes.TryGetConnectionId(subscriberNodeId, out var connectionId))
            {
                continue;
            }

            try
            {
                await _clusterHub.Clients.Client(connectionId).SendAsync("ForwardTerminalEvent", new ClusterTerminalEventEnvelope
                {
                    EventId = $"fwd-{normalizedInstanceId}-{seq}-{Guid.NewGuid():N}",
                    NodeId = ReadString(payload, "node_id") ?? string.Empty,
                    InstanceId = normalizedInstanceId,
                    Seq = seq,
                    Ts = ReadLong(payload, "ts"),
                    Type = type,
                    Payload = payload
                });
            }
            catch
            {
            }
        }
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string? ReadString(JsonElement payload, string propertyName)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long ReadLong(JsonElement payload, string propertyName)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(propertyName, out var value)
            && value.TryGetInt64(out var number)
            ? number
            : 0;
    }
}
