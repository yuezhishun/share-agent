using System.Text.Json;
using Microsoft.Data.Sqlite;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class TerminalEnvService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _dbPath;
    private readonly Lock _sync = new();

    public TerminalEnvService(string dbPath)
    {
        _dbPath = string.IsNullOrWhiteSpace(dbPath) ? "/tmp/pty-agent-cli-templates.db" : dbPath.Trim();
        EnsureDatabase();
    }

    public IReadOnlyList<TerminalEnvEntryRecord> List(string? group = null, string? search = null)
    {
        lock (_sync)
        {
            var normalizedGroup = NormalizeGroup(group, allowEmpty: true);
            var normalizedSearch = (search ?? string.Empty).Trim();
            var items = new List<TerminalEnvEntryRecord>();
            foreach (var item in ReadAll())
            {
                if (normalizedGroup.Length > 0 && !string.Equals(item.GroupName, normalizedGroup, StringComparison.Ordinal))
                {
                    continue;
                }

                if (normalizedSearch.Length > 0 && !item.Key.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(item);
            }

            return items;
        }
    }

    public TerminalEnvEntryRecord Create(CreateTerminalEnvEntryRequest request)
    {
        var item = NormalizeCreate(request);
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO terminal_env_entries (
                  env_id, key_name, value_json, group_name, sort_order, enabled, created_at, updated_at
                ) VALUES (
                  $envId, $keyName, $valueJson, $groupName, $sortOrder, $enabled, $createdAt, $updatedAt
                );
                """;
            Bind(insert, item);
            insert.Parameters.AddWithValue("$envId", item.EnvId);
            insert.Parameters.AddWithValue("$createdAt", item.CreatedAt);
            insert.Parameters.AddWithValue("$updatedAt", item.UpdatedAt);
            insert.ExecuteNonQuery();
            return Clone(item);
        }
    }

    public TerminalEnvEntryRecord Update(string envId, UpdateTerminalEnvEntryRequest request)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            var current = ReadById(connection, envId);
            if (current is null)
            {
                throw new InvalidOperationException($"terminal env not found: {envId}");
            }

            var merged = NormalizeMerged(current, request);
            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE terminal_env_entries
                SET key_name = $keyName,
                    value_json = $valueJson,
                    group_name = $groupName,
                    sort_order = $sortOrder,
                    enabled = $enabled,
                    updated_at = $updatedAt
                WHERE env_id = $envId;
                """;
            Bind(update, merged);
            update.Parameters.AddWithValue("$envId", merged.EnvId);
            update.Parameters.AddWithValue("$updatedAt", merged.UpdatedAt);
            var rows = update.ExecuteNonQuery();
            if (rows == 0)
            {
                throw new InvalidOperationException($"terminal env not found: {envId}");
            }

            return Clone(merged);
        }
    }

    public object Delete(string envId)
    {
        var normalizedEnvId = (envId ?? string.Empty).Trim();
        if (normalizedEnvId.Length == 0)
        {
            throw new InvalidOperationException("env_id is required");
        }

        lock (_sync)
        {
            using var connection = OpenConnection();
            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM terminal_env_entries WHERE env_id = $envId;";
            delete.Parameters.AddWithValue("$envId", normalizedEnvId);
            var rows = delete.ExecuteNonQuery();
            if (rows == 0)
            {
                throw new InvalidOperationException($"terminal env not found: {envId}");
            }

            return new { ok = true, env_id = normalizedEnvId };
        }
    }

    public Dictionary<string, string> ResolveEnvironment(IEnumerable<string>? groupNames, IEnumerable<string>? envEntryIds, string? nodeOs)
    {
        lock (_sync)
        {
            var requestedGroups = NormalizeList(groupNames).ToHashSet(StringComparer.Ordinal);
            var requestedIds = NormalizeList(envEntryIds).ToHashSet(StringComparer.Ordinal);
            if (requestedGroups.Count == 0 && requestedIds.Count == 0)
            {
                return [];
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in ReadAll())
            {
                if (!item.Enabled)
                {
                    continue;
                }

                var included = requestedGroups.Contains(item.GroupName) || requestedIds.Contains(item.EnvId);
                if (!included)
                {
                    continue;
                }

                result[item.Key] = ExpandValue(item, nodeOs);
            }

            return result;
        }
    }

    private string ExpandValue(TerminalEnvEntryRecord item, string? nodeOs)
    {
        if (string.Equals(item.ValueType, "array", StringComparison.Ordinal))
        {
            var values = item.Value as List<string> ?? [];
            return string.Join(NodeOsHelper.PathSeparatorFor(nodeOs), values);
        }

        return item.Value?.ToString() ?? string.Empty;
    }

    private void EnsureDatabase()
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS terminal_env_entries (
              env_id TEXT PRIMARY KEY,
              key_name TEXT NOT NULL,
              value_json TEXT NOT NULL,
              group_name TEXT NOT NULL,
              sort_order INTEGER NOT NULL DEFAULT 0,
              enabled INTEGER NOT NULL DEFAULT 1,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureSeeds(connection);
    }

    private void EnsureSeeds(SqliteConnection connection)
    {
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(1) FROM terminal_env_entries;";
        var existing = Convert.ToInt32(count.ExecuteScalar() ?? 0);
        if (existing > 0)
        {
            return;
        }

        foreach (var item in BuildSeeds(NodeOsHelper.Current))
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO terminal_env_entries (
                  env_id, key_name, value_json, group_name, sort_order, enabled, created_at, updated_at
                ) VALUES (
                  $envId, $keyName, $valueJson, $groupName, $sortOrder, $enabled, $createdAt, $updatedAt
                );
                """;
            Bind(insert, item);
            insert.Parameters.AddWithValue("$envId", item.EnvId);
            insert.Parameters.AddWithValue("$createdAt", item.CreatedAt);
            insert.Parameters.AddWithValue("$updatedAt", item.UpdatedAt);
            insert.ExecuteNonQuery();
        }
    }

    private static IReadOnlyList<TerminalEnvEntryRecord> BuildSeeds(string nodeOs)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var isWindows = string.Equals(NodeOsHelper.Normalize(nodeOs), "windows", StringComparison.Ordinal);
        var pathValues = isWindows
            ? new List<string> { @"C:\Program Files\nodejs", @"%APPDATA%\npm" }
            : new List<string> { "/usr/local/bin", "~/.npm-global/bin" };

        return
        [
            CreateSeed("seed-nodejs-node-env", "NODE_ENV", "development", "nodejs", 10, now),
            CreateSeed("seed-nodejs-npm-color", "NPM_CONFIG_COLOR", "always", "nodejs", 20, now),
            CreateSeed("seed-nodejs-force-color", "FORCE_COLOR", "1", "nodejs", 30, now),
            CreateSeed("seed-nodejs-path", "PATH", pathValues, "nodejs", 40, now),

            CreateSeed("seed-dotnet-env", "DOTNET_ENVIRONMENT", "Development", "dotnet", 10, now),
            CreateSeed("seed-dotnet-aspnet-env", "ASPNETCORE_ENVIRONMENT", "Development", "dotnet", 20, now),
            CreateSeed("seed-dotnet-telemetry", "DOTNET_CLI_TELEMETRY_OPTOUT", "1", "dotnet", 30, now),
            CreateSeed("seed-dotnet-nologo", "DOTNET_NOLOGO", "1", "dotnet", 40, now),

            CreateSeed("seed-codex-api-key", "OPENAI_API_KEY", "", "codex", 10, now),
            CreateSeed("seed-codex-base-url", "OPENAI_BASE_URL", "", "codex", 20, now),
            CreateSeed("seed-codex-home", "CODEX_HOME", isWindows ? @"D:\workspace\code\ai-agent\share-agent" : "/workspace/ai-agent/share-agent", "codex", 30, now),

            CreateSeed("seed-claude-api-key", "ANTHROPIC_API_KEY", "", "claude", 10, now),
            CreateSeed("seed-claude-base-url", "ANTHROPIC_BASE_URL", "", "claude", 20, now)
        ];
    }

    private static TerminalEnvEntryRecord CreateSeed(string envId, string key, string value, string groupName, int sortOrder, string now)
    {
        return new TerminalEnvEntryRecord
        {
            EnvId = envId,
            Key = key,
            ValueType = "string",
            Value = value,
            GroupName = groupName,
            SortOrder = sortOrder,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static TerminalEnvEntryRecord CreateSeed(string envId, string key, List<string> value, string groupName, int sortOrder, string now)
    {
        return new TerminalEnvEntryRecord
        {
            EnvId = envId,
            Key = key,
            ValueType = "array",
            Value = value,
            GroupName = groupName,
            SortOrder = sortOrder,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private static TerminalEnvEntryRecord? ReadById(SqliteConnection connection, string envId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT env_id, key_name, value_json, group_name, sort_order, enabled, created_at, updated_at
            FROM terminal_env_entries
            WHERE env_id = $envId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$envId", (envId ?? string.Empty).Trim());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    private List<TerminalEnvEntryRecord> ReadAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT env_id, key_name, value_json, group_name, sort_order, enabled, created_at, updated_at
            FROM terminal_env_entries
            ORDER BY group_name ASC, sort_order ASC, key_name ASC, env_id ASC;
            """;
        using var reader = command.ExecuteReader();
        var items = new List<TerminalEnvEntryRecord>();
        while (reader.Read())
        {
            items.Add(ReadRecord(reader));
        }

        return items;
    }

    private static TerminalEnvEntryRecord ReadRecord(SqliteDataReader reader)
    {
        var (valueType, value) = DeserializeValue(reader.GetString(2));
        return new TerminalEnvEntryRecord
        {
            EnvId = reader.GetString(0),
            Key = reader.GetString(1),
            ValueType = valueType,
            Value = value,
            GroupName = NormalizeGroup(reader.GetString(3), allowEmpty: false),
            SortOrder = reader.GetInt32(4),
            Enabled = !reader.IsDBNull(5) && reader.GetInt64(5) != 0,
            CreatedAt = reader.GetString(6),
            UpdatedAt = reader.GetString(7)
        };
    }

    private static void Bind(SqliteCommand command, TerminalEnvEntryRecord item)
    {
        command.Parameters.AddWithValue("$keyName", item.Key);
        command.Parameters.AddWithValue("$valueJson", SerializeValue(item.ValueType, item.Value));
        command.Parameters.AddWithValue("$groupName", item.GroupName);
        command.Parameters.AddWithValue("$sortOrder", item.SortOrder);
        command.Parameters.AddWithValue("$enabled", item.Enabled ? 1 : 0);
    }

    private static TerminalEnvEntryRecord NormalizeCreate(CreateTerminalEnvEntryRequest request)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var (valueType, value) = NormalizeValue(request.Value);
        var key = NormalizeKey(request.Key);
        if (key.Length == 0)
        {
            throw new InvalidOperationException("env key is required");
        }

        return new TerminalEnvEntryRecord
        {
            EnvId = string.IsNullOrWhiteSpace(request.EnvId) ? Guid.NewGuid().ToString("N") : request.EnvId.Trim(),
            Key = key,
            ValueType = valueType,
            Value = value,
            GroupName = NormalizeGroup(request.GroupName, allowEmpty: false),
            SortOrder = request.SortOrder ?? 0,
            Enabled = request.Enabled != false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static TerminalEnvEntryRecord NormalizeMerged(TerminalEnvEntryRecord current, UpdateTerminalEnvEntryRequest request)
    {
        var (valueType, value) = request.Value is null
            ? (current.ValueType, CloneValue(current.ValueType, current.Value))
            : NormalizeValue(request.Value);

        var key = request.Key is null ? current.Key : NormalizeKey(request.Key);
        if (key.Length == 0)
        {
            throw new InvalidOperationException("env key is required");
        }

        return new TerminalEnvEntryRecord
        {
            EnvId = current.EnvId,
            Key = key,
            ValueType = valueType,
            Value = value,
            GroupName = request.GroupName is null ? current.GroupName : NormalizeGroup(request.GroupName, allowEmpty: false),
            SortOrder = request.SortOrder ?? current.SortOrder,
            Enabled = request.Enabled ?? current.Enabled,
            CreatedAt = current.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    private static TerminalEnvEntryRecord Clone(TerminalEnvEntryRecord item)
    {
        return new TerminalEnvEntryRecord
        {
            EnvId = item.EnvId,
            Key = item.Key,
            ValueType = item.ValueType,
            Value = CloneValue(item.ValueType, item.Value),
            GroupName = item.GroupName,
            SortOrder = item.SortOrder,
            Enabled = item.Enabled,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    private static object CloneValue(string valueType, object value)
    {
        return string.Equals(valueType, "array", StringComparison.Ordinal)
            ? new List<string>((value as List<string>) ?? [])
            : value?.ToString() ?? string.Empty;
    }

    private static string NormalizeKey(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string NormalizeGroup(string? value, bool allowEmpty)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return allowEmpty ? string.Empty : "general";
        }

        return normalized;
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        return values?
            .Select(item => (item ?? string.Empty).Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
    }

    private static (string ValueType, object Value) NormalizeValue(object? raw)
    {
        return raw switch
        {
            null => ("string", string.Empty),
            JsonElement element => NormalizeJsonElementValue(element),
            string text => ("string", text),
            IEnumerable<string> items => ("array", items.Select(item => (item ?? string.Empty).Trim()).Where(item => item.Length > 0).ToList()),
            _ => ("string", raw.ToString() ?? string.Empty)
        };
    }

    private static (string ValueType, object Value) NormalizeJsonElementValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => ("array", element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => (item.GetString() ?? string.Empty).Trim())
                .Where(item => item.Length > 0)
                .ToList()),
            JsonValueKind.String => ("string", element.GetString() ?? string.Empty),
            _ => throw new InvalidOperationException("env value must be string or string array")
        };
    }

    private static string SerializeValue(string valueType, object value)
    {
        return string.Equals(valueType, "array", StringComparison.Ordinal)
            ? JsonSerializer.Serialize((value as List<string>) ?? [], JsonOptions)
            : JsonSerializer.Serialize(value?.ToString() ?? string.Empty, JsonOptions);
    }

    private static (string ValueType, object Value) DeserializeValue(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return NormalizeJsonElementValue(document.RootElement);
        }
        catch
        {
            return ("string", string.Empty);
        }
    }
}
