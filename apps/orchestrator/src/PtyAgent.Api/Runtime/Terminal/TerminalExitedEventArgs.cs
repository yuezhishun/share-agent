namespace PtyAgent.Api.Runtime.Terminal;

public sealed class TerminalExitedEventArgs : EventArgs
{
    public TerminalExitedEventArgs(int exitCode)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
