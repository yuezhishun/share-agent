using System.Collections.Concurrent;

namespace TerminalGateway.Api.Services;

public sealed class RemoteInstanceRegistry
{
    private readonly ConcurrentDictionary<string, string> _instanceToNode = new(StringComparer.Ordinal);

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

    public bool TryGetNode(string instanceId, out string nodeId)
    {
        return _instanceToNode.TryGetValue((instanceId ?? string.Empty).Trim(), out nodeId!);
    }

    public void Remove(string instanceId)
    {
        _instanceToNode.TryRemove((instanceId ?? string.Empty).Trim(), out _);
    }
}
