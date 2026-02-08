using System.Collections.Concurrent;

namespace PtyAgent.Api.Services;

public sealed class DecisionInbox
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<DecisionPayload>> _pending = new();
    private readonly ConcurrentDictionary<Guid, DecisionPayload> _latest = new();

    public void Submit(Guid taskId, string decision, string? notes)
    {
        var payload = new DecisionPayload(decision, notes, DateTimeOffset.UtcNow);
        _latest[taskId] = payload;

        if (_pending.TryRemove(taskId, out var waiter))
        {
            waiter.TrySetResult(payload);
        }
    }

    public async Task<DecisionPayload?> WaitAsync(Guid taskId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_latest.TryRemove(taskId, out var existing))
        {
            return existing;
        }

        var tcs = new TaskCompletionSource<DecisionPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[taskId] = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));

        try
        {
            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(taskId, out _);
            return null;
        }
    }

    public bool TryTake(Guid taskId, out DecisionPayload? payload)
    {
        if (_latest.TryRemove(taskId, out var existing))
        {
            payload = existing;
            return true;
        }

        payload = null;
        return false;
    }
}

public sealed record DecisionPayload(string Decision, string? Notes, DateTimeOffset At);
