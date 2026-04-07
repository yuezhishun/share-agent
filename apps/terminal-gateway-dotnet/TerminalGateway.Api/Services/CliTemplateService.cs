using System.Text.Json;
using Microsoft.Data.Sqlite;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class CliTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedCliTypes = ["bash", "codex", "custom"];
    private static readonly HashSet<string> AllowedTemplateKinds = ["cli", "terminal"];

    private readonly string _dbPath;
    private readonly string _defaultCwd;
    private readonly string _nodeOs;
    private readonly Dictionary<string, CliTemplateRecord> _builtinTemplates;
    private readonly Lock _sync = new();

    public CliTemplateService(string dbPath, string defaultCwd)
    {
        _dbPath = string.IsNullOrWhiteSpace(dbPath) ? "/tmp/pty-agent-cli-templates.db" : dbPath.Trim();
        _defaultCwd = string.IsNullOrWhiteSpace(defaultCwd) ? "/tmp" : defaultCwd.Trim();
        _nodeOs = NodeOsHelper.Current;
        _builtinTemplates = BuildBuiltins(_defaultCwd)
            .ToDictionary(x => x.TemplateId, Clone, StringComparer.Ordinal);
        EnsureDatabase();
    }

    public IReadOnlyList<CliTemplateRecord> List(string? templateKind = null)
    {
        lock (_sync)
        {
            var normalizedTemplateKind = NormalizeTemplateKindFilter(templateKind);
            var items = _builtinTemplates.Values
                .Select(Clone)
                .Concat(ReadCustomTemplates())
                .Where(x => normalizedTemplateKind.Length == 0 || string.Equals(x.TemplateKind, normalizedTemplateKind, StringComparison.Ordinal))
                .OrderByDescending(x => x.IsBuiltin)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return items;
        }
    }

    public CliTemplateRecord GetRequired(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException("template_id is required");
        }

        lock (_sync)
        {
            if (_builtinTemplates.TryGetValue(templateId.Trim(), out var builtin))
            {
                return Clone(builtin);
            }

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT template_id, name, cli_type, executable, base_args_json, default_cwd, default_env_json, description, icon, color, created_at, updated_at
                     , template_kind, is_default, env_entry_ids_json, env_group_names_json, supported_os_json
                FROM cli_templates
                WHERE template_id = $templateId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$templateId", templateId.Trim());
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException($"template not found: {templateId}");
            }

            return ReadRecord(reader, isBuiltin: false);
        }
    }

    public CliTemplateRecord Create(CreateCliTemplateRequest request)
    {
        var template = NormalizeCreate(request);
        Validate(template, checkBuiltin: false);

        lock (_sync)
        {
            if (_builtinTemplates.ContainsKey(template.TemplateId))
            {
                throw new InvalidOperationException($"template already exists: {template.TemplateId}");
            }

            using var connection = OpenConnection();
            using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(1) FROM cli_templates WHERE template_id = $templateId;";
            exists.Parameters.AddWithValue("$templateId", template.TemplateId);
            var count = Convert.ToInt32(exists.ExecuteScalar() ?? 0);
            if (count > 0)
            {
                throw new InvalidOperationException($"template already exists: {template.TemplateId}");
            }

            Insert(connection, template);
            return Clone(template);
        }
    }

    public CliTemplateRecord Update(string templateId, UpdateCliTemplateRequest request)
    {
        lock (_sync)
        {
            if (_builtinTemplates.ContainsKey(templateId))
            {
                throw new InvalidOperationException("builtin template is read-only");
            }

            using var connection = OpenConnection();
            var current = ReadCustomTemplate(connection, templateId);
            if (current is null)
            {
                throw new InvalidOperationException($"template not found: {templateId}");
            }

            if (current.IsBuiltin)
            {
                throw new InvalidOperationException("builtin template is read-only");
            }

            var merged = new CliTemplateRecord
            {
                TemplateId = current.TemplateId,
                Name = (request.Name ?? current.Name).Trim(),
                TemplateKind = NormalizeTemplateKind(request.TemplateKind ?? current.TemplateKind),
                CliType = NormalizeCliType(request.CliType ?? current.CliType),
                Executable = (request.Executable ?? current.Executable).Trim(),
                BaseArgs = NormalizeStrings(request.BaseArgs ?? current.BaseArgs),
                DefaultCwd = (request.DefaultCwd ?? current.DefaultCwd).Trim(),
                DefaultEnv = NormalizeEnv(request.DefaultEnv ?? current.DefaultEnv),
                EnvEntryIds = NormalizeStrings(request.EnvEntryIds ?? current.EnvEntryIds),
                EnvGroupNames = NormalizeStrings(request.EnvGroupNames ?? current.EnvGroupNames),
                SupportedOs = NormalizeSupportedOs(request.SupportedOs ?? current.SupportedOs),
                Description = (request.Description ?? current.Description).Trim(),
                Icon = (request.Icon ?? current.Icon).Trim(),
                Color = (request.Color ?? current.Color).Trim(),
                IsBuiltin = false,
                IsDefault = request.IsDefault ?? current.IsDefault,
                CreatedAt = current.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
            };

            Validate(merged, checkBuiltin: false);

            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE cli_templates
                SET name = $name,
                    template_kind = $templateKind,
                    cli_type = $cliType,
                    executable = $executable,
                    base_args_json = $baseArgsJson,
                    default_cwd = $defaultCwd,
                    default_env_json = $defaultEnvJson,
                    env_entry_ids_json = $envEntryIdsJson,
                    env_group_names_json = $envGroupNamesJson,
                    supported_os_json = $supportedOsJson,
                    description = $description,
                    icon = $icon,
                    color = $color,
                    is_default = $isDefault,
                    updated_at = $updatedAt
                WHERE template_id = $templateId;
                """;
            Bind(update, merged);
            update.Parameters.AddWithValue("$templateId", merged.TemplateId);
            update.Parameters.AddWithValue("$updatedAt", merged.UpdatedAt);
            var rows = update.ExecuteNonQuery();
            if (rows == 0)
            {
                throw new InvalidOperationException($"template not found: {templateId}");
            }

            EnsureSingleDefault(connection, merged);
            return Clone(merged);
        }
    }

    public object Delete(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException("template_id is required");
        }

        lock (_sync)
        {
            if (_builtinTemplates.ContainsKey(templateId.Trim()))
            {
                throw new InvalidOperationException("builtin template cannot be deleted");
            }

            using var connection = OpenConnection();
            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM cli_templates WHERE template_id = $templateId;";
            delete.Parameters.AddWithValue("$templateId", templateId.Trim());
            var rows = delete.ExecuteNonQuery();
            if (rows == 0)
            {
                throw new InvalidOperationException($"template not found: {templateId}");
            }

            return new { ok = true, template_id = templateId.Trim() };
        }
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
            CREATE TABLE IF NOT EXISTS cli_templates (
              template_id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              template_kind TEXT NOT NULL DEFAULT 'cli',
              cli_type TEXT NOT NULL,
              executable TEXT NOT NULL,
              base_args_json TEXT NOT NULL,
              default_cwd TEXT NOT NULL,
              default_env_json TEXT NOT NULL,
              env_entry_ids_json TEXT NOT NULL DEFAULT '[]',
              env_group_names_json TEXT NOT NULL DEFAULT '[]',
              supported_os_json TEXT NOT NULL DEFAULT '[]',
              description TEXT NOT NULL,
              icon TEXT NOT NULL,
              color TEXT NOT NULL,
              is_default INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "template_kind", "TEXT NOT NULL DEFAULT 'cli'");
        EnsureColumn(connection, "is_default", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "env_entry_ids_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureColumn(connection, "env_group_names_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureColumn(connection, "supported_os_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureTerminalTemplateSeeds(connection);
        EnsureTerminalDefault(connection);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private List<CliTemplateRecord> ReadCustomTemplates()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT template_id, name, cli_type, executable, base_args_json, default_cwd, default_env_json, description, icon, color, created_at, updated_at
                 , template_kind, is_default, env_entry_ids_json, env_group_names_json, supported_os_json
            FROM cli_templates;
            """;
        using var reader = command.ExecuteReader();
        var items = new List<CliTemplateRecord>();
        while (reader.Read())
        {
            items.Add(ReadRecord(reader, isBuiltin: false));
        }

        return items;
    }

    private static CliTemplateRecord? ReadCustomTemplate(SqliteConnection connection, string templateId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT template_id, name, cli_type, executable, base_args_json, default_cwd, default_env_json, description, icon, color, created_at, updated_at
                 , template_kind, is_default, env_entry_ids_json, env_group_names_json, supported_os_json
            FROM cli_templates
            WHERE template_id = $templateId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$templateId", templateId.Trim());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader, isBuiltin: false) : null;
    }

    private static CliTemplateRecord ReadRecord(SqliteDataReader reader, bool isBuiltin)
    {
        return new CliTemplateRecord
        {
            TemplateId = reader.GetString(0),
            Name = reader.GetString(1),
            CliType = reader.GetString(2),
            Executable = reader.GetString(3),
            BaseArgs = DeserializeList(reader.GetString(4)),
            DefaultCwd = reader.GetString(5),
            DefaultEnv = DeserializeMap(reader.GetString(6)),
            EnvEntryIds = reader.FieldCount > 14 ? DeserializeList(reader.GetString(14)) : [],
            EnvGroupNames = reader.FieldCount > 15 ? DeserializeList(reader.GetString(15)) : [],
            SupportedOs = reader.FieldCount > 16 ? NormalizeSupportedOs(DeserializeList(reader.GetString(16))) : [],
            Description = reader.GetString(7),
            Icon = reader.GetString(8),
            Color = reader.GetString(9),
            IsBuiltin = isBuiltin,
            CreatedAt = reader.GetString(10),
            UpdatedAt = reader.GetString(11),
            TemplateKind = reader.FieldCount > 12 ? NormalizeTemplateKind(reader.GetString(12)) : "cli",
            IsDefault = reader.FieldCount > 13 && !reader.IsDBNull(13) && reader.GetInt64(13) != 0
        };
    }

    private static void Insert(SqliteConnection connection, CliTemplateRecord template)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO cli_templates (
              template_id, name, template_kind, cli_type, executable, base_args_json, default_cwd, default_env_json, env_entry_ids_json, env_group_names_json, supported_os_json, description, icon, color, is_default, created_at, updated_at
            ) VALUES (
              $templateId, $name, $templateKind, $cliType, $executable, $baseArgsJson, $defaultCwd, $defaultEnvJson, $envEntryIdsJson, $envGroupNamesJson, $supportedOsJson, $description, $icon, $color, $isDefault, $createdAt, $updatedAt
            );
            """;
        Bind(insert, template);
        insert.Parameters.AddWithValue("$templateId", template.TemplateId);
        insert.Parameters.AddWithValue("$createdAt", template.CreatedAt);
        insert.Parameters.AddWithValue("$updatedAt", template.UpdatedAt);
        insert.ExecuteNonQuery();
        EnsureSingleDefault(connection, template);
    }

    private static void Bind(SqliteCommand command, CliTemplateRecord template)
    {
        command.Parameters.AddWithValue("$name", template.Name);
        command.Parameters.AddWithValue("$templateKind", template.TemplateKind);
        command.Parameters.AddWithValue("$cliType", template.CliType);
        command.Parameters.AddWithValue("$executable", template.Executable);
        command.Parameters.AddWithValue("$baseArgsJson", JsonSerializer.Serialize(template.BaseArgs, JsonOptions));
        command.Parameters.AddWithValue("$defaultCwd", template.DefaultCwd);
        command.Parameters.AddWithValue("$defaultEnvJson", JsonSerializer.Serialize(template.DefaultEnv, JsonOptions));
        command.Parameters.AddWithValue("$envEntryIdsJson", JsonSerializer.Serialize(template.EnvEntryIds, JsonOptions));
        command.Parameters.AddWithValue("$envGroupNamesJson", JsonSerializer.Serialize(template.EnvGroupNames, JsonOptions));
        command.Parameters.AddWithValue("$supportedOsJson", JsonSerializer.Serialize(template.SupportedOs, JsonOptions));
        command.Parameters.AddWithValue("$description", template.Description);
        command.Parameters.AddWithValue("$icon", template.Icon);
        command.Parameters.AddWithValue("$color", template.Color);
        command.Parameters.AddWithValue("$isDefault", template.IsDefault ? 1 : 0);
    }

    private CliTemplateRecord NormalizeCreate(CreateCliTemplateRequest request)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return new CliTemplateRecord
        {
            TemplateId = string.IsNullOrWhiteSpace(request.TemplateId) ? Guid.NewGuid().ToString("N") : request.TemplateId.Trim(),
            Name = (request.Name ?? string.Empty).Trim(),
            TemplateKind = NormalizeTemplateKind(request.TemplateKind),
            CliType = NormalizeCliType(request.CliType),
            Executable = (request.Executable ?? string.Empty).Trim(),
            BaseArgs = NormalizeStrings(request.BaseArgs ?? []),
            DefaultCwd = string.IsNullOrWhiteSpace(request.DefaultCwd) ? _defaultCwd : request.DefaultCwd.Trim(),
            DefaultEnv = NormalizeEnv(request.DefaultEnv ?? []),
            EnvEntryIds = NormalizeStrings(request.EnvEntryIds ?? []),
            EnvGroupNames = NormalizeStrings(request.EnvGroupNames ?? []),
            SupportedOs = NormalizeSupportedOs(request.SupportedOs ?? []),
            Description = (request.Description ?? string.Empty).Trim(),
            Icon = (request.Icon ?? string.Empty).Trim(),
            Color = (request.Color ?? string.Empty).Trim(),
            IsBuiltin = false,
            IsDefault = request.IsDefault == true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static void Validate(CliTemplateRecord template, bool checkBuiltin)
    {
        if (checkBuiltin && template.IsBuiltin)
        {
            throw new InvalidOperationException("builtin template is read-only");
        }
        if (string.IsNullOrWhiteSpace(template.TemplateId))
        {
            throw new InvalidOperationException("template_id is required");
        }
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            throw new InvalidOperationException("template name is required");
        }
        if (!AllowedTemplateKinds.Contains(template.TemplateKind))
        {
            throw new InvalidOperationException($"unsupported template_kind: {template.TemplateKind}");
        }
        if (string.IsNullOrWhiteSpace(template.Executable))
        {
            throw new InvalidOperationException("template executable is required");
        }
        if (string.IsNullOrWhiteSpace(template.DefaultCwd))
        {
            throw new InvalidOperationException("template default_cwd is required");
        }
        if (!AllowedCliTypes.Contains(template.CliType))
        {
            throw new InvalidOperationException($"unsupported cli_type: {template.CliType}");
        }
        if (template.SupportedOs.Any(value => value is not ("windows" or "linux")))
        {
            throw new InvalidOperationException("supported_os only accepts windows/linux");
        }
    }

    private static string NormalizeCliType(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "custom" : value.Trim().ToLowerInvariant();
        return AllowedCliTypes.Contains(normalized) ? normalized : "custom";
    }

    private static string NormalizeTemplateKind(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "cli" : value.Trim().ToLowerInvariant();
        return AllowedTemplateKinds.Contains(normalized) ? normalized : "cli";
    }

    private static string NormalizeTemplateKindFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return AllowedTemplateKinds.Contains(normalized) ? normalized : string.Empty;
    }

    private static List<string> NormalizeStrings(IEnumerable<string> items)
    {
        return items.Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).ToList();
    }

    private static Dictionary<string, string> NormalizeEnv(IEnumerable<KeyValuePair<string, string>> items)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in items)
        {
            var key = (kv.Key ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                continue;
            }

            output[key] = kv.Value ?? string.Empty;
        }

        return output;
    }

    private static List<string> NormalizeSupportedOs(IEnumerable<string> items)
    {
        return items
            .Select(NodeOsHelper.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> DeserializeList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, string> DeserializeMap(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<CliTemplateRecord> BuildBuiltins(string defaultCwd)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return
        [
            new CliTemplateRecord
            {
                TemplateId = "builtin-bash",
                Name = "Bash Shell",
                TemplateKind = "cli",
                CliType = "bash",
                Executable = "bash",
                BaseArgs = [],
                DefaultCwd = defaultCwd,
                Description = "Run a local bash command or shell entrypoint.",
                Icon = "terminal",
                Color = "#1ea7a4",
                SupportedOs = ["linux"],
                IsBuiltin = true,
                IsDefault = false,
                CreatedAt = now,
                UpdatedAt = now
            },
            new CliTemplateRecord
            {
                TemplateId = "builtin-codex",
                Name = "Codex CLI",
                TemplateKind = "cli",
                CliType = "codex",
                Executable = "codex",
                BaseArgs = [],
                DefaultCwd = defaultCwd,
                Description = "Launch the local Codex CLI with extra args.",
                Icon = "bot",
                Color = "#3a90e5",
                SupportedOs = ["linux", "windows"],
                IsBuiltin = true,
                IsDefault = false,
                CreatedAt = now,
                UpdatedAt = now
            },
            new CliTemplateRecord
            {
                TemplateId = "builtin-bash-echo",
                Name = "Bash Echo Example",
                TemplateKind = "cli",
                CliType = "bash",
                Executable = "bash",
                BaseArgs = ["-lc", "echo hello from cli template"],
                DefaultCwd = defaultCwd,
                Description = "Example template for validating the CLI process pipeline.",
                Icon = "flask",
                Color = "#ff9f1a",
                SupportedOs = ["linux"],
                IsBuiltin = true,
                IsDefault = false,
                CreatedAt = now,
                UpdatedAt = now
            }
        ];
    }

    private IReadOnlyList<CliTemplateRecord> BuildTerminalSeeds()
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        if (string.Equals(_nodeOs, "windows", StringComparison.Ordinal))
        {
            return
            [
                BuildTerminalSeed("seed-terminal-powershell", "PowerShell", "powershell.exe", ["-NoLogo"], now, true),
                BuildTerminalSeed("seed-terminal-cmd", "CMD", "cmd.exe", [], now, false),
                BuildTerminalSeed("seed-terminal-node-repl", "Node REPL", "node", [], now, false, ["nodejs"]),
                BuildTerminalSeed("seed-terminal-codex-cli", "Codex CLI", "codex", [], now, false, ["codex"]),
                BuildTerminalSeed("seed-terminal-claude-cli", "Claude CLI", "claude", [], now, false, ["claude"])
            ];
        }

        return
        [
            BuildTerminalSeed("seed-terminal-bash-interactive", "Bash Interactive", "bash", ["-i"], now, true),
            BuildTerminalSeed("seed-terminal-bash-login", "Bash Login", "bash", ["-l"], now, false),
            BuildTerminalSeed("seed-terminal-node-repl", "Node REPL", "node", [], now, false, ["nodejs"]),
            BuildTerminalSeed("seed-terminal-codex-cli", "Codex CLI", "codex", [], now, false, ["codex"]),
            BuildTerminalSeed("seed-terminal-claude-cli", "Claude CLI", "claude", [], now, false, ["claude"])
        ];
    }

    private CliTemplateRecord BuildTerminalSeed(
        string templateId,
        string name,
        string executable,
        List<string> baseArgs,
        string now,
        bool isDefault,
        List<string>? envGroupNames = null)
    {
        return new CliTemplateRecord
        {
            TemplateId = templateId,
            Name = name,
            TemplateKind = "terminal",
            CliType = "custom",
            Executable = executable,
            BaseArgs = baseArgs,
            DefaultCwd = _defaultCwd,
            DefaultEnv = [],
            EnvEntryIds = [],
            EnvGroupNames = envGroupNames ?? [],
            SupportedOs = [_nodeOs],
            Description = "terminal",
            Icon = "terminal",
            Color = "#0e639c",
            IsBuiltin = false,
            IsDefault = isDefault,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static CliTemplateRecord Clone(CliTemplateRecord template)
    {
        return new CliTemplateRecord
        {
            TemplateId = template.TemplateId,
            Name = template.Name,
            TemplateKind = template.TemplateKind,
            CliType = template.CliType,
            Executable = template.Executable,
            BaseArgs = [.. template.BaseArgs],
            DefaultCwd = template.DefaultCwd,
            DefaultEnv = new Dictionary<string, string>(template.DefaultEnv, StringComparer.Ordinal),
            EnvEntryIds = [.. template.EnvEntryIds],
            EnvGroupNames = [.. template.EnvGroupNames],
            SupportedOs = [.. template.SupportedOs],
            Description = template.Description,
            Icon = template.Icon,
            Color = template.Color,
            IsBuiltin = template.IsBuiltin,
            IsDefault = template.IsDefault,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }

    private static void EnsureColumn(SqliteConnection connection, string columnName, string definition)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(cli_templates);";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE cli_templates ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }

    private void EnsureTerminalTemplateSeeds(SqliteConnection connection)
    {
        using var count = connection.CreateCommand();
        count.CommandText = """
            SELECT COUNT(1)
            FROM cli_templates
            WHERE template_kind = 'terminal';
            """;
        var terminalCount = Convert.ToInt32(count.ExecuteScalar() ?? 0);
        if (terminalCount > 0)
        {
            return;
        }

        foreach (var template in BuildTerminalSeeds())
        {
            Insert(connection, template);
        }
    }

    private static void EnsureTerminalDefault(SqliteConnection connection)
    {
        using var count = connection.CreateCommand();
        count.CommandText = """
            SELECT COUNT(1)
            FROM cli_templates
            WHERE template_kind = 'terminal'
              AND is_default = 1;
            """;
        var defaultCount = Convert.ToInt32(count.ExecuteScalar() ?? 0);
        if (defaultCount > 0)
        {
            return;
        }

        using var find = connection.CreateCommand();
        find.CommandText = """
            SELECT template_id
            FROM cli_templates
            WHERE template_kind = 'terminal'
            ORDER BY created_at ASC, template_id ASC
            LIMIT 1;
            """;
        var templateId = Convert.ToString(find.ExecuteScalar() ?? string.Empty)?.Trim();
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return;
        }

        using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE cli_templates
            SET is_default = 1
            WHERE template_id = $templateId;
            """;
        update.Parameters.AddWithValue("$templateId", templateId);
        update.ExecuteNonQuery();
    }

    private static void EnsureSingleDefault(SqliteConnection connection, CliTemplateRecord template)
    {
        if (!template.IsDefault)
        {
            return;
        }

        using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE cli_templates
            SET is_default = 0
            WHERE template_kind = $templateKind
              AND template_id <> $templateId;
            """;
        update.Parameters.AddWithValue("$templateKind", template.TemplateKind);
        update.Parameters.AddWithValue("$templateId", template.TemplateId);
        update.ExecuteNonQuery();
    }
}
