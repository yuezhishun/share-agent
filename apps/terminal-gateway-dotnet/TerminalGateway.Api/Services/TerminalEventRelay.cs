using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using TerminalGateway.Api.Endpoints;

namespace TerminalGateway.Api.Services;

public sealed class TerminalEventRelay
{
    private readonly IHubContext<TerminalHub> _hub;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _instanceGates = new(StringComparer.Ordinal);

    public TerminalEventRelay(InstanceManager manager, IHubContext<TerminalHub> hub)
    {
        _hub = hub;

        manager.Patch += (instanceId, payload) => Enqueue(instanceId, payload);
        manager.Raw += (instanceId, payload) => Enqueue(instanceId, payload);
        manager.Exited += (instanceId, payload) => Enqueue(instanceId, payload);
        manager.StateChanged += (instanceId, payload) => Enqueue(instanceId, payload);
    }

    private void Enqueue(string instanceId, object payload)
    {
        _ = EnqueueAsync(instanceId, payload);
    }

    private async Task EnqueueAsync(string instanceId, object payload)
    {
        var gate = _instanceGates.GetOrAdd(instanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await _hub.Clients.Group(TerminalHub.BuildInstanceGroup(instanceId)).SendAsync("TerminalEvent", payload);
        }
        catch
        {
        }
        finally
        {
            gate.Release();
        }
    }
}
