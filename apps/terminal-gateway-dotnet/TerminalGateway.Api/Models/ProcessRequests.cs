namespace TerminalGateway.Api.Models;

public sealed class ProcessCommandSpec
{
    public string? File { get; set; }
    public List<string>? Args { get; set; }
}

public sealed class RunProcessRequest
{
    public string? File { get; set; }
    public List<string>? Args { get; set; }
    public string? Cwd { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public string? Stdin { get; set; }
    public int? TimeoutMs { get; set; }
    public bool? AllowNonZeroExitCode { get; set; }
    public List<ProcessCommandSpec>? Pipeline { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public sealed class StopManagedProcessRequest
{
    public bool? Force { get; set; }
}
