namespace TerminalGateway.Api.Models;

public sealed class CreateSessionRequest
{
    public string? SessionId { get; set; }
    public string? TaskId { get; set; }
    public string? CliType { get; set; }
    public string? Mode { get; set; }
    public string? ProfileId { get; set; }
    public string? Title { get; set; }
    public string? Shell { get; set; }
    public string? Cwd { get; set; }
    public string? Command { get; set; }
    public string? WorkspaceRoot { get; set; }
    public List<string>? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public List<string>? StartupCommands { get; set; }
    public int? Cols { get; set; }
    public int? Rows { get; set; }
}

public sealed class SessionInputRequest
{
    public string? Data { get; set; }
}

public sealed class SessionResizeRequest
{
    public int? Cols { get; set; }
    public int? Rows { get; set; }
}

public sealed class SessionTerminateRequest
{
    public string? Signal { get; set; }
}

public class CreateProfileRequest
{
    public string? ProfileId { get; set; }
    public string? Name { get; set; }
    public string? CliType { get; set; }
    public string? Shell { get; set; }
    public string? Cwd { get; set; }
    public List<string>? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public List<string>? StartupCommands { get; set; }
    public List<QuickCommandItem>? QuickCommands { get; set; }
    public Dictionary<string, object>? CliOptions { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
}

public sealed class UpdateProfileRequest : CreateProfileRequest;

public sealed class SetQuickCommandsRequest
{
    public List<QuickCommandItem>? QuickCommands { get; set; }
}

public sealed class SetFsAllowedRootsRequest
{
    public List<string>? FsAllowedRoots { get; set; }
}
