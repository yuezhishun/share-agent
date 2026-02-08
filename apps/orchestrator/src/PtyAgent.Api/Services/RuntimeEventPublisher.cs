using Microsoft.AspNetCore.SignalR;
using PtyAgent.Api.Domain;
using PtyAgent.Api.Hubs;
using PtyAgent.Api.Infrastructure;

namespace PtyAgent.Api.Services;

public sealed class RuntimeEventPublisher
{
    private readonly SqliteStore _store;
    private readonly IHubContext<RuntimeHub> _hub;

    public RuntimeEventPublisher(SqliteStore store, IHubContext<RuntimeHub> hub)
    {
        _store = store;
        _hub = hub;
    }

    public async Task PublishAsync(Guid taskId, Guid? sessionId, string eventType, string severity, string payload)
    {
        var evt = new ProgressEvent(Guid.NewGuid(), taskId, sessionId, eventType, severity, payload, DateTimeOffset.UtcNow);
        await _store.InsertEventAsync(evt);
        await _hub.Clients.All.SendAsync("runtime_event", evt);
    }
}
