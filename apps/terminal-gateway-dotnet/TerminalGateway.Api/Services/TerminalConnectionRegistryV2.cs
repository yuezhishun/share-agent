using System.Collections.Concurrent;

namespace TerminalGateway.Api.Services;

public sealed class TerminalConnectionRegistryV2
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connectionToInstances = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _instanceToConnections = new(StringComparer.Ordinal);

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

        var connections = _instanceToConnections.GetOrAdd(instanceId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        connections[connectionId] = 0;
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

        if (_instanceToConnections.TryGetValue(instanceId, out var connections))
        {
            connections.TryRemove(connectionId, out _);
            if (connections.IsEmpty)
            {
                _instanceToConnections.TryRemove(instanceId, out _);
            }
        }

        return removed;
    }

    public IReadOnlyList<string> UnbindAll(string connectionId)
    {
        if (_connectionToInstances.TryRemove(connectionId, out var instances))
        {
            var items = instances.Keys.ToList();
            foreach (var instanceId in items)
            {
                if (_instanceToConnections.TryGetValue(instanceId, out var connections))
                {
                    connections.TryRemove(connectionId, out _);
                    if (connections.IsEmpty)
                    {
                        _instanceToConnections.TryRemove(instanceId, out _);
                    }
                }
            }

            return items;
        }

        return Array.Empty<string>();
    }

    public IReadOnlyList<string> GetConnections(string instanceId)
    {
        if (!_instanceToConnections.TryGetValue(instanceId, out var connections))
        {
            return Array.Empty<string>();
        }

        return connections.Keys.ToList();
    }
}
