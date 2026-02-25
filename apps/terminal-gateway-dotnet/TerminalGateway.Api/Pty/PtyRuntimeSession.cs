namespace TerminalGateway.Api.Pty;

public interface IPtyRuntimeSession : IAsyncDisposable
{
    int Pid { get; }
    event Action<string>? OutputReceived;
    event Action<int?>? Exited;
    Task WriteAsync(string data, CancellationToken cancellationToken = default);
    Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default);
    Task TerminateAsync(string signal, CancellationToken cancellationToken = default);
}
