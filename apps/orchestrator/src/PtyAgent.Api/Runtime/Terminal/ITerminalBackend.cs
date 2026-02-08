namespace PtyAgent.Api.Runtime.Terminal;

public interface ITerminalBackend
{
    string Name { get; }
    Task<ITerminalSession> StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken);
}
