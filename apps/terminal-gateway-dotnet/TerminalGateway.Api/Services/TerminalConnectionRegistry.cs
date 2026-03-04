using System.Collections.Concurrent;

namespace TerminalGateway.Api.Services;

public sealed class TerminalConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connectionToInstances = new(StringComparer.Ordinal);

    public IReadOnlyList<string> GetInstances(string connectionId)
    {
        if (!_connectionToInstances.TryGetValue(connectionId, out var instances))
        {
            return Array.Empty<string>();
        }

        return instances.Keys.ToList();
    }

    public void Bind(string connectionId, string instanceId)
    {
        var instances = _connectionToInstances.GetOrAdd(connectionId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        instances[instanceId] = 0;
    }

    public bool Unbind(string connectionId, string instanceId)
    {
        if (!_connectionToInstances.TryGetValue(connectionId, out var instances))
        {
            return false;
        }

        var removed = instances.TryRemove(instanceId, out _);
        if (instances.IsEmpty)
        {
            _connectionToInstances.TryRemove(connectionId, out _);
        }
        return removed;
    }

    public IReadOnlyList<string> UnbindAll(string connectionId)
    {
        if (_connectionToInstances.TryRemove(connectionId, out var instances))
        {
            return instances.Keys.ToList();
        }

        return Array.Empty<string>();
    }
}
