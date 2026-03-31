namespace TerminalGateway.Api.Infrastructure;

public sealed class GatewayOptions
{
    private static readonly string[] DefaultFsAllowedRoots = ["/home", "/workspace", "/www"];
    private static readonly string[] DefaultPathPrefixes = ["/www/server/nodejs/v22.22.0/bin"];

    public string GatewayRole { get; init; } = "master";
    public string? MasterUrl { get; init; }
    public bool SlaveViewOtherSlaves { get; init; } = true;
    public string NodeId { get; init; } = "master-local";
    public string NodeName { get; init; } = "master-local";
    public string? NodeLabel { get; init; }
    public string ClusterToken { get; init; } = "dev-cluster-token";
    public int NodeHeartbeatTimeoutSeconds { get; init; } = 15;
    public string Host { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 8080;
    public int HistoryLimit { get; init; } = 5000;
    public string FilesBasePath { get; init; } = "/home/yueyuan";
    public int LargeFileThresholdBytes { get; init; } = 100 * 1024;
    public int FileChunkBytes { get; init; } = 64 * 1024;
    public int FileChunkMaxLines { get; init; } = 800;
    public int RawReplayMaxBytes { get; init; } = 2 * 1024 * 1024;
    public int DefaultCols { get; init; } = 80;
    public int DefaultRows { get; init; } = 25;
    public string InternalToken { get; init; } = "dev-terminal-token";
    public string WsToken { get; init; } = "dev-ws-token";
    public string ProfileStoreFile { get; init; } = string.Empty;
    public string CliTemplateDbPath { get; init; } = "/tmp/pty-agent-cli-templates.db";
    public string SettingsStoreFile { get; init; } = "/tmp/pty-agent-terminal-settings.json";
    public int MaxOutputBufferBytes { get; init; } = 8 * 1024 * 1024;
    public int ProcessManagerMaxConcurrency { get; init; } = 4;
    public int RemoteInstanceCacheTtlSeconds { get; init; } = 30;
    public string GitBashPath { get; init; } = string.Empty;
    public string CodexConfigPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
    public string ClaudeConfigPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "config.json");
    public IReadOnlyList<string> FsAllowedRoots { get; init; } = DefaultFsAllowedRoots;
    public IReadOnlyList<string> PathPrefixes { get; init; } = DefaultPathPrefixes;

