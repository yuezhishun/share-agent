using System.Collections.Concurrent;

namespace TerminalGateway.Api.Services;

public sealed class TerminalConnectionRegistry
{
    private readonly ConcurrentDictionary<string, string> _connectionToInstance = new(StringComparer.Ordinal);

    public string? GetInstance(string connectionId)
    {
        return _connectionToInstance.TryGetValue(connectionId, out var instanceId) ? instanceId : null;
    }

    public void Bind(string connectionId, string instanceId)
    {
        _connectionToInstance[connectionId] = instanceId;
    }

    public string? Unbind(string connectionId)
    {
        return _connectionToInstance.TryRemove(connectionId, out var instanceId) ? instanceId : null;
    }
}
