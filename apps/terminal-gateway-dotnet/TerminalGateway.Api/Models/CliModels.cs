namespace TerminalGateway.Api.Models;

public sealed class CliTemplateRecord
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CliType { get; set; } = "custom";
    public string Executable { get; set; } = string.Empty;
    public List<string> BaseArgs { get; set; } = [];
    public string DefaultCwd { get; set; } = string.Empty;
    public Dictionary<string, string> DefaultEnv { get; set; } = [];
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsBuiltin { get; set; }
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

public sealed class CreateCliTemplateRequest
{
    public string? TemplateId { get; set; }
    public string? Name { get; set; }
    public string? CliType { get; set; }
    public string? Executable { get; set; }
    public List<string>? BaseArgs { get; set; }
    public string? DefaultCwd { get; set; }
    public Dictionary<string, string>? DefaultEnv { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
}

public sealed class UpdateCliTemplateRequest
{
    public string? Name { get; set; }
    public string? CliType { get; set; }
    public string? Executable { get; set; }
    public List<string>? BaseArgs { get; set; }
    public string? DefaultCwd { get; set; }
    public Dictionary<string, string>? DefaultEnv { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
}

public sealed class StartCliProcessRequest
{
    public string? TemplateId { get; set; }
    public string? CwdOverride { get; set; }
    public Dictionary<string, string>? EnvOverrides { get; set; }
    public List<string>? ExtraArgs { get; set; }
    public string? Label { get; set; }
    public int? TimeoutMs { get; set; }
    public string? NodeId { get; set; }
}

public sealed class StopCliProcessRequest
{
    public bool? Force { get; set; }
}