    public static GatewayOptions FromConfiguration(IConfiguration config)
    {
        var role = NormalizeRole(Read(config, "GATEWAY_ROLE", "Gateway:GatewayRole", "Gateway:Role"));
        var nodeId = Read(config, "NODE_ID", "Gateway:NodeId");
        nodeId = string.IsNullOrWhiteSpace(nodeId) ? $"node-{Environment.MachineName}".ToLowerInvariant() : nodeId;

        var nodeName = Read(config, "NODE_NAME", "Gateway:NodeName");
        nodeName = string.IsNullOrWhiteSpace(nodeName) ? nodeId : nodeName;

        return new GatewayOptions
        {
            GatewayRole = role,
            MasterUrl = Read(config, "MASTER_URL", "Gateway:MasterUrl"),
            SlaveViewOtherSlaves = ParseBool(config, true, "SLAVE_VIEW_OTHER_SLAVES", "Gateway:SlaveViewOtherSlaves"),
            NodeId = nodeId,
            NodeName = nodeName,
            NodeLabel = Read(config, "NODE_LABEL", "Gateway:NodeLabel"),
            ClusterToken = Read(config, "CLUSTER_TOKEN", "Gateway:ClusterToken") ?? "dev-cluster-token",
            NodeHeartbeatTimeoutSeconds = ParseInt(config, 15, "NODE_HEARTBEAT_TIMEOUT_SECONDS", "Gateway:NodeHeartbeatTimeoutSeconds"),
            Host = Read(config, "HOST", "Gateway:Host") ?? "0.0.0.0",
            Port = ParseInt(config, 8080, "PORT", "Gateway:Port"),
            HistoryLimit = ParseInt(config, 5000, "HISTORY_LIMIT", "Gateway:HistoryLimit"),
            FilesBasePath = Read(config, "FILES_BASE_PATH", "Gateway:FilesBasePath") ?? "/home/yueyuan",
            LargeFileThresholdBytes = ParseInt(config, 100 * 1024, "TERMINAL_LARGE_FILE_THRESHOLD_BYTES", "Gateway:LargeFileThresholdBytes"),
            FileChunkBytes = ParseInt(config, 64 * 1024, "TERMINAL_FILE_CHUNK_BYTES", "Gateway:FileChunkBytes"),
            FileChunkMaxLines = ParseInt(config, 800, "TERMINAL_FILE_CHUNK_MAX_LINES", "Gateway:FileChunkMaxLines"),
            RawReplayMaxBytes = ParseInt(config, 2 * 1024 * 1024, "RAW_REPLAY_MAX_BYTES", "Gateway:RawReplayMaxBytes"),
            DefaultCols = ParseInt(config, 80, "DEFAULT_COLS", "Gateway:DefaultCols"),
            DefaultRows = ParseInt(config, 25, "DEFAULT_ROWS", "Gateway:DefaultRows"),
            InternalToken = Read(config, "TERMINAL_GATEWAY_TOKEN", "Gateway:InternalToken") ?? "dev-terminal-token",
            WsToken = Read(config, "TERMINAL_WS_TOKEN", "Gateway:WsToken") ?? "dev-ws-token",
            ProfileStoreFile = Read(config, "TERMINAL_PROFILE_STORE_FILE", "Gateway:ProfileStoreFile") ?? string.Empty,
            CliTemplateDbPath = Read(config, "TERMINAL_CLI_TEMPLATE_DB_PATH", "Gateway:CliTemplateDbPath") ?? "/tmp/pty-agent-cli-templates.db",
            SettingsStoreFile = Read(config, "TERMINAL_SETTINGS_STORE_FILE", "Gateway:SettingsStoreFile") ?? "/tmp/pty-agent-terminal-settings.json",
            MaxOutputBufferBytes = ParseInt(config, 8 * 1024 * 1024, "TERMINAL_MAX_OUTPUT_BUFFER_BYTES", "Gateway:MaxOutputBufferBytes"),
            ProcessManagerMaxConcurrency = ParseInt(config, 4, "TERMINAL_PROCESS_MANAGER_MAX_CONCURRENCY", "Gateway:ProcessManagerMaxConcurrency"),
            RemoteInstanceCacheTtlSeconds = ParseInt(config, 30, "REMOTE_INSTANCE_CACHE_TTL_SECONDS", "Gateway:RemoteInstanceCacheTtlSeconds"),
            GitBashPath = Read(config, "TERMINAL_GIT_BASH_PATH", "Gateway:GitBashPath") ?? string.Empty,
            CodexConfigPath = Read(config, "TERMINAL_CODEX_CONFIG_PATH", "Gateway:CodexConfigPath") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml"),
            ClaudeConfigPath = Read(config, "TERMINAL_CLAUDE_CONFIG_PATH", "Gateway:ClaudeConfigPath") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "config.json"),
            FsAllowedRoots = ParseRoots(config),
            PathPrefixes = ParsePathPrefixes(config)
        };
    }

    public static GatewayOptions FromEnvironment(IConfiguration config) => FromConfiguration(config);

    private static string? Read(IConfiguration config, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = config[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static int ParseInt(IConfiguration config, int fallback, params string[] keys)
    {
        var raw = Read(config, keys);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static bool ParseBool(IConfiguration config, bool fallback, params string[] keys)
    {
        var raw = Read(config, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback
        };
    }

    private static string NormalizeRole(string? raw)
    {
        return string.Equals(raw, "slave", StringComparison.OrdinalIgnoreCase) ? "slave" : "master";
    }

    private static IReadOnlyList<string> ParseRoots(IConfiguration config)
    {
        var raw = Read(config, "TERMINAL_FS_ALLOWED_ROOTS", "Gateway:FsAllowedRoots");
        var parsedRaw = ParseRootsString(raw);
        if (parsedRaw.Count > 0)
        {
            return parsedRaw;
        }

        var parsedSection = ParseRootsValues(config.GetSection("Gateway:FsAllowedRoots")
            .GetChildren()
            .Select(x => x.Value));
        return parsedSection.Count > 0 ? parsedSection : DefaultFsAllowedRoots;
    }

    private static IReadOnlyList<string> ParseRootsString(string? raw)
    {
        return ParseRootsValues((raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim()));
    }

    private static IReadOnlyList<string> ParseRootsValues(IEnumerable<string?> roots)
    {
        return roots
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Where(Path.IsPathRooted)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> ParsePathPrefixes(IConfiguration config)
    {
        var raw = Read(config, "TERMINAL_PATH_PREFIXES", "Gateway:PathPrefixes");
        var parsedRaw = ParsePathPrefixString(raw);
        if (parsedRaw.Count > 0)
        {
            return parsedRaw;
        }

        var parsedSection = ParsePathPrefixValues(config.GetSection("Gateway:PathPrefixes")
            .GetChildren()
            .Select(x => x.Value));
        return parsedSection.Count > 0 ? parsedSection : DefaultPathPrefixes;
    }

    private static IReadOnlyList<string> ParsePathPrefixString(string? raw)
    {
        return ParsePathPrefixValues((raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim()));
    }

    private static IReadOnlyList<string> ParsePathPrefixValues(IEnumerable<string?> values)
    {
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
