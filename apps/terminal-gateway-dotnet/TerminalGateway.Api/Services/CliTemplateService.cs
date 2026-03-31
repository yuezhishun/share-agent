using System.Text.Json;
using Microsoft.Data.Sqlite;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class CliTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedCliTypes = ["bash", "codex", "custom"];

    private readonly string _dbPath;
    private readonly string _defaultCwd;
    private readonly Dictionary<string, CliTemplateRecord> _builtinTemplates;
    private readonly Lock _sync = new();

    public CliTemplateService(string dbPath, string defaultCwd)
    {
        _dbPath = string.IsNullOrWhiteSpace(dbPath) ? "/tmp/pty-agent-cli-templates.db" : dbPath.Trim();
        _defaultCwd = string.IsNullOrWhiteSpace(defaultCwd) ? "/tmp" : defaultCwd.Trim();
        _builtinTemplates = BuildBuiltins(_defaultCwd)
            .ToDictionary(x => x.TemplateId, Clone, StringComparer.Ordinal);
        EnsureDatabase();
    }

    public IReadOnlyList<CliTemplateRecord> List()
    {
        lock (_sync)
        {
            var items = _builtinTemplates.Values
                .Select(Clone)
                .Concat(ReadCustomTemplates())
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
                CliType = NormalizeCliType(request.CliType ?? current.CliType),
                Executable = (request.Executable ?? current.Executable).Trim(),
                BaseArgs = NormalizeStrings(request.BaseArgs ?? current.BaseArgs),
                DefaultCwd = (request.DefaultCwd ?? current.DefaultCwd).Trim(),
                DefaultEnv = NormalizeEnv(request.DefaultEnv ?? current.DefaultEnv),
                Description = (request.Description ?? current.Description).Trim(),
                Icon = (request.Icon ?? current.Icon).Trim(),
                Color = (request.Color ?? current.Color).Trim(),
                IsBuiltin = false,
                CreatedAt = current.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
            };

            Validate(merged, checkBuiltin: false);

            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE cli_templates
                SET name = $name,
                    cli_type = $cliType,
                    executable = $executable,
                    base_args_json = $baseArgsJson,
                    default_cwd = $defaultCwd,
                    default_env_json = $defaultEnvJson,
                    description = $description,
                    icon = $icon,
                    color = $color,
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
              cli_type TEXT NOT NULL,
              executable TEXT NOT NULL,
              base_args_json TEXT NOT NULL,
              default_cwd TEXT NOT NULL,
              default_env_json TEXT NOT NULL,
              description TEXT NOT NULL,
              icon TEXT NOT NULL,
              color TEXT NOT NULL,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
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
            Description = reader.GetString(7),
            Icon = reader.GetString(8),
            Color = reader.GetString(9),
            IsBuiltin = isBuiltin,
            CreatedAt = reader.GetString(10),
            UpdatedAt = reader.GetString(11)
        };
    }

    private static void Insert(SqliteConnection connection, CliTemplateRecord template)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO cli_templates (
              template_id, name, cli_type, executable, base_args_json, default_cwd, default_env_json, description, icon, color, created_at, updated_at
            ) VALUES (
              $templateId, $name, $cliType, $executable, $baseArgsJson, $defaultCwd, $defaultEnvJson, $description, $icon, $color, $createdAt, $updatedAt
            );
            """;
        Bind(insert, template);
        insert.Parameters.AddWithValue("$templateId", template.TemplateId);
        insert.Parameters.AddWithValue("$createdAt", template.CreatedAt);
        insert.Parameters.AddWithValue("$updatedAt", template.UpdatedAt);
        insert.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, CliTemplateRecord template)
    {
        command.Parameters.AddWithValue("$name", template.Name);
        command.Parameters.AddWithValue("$cliType", template.CliType);
        command.Parameters.AddWithValue("$executable", template.Executable);
        command.Parameters.AddWithValue("$baseArgsJson", JsonSerializer.Serialize(template.BaseArgs, JsonOptions));
        command.Parameters.AddWithValue("$defaultCwd", template.DefaultCwd);
        command.Parameters.AddWithValue("$defaultEnvJson", JsonSerializer.Serialize(template.DefaultEnv, JsonOptions));
        command.Parameters.AddWithValue("$description", template.Description);
        command.Parameters.AddWithValue("$icon", template.Icon);
        command.Parameters.AddWithValue("$color", template.Color);
    }

    private CliTemplateRecord NormalizeCreate(CreateCliTemplateRequest request)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return new CliTemplateRecord
        {
            TemplateId = string.IsNullOrWhiteSpace(request.TemplateId) ? Guid.NewGuid().ToString("N") : request.TemplateId.Trim(),
            Name = (request.Name ?? string.Empty).Trim(),
            CliType = NormalizeCliType(request.CliType),
            Executable = (request.Executable ?? string.Empty).Trim(),
            BaseArgs = NormalizeStrings(request.BaseArgs ?? []),
            DefaultCwd = string.IsNullOrWhiteSpace(request.DefaultCwd) ? _defaultCwd : request.DefaultCwd.Trim(),
            DefaultEnv = NormalizeEnv(request.DefaultEnv ?? []),
            Description = (request.Description ?? string.Empty).Trim(),
            Icon = (request.Icon ?? string.Empty).Trim(),
            Color = (request.Color ?? string.Empty).Trim(),
            IsBuiltin = false,
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
    }

    private static string NormalizeCliType(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "custom" : value.Trim().ToLowerInvariant();
        return AllowedCliTypes.Contains(normalized) ? normalized : "custom";
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
                CliType = "bash",
                Executable = "bash",
                BaseArgs = [],
                DefaultCwd = defaultCwd,
                Description = "Run a local bash command or shell entrypoint.",
                Icon = "terminal",
                Color = "#1ea7a4",
                IsBuiltin = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new CliTemplateRecord
            {
                TemplateId = "builtin-codex",
                Name = "Codex CLI",
                CliType = "codex",
                Executable = "codex",
                BaseArgs = [],
                DefaultCwd = defaultCwd,
                Description = "Launch the local Codex CLI with extra args.",
                Icon = "bot",
                Color = "#3a90e5",
                IsBuiltin = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new CliTemplateRecord
            {
                TemplateId = "builtin-bash-echo",
                Name = "Bash Echo Example",
                CliType = "bash",
                Executable = "bash",
                BaseArgs = ["-lc", "echo hello from cli template"],
                DefaultCwd = defaultCwd,
                Description = "Example template for validating the CLI process pipeline.",
                Icon = "flask",
                Color = "#ff9f1a",
                IsBuiltin = true,
                CreatedAt = now,
                UpdatedAt = now
            }
        ];
    }

    private static CliTemplateRecord Clone(CliTemplateRecord template)
    {
        return new CliTemplateRecord
        {
            TemplateId = template.TemplateId,
            Name = template.Name,
            CliType = template.CliType,
            Executable = template.Executable,
            BaseArgs = [.. template.BaseArgs],
            DefaultCwd = template.DefaultCwd,
            DefaultEnv = new Dictionary<string, string>(template.DefaultEnv, StringComparer.Ordinal),
            Description = template.Description,
            Icon = template.Icon,
            Color = template.Color,
            IsBuiltin = template.IsBuiltin,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }
}
