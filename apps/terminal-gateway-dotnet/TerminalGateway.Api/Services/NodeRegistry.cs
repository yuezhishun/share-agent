using System.Collections.Concurrent;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class NodeRegistry
{
    private readonly GatewayOptions _options;
    private readonly ConcurrentDictionary<string, RemoteNodeState> _remoteNodes = new(StringComparer.Ordinal);

    public NodeRegistry(GatewayOptions options)
    {
        _options = options;
    }

    public void RegisterRemoteNode(ClusterRegisterNodeRequest request, string connectionId)
    {
        var nodeId = NormalizeNodeId(request.NodeId);
        var now = DateTimeOffset.UtcNow;
        _remoteNodes.AddOrUpdate(nodeId,
            _ => new RemoteNodeState
            {
                NodeId = nodeId,
                NodeName = NormalizeNodeName(request.NodeName, nodeId),
                NodeLabel = NormalizeLabel(request.NodeLabel),
                NodeRole = "slave",
                NodeOs = NormalizeNodeOs(request.NodeOs),
                InstanceCount = Math.Max(0, request.InstanceCount ?? 0),
                LastSeenAt = now,
                ConnectionId = connectionId
            },
            (_, existing) =>
            {
                existing.NodeName = NormalizeNodeName(request.NodeName, nodeId);
                existing.NodeLabel = NormalizeLabel(request.NodeLabel);
                existing.NodeRole = "slave";
                existing.NodeOs = NormalizeNodeOs(request.NodeOs);
                existing.InstanceCount = Math.Max(0, request.InstanceCount ?? existing.InstanceCount);
                existing.LastSeenAt = now;
                existing.ConnectionId = connectionId;
                return existing;
            });
    }

    public void Heartbeat(ClusterHeartbeatRequest request, string connectionId)
    {
        var nodeId = NormalizeNodeId(request.NodeId);
        var now = DateTimeOffset.UtcNow;
        _remoteNodes.AddOrUpdate(nodeId,
            _ => new RemoteNodeState
            {
                NodeId = nodeId,
                NodeName = nodeId,
                NodeRole = "slave",
                NodeOs = NormalizeNodeOs(request.NodeOs),
                InstanceCount = Math.Max(0, request.InstanceCount ?? 0),
                LastSeenAt = now,
                ConnectionId = connectionId
            },
            (_, existing) =>
            {
                existing.NodeOs = NormalizeNodeOs(request.NodeOs);
                existing.InstanceCount = Math.Max(0, request.InstanceCount ?? existing.InstanceCount);
                existing.LastSeenAt = now;
                existing.ConnectionId = connectionId;
                return existing;
            });
    }

    public void MarkDisconnected(string connectionId)
    {
        foreach (var kv in _remoteNodes)
        {
            if (!string.Equals(kv.Value.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                continue;
            }

            kv.Value.ConnectionId = string.Empty;
        }
    }

    public IReadOnlyList<NodeSummary> ListNodes(int localInstanceCount)
    {
        var now = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.NodeHeartbeatTimeoutSeconds));
        var items = new List<NodeSummary>
        {
            new()
            {
                NodeId = _options.NodeId,
                NodeName = _options.NodeName,
                NodeRole = _options.GatewayRole,
                NodeOs = NodeOsHelper.Current,
                NodeLabel = _options.NodeLabel,
                IsCurrent = true,
                NodeOnline = true,
                InstanceCount = localInstanceCount,
                LastSeenAt = now.ToString("O")
            }
        };

        foreach (var state in _remoteNodes.Values.OrderBy(x => x.NodeId, StringComparer.Ordinal))
        {
            var online = state.ConnectionId.Length > 0 && (now - state.LastSeenAt) <= timeout;
            items.Add(new NodeSummary
            {
                NodeId = state.NodeId,
                NodeName = state.NodeName,
                NodeRole = state.NodeRole,
                NodeOs = state.NodeOs,
                NodeLabel = state.NodeLabel,
                IsCurrent = false,
                NodeOnline = online,
                InstanceCount = state.InstanceCount,
                LastSeenAt = state.LastSeenAt.ToString("O")
            });
        }

        return items;
    }

    public bool TryGetConnectionId(string nodeId, out string connectionId)
    {
        connectionId = string.Empty;
        var key = NormalizeNodeId(nodeId);
        if (!_remoteNodes.TryGetValue(key, out var state))
        {
            return false;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.NodeHeartbeatTimeoutSeconds));
        var online = state.ConnectionId.Length > 0 && (DateTimeOffset.UtcNow - state.LastSeenAt) <= timeout;
        if (!online)
        {
            return false;
        }

        connectionId = state.ConnectionId;
        return true;
    }

    public bool TryGetNodeByConnectionId(string connectionId, out NodeSummary node)
    {
        node = null!;
        var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.NodeHeartbeatTimeoutSeconds));
        var now = DateTimeOffset.UtcNow;
        foreach (var state in _remoteNodes.Values)
        {
            if (!string.Equals(state.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                continue;
            }

            var online = (now - state.LastSeenAt) <= timeout;
            node = new NodeSummary
            {
                NodeId = state.NodeId,
                NodeName = state.NodeName,
                NodeRole = state.NodeRole,
                NodeOs = state.NodeOs,
                NodeLabel = state.NodeLabel,
                IsCurrent = false,
                NodeOnline = online,
                InstanceCount = state.InstanceCount,
                LastSeenAt = state.LastSeenAt.ToString("O")
            };
            return true;
        }

        return false;
    }

    private static string NormalizeNodeId(string? nodeId)
    {
        var value = (nodeId ?? string.Empty).Trim();
        return value.Length == 0 ? "unknown-node" : value;
    }

    private static string NormalizeNodeName(string? nodeName, string fallback)
    {
        var value = (nodeName ?? string.Empty).Trim();
        return value.Length == 0 ? fallback : value;
    }

    private static string? NormalizeLabel(string? nodeLabel)
    {
        var value = (nodeLabel ?? string.Empty).Trim();
        return value.Length == 0 ? null : value;
    }

    private static string NormalizeNodeOs(string? nodeOs)
    {
        return NodeOsHelper.Normalize(nodeOs);
    }

    private sealed class RemoteNodeState
    {
        public required string NodeId { get; init; }
        public required string NodeRole { get; set; }
        public required string NodeName { get; set; }
        public required string NodeOs { get; set; }
        public string? NodeLabel { get; set; }
        public required int InstanceCount { get; set; }
        public required DateTimeOffset LastSeenAt { get; set; }
        public required string ConnectionId { get; set; }
    }
}
