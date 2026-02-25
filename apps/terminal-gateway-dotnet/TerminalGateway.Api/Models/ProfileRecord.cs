namespace TerminalGateway.Api.Models;

public sealed class ProfileRecord
{
    public string ProfileId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CliType { get; set; } = "custom";
    public string Shell { get; set; } = "/bin/bash";
    public string Cwd { get; set; } = "/tmp";
    public List<string> Args { get; set; } = [];
    public Dictionary<string, string> Env { get; set; } = [];
    public List<string> StartupCommands { get; set; } = [];
    public List<QuickCommandItem> QuickCommands { get; set; } = [];
    public Dictionary<string, object> CliOptions { get; set; } = [];
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsBuiltin { get; set; }
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

public sealed class QuickCommandItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SendMode { get; set; } = "auto";
    public bool Enabled { get; set; } = true;
    public int Order { get; set; }
}
