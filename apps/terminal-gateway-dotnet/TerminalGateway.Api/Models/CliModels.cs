using System.Text.Json.Serialization;

namespace TerminalGateway.Api.Models;

public sealed class CliTemplateRecord
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TemplateKind { get; set; } = "cli";
    public string CliType { get; set; } = "custom";
    public string Executable { get; set; } = string.Empty;
    public List<string> BaseArgs { get; set; } = [];
    public string DefaultCwd { get; set; } = string.Empty;
    public Dictionary<string, string> DefaultEnv { get; set; } = [];
    public List<string> EnvEntryIds { get; set; } = [];
    public List<string> EnvGroupNames { get; set; } = [];
    public List<string> SupportedOs { get; set; } = [];
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsBuiltin { get; set; }
    public bool IsDefault { get; set; }
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

public sealed class CreateCliTemplateRequest
{
    [JsonPropertyName("template_id")]
    public string? TemplateId { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("template_kind")]
    public string? TemplateKind { get; set; }
    [JsonPropertyName("cli_type")]
    public string? CliType { get; set; }
    [JsonPropertyName("executable")]
    public string? Executable { get; set; }
    [JsonPropertyName("base_args")]
    public List<string>? BaseArgs { get; set; }
    [JsonPropertyName("default_cwd")]
    public string? DefaultCwd { get; set; }
    [JsonPropertyName("default_env")]
    public Dictionary<string, string>? DefaultEnv { get; set; }
    [JsonPropertyName("env_entry_ids")]
    public List<string>? EnvEntryIds { get; set; }
    [JsonPropertyName("env_group_names")]
    public List<string>? EnvGroupNames { get; set; }
    [JsonPropertyName("supported_os")]
    public List<string>? SupportedOs { get; set; }
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
    [JsonPropertyName("color")]
    public string? Color { get; set; }
    [JsonPropertyName("is_default")]
    public bool? IsDefault { get; set; }
}

public sealed class UpdateCliTemplateRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("template_kind")]
    public string? TemplateKind { get; set; }
    [JsonPropertyName("cli_type")]
    public string? CliType { get; set; }
    [JsonPropertyName("executable")]
    public string? Executable { get; set; }
    [JsonPropertyName("base_args")]
    public List<string>? BaseArgs { get; set; }
    [JsonPropertyName("default_cwd")]
    public string? DefaultCwd { get; set; }
    [JsonPropertyName("default_env")]
    public Dictionary<string, string>? DefaultEnv { get; set; }
    [JsonPropertyName("env_entry_ids")]
    public List<string>? EnvEntryIds { get; set; }
    [JsonPropertyName("env_group_names")]
    public List<string>? EnvGroupNames { get; set; }
    [JsonPropertyName("supported_os")]
    public List<string>? SupportedOs { get; set; }
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
    [JsonPropertyName("color")]
    public string? Color { get; set; }
    [JsonPropertyName("is_default")]
    public bool? IsDefault { get; set; }
}

public sealed class TerminalEnvEntryRecord
{
    public string EnvId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string ValueType { get; set; } = "string";
    public object Value { get; set; } = string.Empty;
    public string GroupName { get; set; } = "general";
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

public sealed class CreateTerminalEnvEntryRequest
{
    [JsonPropertyName("env_id")]
    public string? EnvId { get; set; }
    [JsonPropertyName("key")]
    public string? Key { get; set; }
    [JsonPropertyName("value")]
    public object? Value { get; set; }
    [JsonPropertyName("group_name")]
    public string? GroupName { get; set; }
    [JsonPropertyName("sort_order")]
    public int? SortOrder { get; set; }
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}

public sealed class UpdateTerminalEnvEntryRequest
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }
    [JsonPropertyName("value")]
    public object? Value { get; set; }
    [JsonPropertyName("group_name")]
    public string? GroupName { get; set; }
    [JsonPropertyName("sort_order")]
    public int? SortOrder { get; set; }
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}

public sealed class StartCliProcessRequest
{
    [JsonPropertyName("template_id")]
    public string? TemplateId { get; set; }
    [JsonPropertyName("cwd_override")]
    public string? CwdOverride { get; set; }
    [JsonPropertyName("env_overrides")]
    public Dictionary<string, string>? EnvOverrides { get; set; }
    [JsonPropertyName("extra_args")]
    public List<string>? ExtraArgs { get; set; }
    [JsonPropertyName("label")]
    public string? Label { get; set; }
    [JsonPropertyName("timeout_ms")]
    public int? TimeoutMs { get; set; }
    [JsonPropertyName("node_id")]
    public string? NodeId { get; set; }
}

public sealed class StopCliProcessRequest
{
    [JsonPropertyName("force")]
    public bool? Force { get; set; }
}

public sealed class TerminalShortcutRecord
{
    public string ShortcutId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string GroupName { get; set; } = "custom";
    public bool PressEnter { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

public sealed class CreateTerminalShortcutRequest
{
    [JsonPropertyName("shortcut_id")]
    public string? ShortcutId { get; set; }
    [JsonPropertyName("label")]
    public string? Label { get; set; }
    [JsonPropertyName("command")]
    public string? Command { get; set; }
    [JsonPropertyName("group_name")]
    public string? GroupName { get; set; }
    [JsonPropertyName("press_enter")]
    public bool? PressEnter { get; set; }
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
    [JsonPropertyName("sort_order")]
    public int? SortOrder { get; set; }
}

public sealed class UpdateTerminalShortcutRequest
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }
    [JsonPropertyName("command")]
    public string? Command { get; set; }
    [JsonPropertyName("group_name")]
    public string? GroupName { get; set; }
    [JsonPropertyName("press_enter")]
    public bool? PressEnter { get; set; }
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
    [JsonPropertyName("sort_order")]
    public int? SortOrder { get; set; }
}
