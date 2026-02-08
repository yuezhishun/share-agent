namespace PtyAgent.Api.Runtime.Terminal;

public sealed record TerminalLaunchOptions(
    Guid TaskId,
    Guid SessionId,
    string CliType,
    string Mode,
    string Command,
    string Workdir,
    int Cols,
    int Rows);
