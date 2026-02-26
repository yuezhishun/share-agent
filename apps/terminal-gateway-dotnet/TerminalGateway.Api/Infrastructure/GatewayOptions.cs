namespace TerminalGateway.Api.Infrastructure;

public sealed class GatewayOptions
{
    public string GatewayRole { get; init; } = "master";
    public string? MasterUrl { get; init; }
    public string NodeId { get; init; } = "master-local";
    public string NodeName { get; init; } = "master-local";
    public string? NodeLabel { get; init; }
    public string ClusterToken { get; init; } = "dev-cluster-token";
    public int NodeHeartbeatTimeoutSeconds { get; init; } = 15;
    public string Host { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 8080;
    public int HistoryLimit { get; init; } = 1000;
    public string FilesBasePath { get; init; } = "/home/yueyuan";
    public int RawReplayMaxBytes { get; init; } = 512 * 1024;
    public int DefaultCols { get; init; } = 80;
    public int DefaultRows { get; init; } = 25;
    public string InternalToken { get; init; } = "dev-terminal-token";
    public string WsToken { get; init; } = "dev-ws-token";
    public string ProfileStoreFile { get; init; } = string.Empty;
    public string SettingsStoreFile { get; init; } = "/tmp/pty-agent-terminal-settings.json";
    public int MaxOutputBufferBytes { get; init; } = 8 * 1024 * 1024;
    public string CodexConfigPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
    public string ClaudeConfigPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "config.json");
    public IReadOnlyList<string> FsAllowedRoots { get; init; } = ["/home", "/workspace", "/www"];

    public static GatewayOptions FromConfiguration(IConfiguration config)
    {
        var role = NormalizeRole(config["GATEWAY_ROLE"]);
        var nodeId = string.IsNullOrWhiteSpace(config["NODE_ID"]) ? $"node-{Environment.MachineName}".ToLowerInvariant() : config["NODE_ID"]!.Trim();
        var nodeName = string.IsNullOrWhiteSpace(config["NODE_NAME"]) ? nodeId : config["NODE_NAME"]!.Trim();

        return new GatewayOptions
        {
            GatewayRole = role,
            MasterUrl = config["MASTER_URL"],
            NodeId = nodeId,
            NodeName = nodeName,
            NodeLabel = string.IsNullOrWhiteSpace(config["NODE_LABEL"]) ? null : config["NODE_LABEL"]!.Trim(),
            ClusterToken = config["CLUSTER_TOKEN"] ?? "dev-cluster-token",
            NodeHeartbeatTimeoutSeconds = ParseInt(config["NODE_HEARTBEAT_TIMEOUT_SECONDS"], 15),
            Host = config["HOST"] ?? "0.0.0.0",
            Port = ParseInt(config["PORT"], 8080),
            HistoryLimit = ParseInt(config["HISTORY_LIMIT"], 1000),
            FilesBasePath = config["FILES_BASE_PATH"] ?? "/home/yueyuan",
            RawReplayMaxBytes = ParseInt(config["RAW_REPLAY_MAX_BYTES"], 512 * 1024),
            DefaultCols = ParseInt(config["DEFAULT_COLS"], 80),
            DefaultRows = ParseInt(config["DEFAULT_ROWS"], 25),
            InternalToken = config["TERMINAL_GATEWAY_TOKEN"] ?? "dev-terminal-token",
            WsToken = config["TERMINAL_WS_TOKEN"] ?? "dev-ws-token",
            ProfileStoreFile = config["TERMINAL_PROFILE_STORE_FILE"] ?? string.Empty,
            SettingsStoreFile = config["TERMINAL_SETTINGS_STORE_FILE"] ?? "/tmp/pty-agent-terminal-settings.json",
            MaxOutputBufferBytes = ParseInt(config["TERMINAL_MAX_OUTPUT_BUFFER_BYTES"], 8 * 1024 * 1024),
            CodexConfigPath = config["TERMINAL_CODEX_CONFIG_PATH"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml"),
            ClaudeConfigPath = config["TERMINAL_CLAUDE_CONFIG_PATH"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "config.json"),
            FsAllowedRoots = ParseRoots(config["TERMINAL_FS_ALLOWED_ROOTS"])
        };
    }

    public static GatewayOptions FromEnvironment(IConfiguration config) => FromConfiguration(config);

    private static int ParseInt(string? raw, int fallback)
    {
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static string NormalizeRole(string? raw)
    {
        return string.Equals(raw, "slave", StringComparison.OrdinalIgnoreCase) ? "slave" : "master";
    }

    private static IReadOnlyList<string> ParseRoots(string? raw)
    {
        var rows = (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathRooted)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return rows.Count > 0 ? rows : ["/home", "/workspace", "/www"];
    }
}
