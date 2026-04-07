using System.Collections.Concurrent;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class RemoteInstanceRegistry
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, string> _instanceToNode = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CachedRemoteInstance> _summaries = new(StringComparer.Ordinal);

    public RemoteInstanceRegistry(GatewayOptions options, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _cacheTtl = TimeSpan.FromSeconds(Math.Max(1, options.RemoteInstanceCacheTtlSeconds));
    }

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
        _summaries[normalized.Id] = new CachedRemoteInstance(normalized, _timeProvider.GetUtcNow());
    }

    public void UpsertRange(IEnumerable<InstanceSummary> summaries)
    {
        if (summaries is null)
        {
            return;
        }

        foreach (var summary in summaries)
        {
            Upsert(summary);
        }
    }

    public void SyncNode(string nodeId, IEnumerable<InstanceSummary> summaries)
    {
        var normalizedNode = (nodeId ?? string.Empty).Trim();
        if (normalizedNode.Length == 0)
        {
            return;
        }

        var next = (summaries ?? Array.Empty<InstanceSummary>())
            .Where(summary => summary is not null && !string.IsNullOrWhiteSpace(summary.Id))
            .Select(summary =>
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
                    NodeId = normalizedNode,
                    NodeName = string.IsNullOrWhiteSpace(summary.NodeName) ? normalizedNode : summary.NodeName,
                    NodeRole = string.IsNullOrWhiteSpace(summary.NodeRole) ? "slave" : summary.NodeRole,
                    NodeOnline = summary.NodeOnline
                };
            })
            .ToList();

        var nextIds = new HashSet<string>(next.Select(item => item.Id), StringComparer.Ordinal);
        var staleIds = _summaries
            .Where(pair => string.Equals(pair.Value.Summary.NodeId, normalizedNode, StringComparison.Ordinal) && !nextIds.Contains(pair.Key))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var staleId in staleIds)
        {
            Remove(staleId);
        }

        UpsertRange(next);
    }

    public bool TryGetNode(string instanceId, out string nodeId)
    {
        return _instanceToNode.TryGetValue((instanceId ?? string.Empty).Trim(), out nodeId!);
    }

    public bool TryGetSummary(string instanceId, out InstanceSummary summary)
    {
        var normalized = (instanceId ?? string.Empty).Trim();
        if (_summaries.TryGetValue(normalized, out var cached))
        {
            summary = Clone(cached.Summary);
            return true;
        }

        summary = null!;
        return false;
    }

    public IReadOnlyList<InstanceSummary> List()
    {
        var cutoff = _timeProvider.GetUtcNow() - _cacheTtl;
        return _summaries
            .Where(pair => pair.Value.UpdatedAt >= cutoff)
            .Select(pair => Clone(pair.Value.Summary))
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

    private sealed record CachedRemoteInstance(InstanceSummary Summary, DateTimeOffset UpdatedAt);
}
