using System.Collections.Concurrent;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class RemoteInstanceRegistry
{
    private readonly ConcurrentDictionary<string, string> _instanceToNode = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, InstanceSummary> _summaries = new(StringComparer.Ordinal);

    public void Upsert(string instanceId, string nodeId)
    {
        var normalizedInstance = (instanceId ?? string.Empty).Trim();
        var normalizedNode = (nodeId ?? string.Empty).Trim();
        if (normalizedInstance.Length == 0 || normalizedNode.Length == 0)
        {
            return;
        }

        _instanceToNode[normalizedInstance] = normalizedNode;
    }

    public void Upsert(InstanceSummary summary)
    {
        if (summary is null || string.IsNullOrWhiteSpace(summary.Id) || string.IsNullOrWhiteSpace(summary.NodeId))
        {
            return;
        }

        var normalized = Clone(summary);
        _instanceToNode[normalized.Id] = normalized.NodeId;
        _summaries[normalized.Id] = normalized;
    }

    public bool TryGetNode(string instanceId, out string nodeId)
    {
        return _instanceToNode.TryGetValue((instanceId ?? string.Empty).Trim(), out nodeId!);
    }

    public IReadOnlyList<InstanceSummary> List()
    {
        return _summaries.Values
            .Select(Clone)
            .OrderByDescending(x => x.CreatedAt, StringComparer.Ordinal)
            .ToList();
    }

    public void Remove(string instanceId)
    {
        var normalized = (instanceId ?? string.Empty).Trim();
        _instanceToNode.TryRemove(normalized, out _);
        _summaries.TryRemove(normalized, out _);
    }

    private static InstanceSummary Clone(InstanceSummary summary)
    {
        return new InstanceSummary
        {
            Id = summary.Id,
            Command = summary.Command,
            Cwd = summary.Cwd,
            Cols = summary.Cols,
            Rows = summary.Rows,
            CreatedAt = summary.CreatedAt,
            Status = summary.Status,
            Clients = summary.Clients,
            NodeId = summary.NodeId,
            NodeName = summary.NodeName,
            NodeRole = summary.NodeRole,
            NodeOnline = summary.NodeOnline
        };
    }
}
