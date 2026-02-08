namespace PtyAgent.Api.Runtime.Terminal;

public interface ITerminalSession : IAsyncDisposable
{
    event EventHandler<TerminalExitedEventArgs>? Exited;

    int Pid { get; }
    string BackendName { get; }
    TextReader OutputReader { get; }
    TextReader? ErrorReader { get; }

    Task SendInputAsync(string input, CancellationToken cancellationToken);
    Task TerminateAsync(CancellationToken cancellationToken);
}
