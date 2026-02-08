using System.Threading.Channels;

namespace PtyAgent.Api.Orchestration;

public sealed class TaskQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid taskId) => _channel.Writer.WriteAsync(taskId);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAsync(cancellationToken);
}
