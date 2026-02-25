using System.Net;

namespace TerminalGateway.Api.Infrastructure;

public sealed class GatewayOptions
{
    public int Port { get; init; } = 7300;
    public string Host { get; init; } = "0.0.0.0";
    public string InternalToken { get; init; } = "dev-terminal-token";
    public string WsToken { get; init; } = "dev-ws-token";
    public string ProfileStoreFile { get; init; } = string.Empty;
    public string SettingsStoreFile { get; init; } = "/tmp/pty-agent-terminal-settings.json";
    public int MaxOutputBufferBytes { get; init; } = 8 * 1024 * 1024;
    public string CodexConfigPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
    public string ClaudeConfigPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "config.json");
    public IReadOnlyList<string> FsAllowedRoots { get; init; } = new[] { "/home", "/workspace", "/www" };

    public static GatewayOptions FromEnvironment(IConfiguration config)
    {
        static int ParsePositiveInt(string? raw, int fallback)
            => int.TryParse(raw, out var value) && value > 0 ? value : fallback;

        static IReadOnlyList<string> ParseRoots(string? raw)
        {
            var rows = (raw ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(Path.IsPathRooted)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return rows;
        }

        var configuredRoots = ParseRoots(config["TERMINAL_FS_ALLOWED_ROOTS"]);
        return new GatewayOptions
        {
            Port = int.TryParse(config["PORT"], out var port) ? port : 7300,
            Host = config["HOST"] ?? "0.0.0.0",
            InternalToken = config["TERMINAL_GATEWAY_TOKEN"] ?? "dev-terminal-token",
            WsToken = config["TERMINAL_WS_TOKEN"] ?? "dev-ws-token",
            ProfileStoreFile = config["TERMINAL_PROFILE_STORE_FILE"] ?? string.Empty,
            SettingsStoreFile = config["TERMINAL_SETTINGS_STORE_FILE"] ?? "/tmp/pty-agent-terminal-settings.json",
            MaxOutputBufferBytes = ParsePositiveInt(config["TERMINAL_MAX_OUTPUT_BUFFER_BYTES"], 8 * 1024 * 1024),
            CodexConfigPath = config["TERMINAL_CODEX_CONFIG_PATH"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml"),
            ClaudeConfigPath = config["TERMINAL_CLAUDE_CONFIG_PATH"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "config.json"),
            FsAllowedRoots = configuredRoots.Count > 0 ? configuredRoots : new[] { "/home", "/workspace", "/www" }
        };
    }
}
