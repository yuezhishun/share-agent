using Microsoft.Data.Sqlite;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class TerminalShortcutService
{
    private readonly string _dbPath;
    private readonly Lock _sync = new();

    public TerminalShortcutService(string dbPath)
    {
        _dbPath = string.IsNullOrWhiteSpace(dbPath) ? "/tmp/pty-agent-cli-templates.db" : dbPath.Trim();
        EnsureDatabase();
    }

    public IReadOnlyList<TerminalShortcutRecord> List()
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT shortcut_id, label, command, group_name, press_enter, enabled, sort_order, created_at, updated_at
                FROM terminal_shortcuts
                ORDER BY sort_order ASC, created_at ASC, shortcut_id ASC;
                """;
            using var reader = command.ExecuteReader();
            var items = new List<TerminalShortcutRecord>();
            while (reader.Read())
            {
                items.Add(ReadRecord(reader));
            }

            return items;
        }
    }

    public TerminalShortcutRecord Create(CreateTerminalShortcutRequest request)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            var nextSortOrder = request.SortOrder ?? GetNextSortOrder(connection);
            var item = NormalizeCreate(request, nextSortOrder);
            Validate(item);

            using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(1) FROM terminal_shortcuts WHERE shortcut_id = $shortcutId;";
            exists.Parameters.AddWithValue("$shortcutId", item.ShortcutId);
            if (Convert.ToInt32(exists.ExecuteScalar() ?? 0) > 0)
            {
                throw new InvalidOperationException($"shortcut already exists: {item.ShortcutId}");
            }

            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO terminal_shortcuts (
                    shortcut_id, label, command, group_name, press_enter, enabled, sort_order, created_at, updated_at
                ) VALUES (
                    $shortcutId, $label, $command, $groupName, $pressEnter, $enabled, $sortOrder, $createdAt, $updatedAt
                );
                """;
            Bind(insert, item);
            insert.ExecuteNonQuery();
            NormalizeSortOrder(connection);
            return GetRequired(item.ShortcutId);
        }
    }

    public TerminalShortcutRecord Update(string shortcutId, UpdateTerminalShortcutRequest request)
    {
        if (string.IsNullOrWhiteSpace(shortcutId))
        {
            throw new InvalidOperationException("shortcut_id is required");
        }

        lock (_sync)
        {
            using var connection = OpenConnection();
            var current = ReadShortcut(connection, shortcutId.Trim());
            if (current is null)
            {
                throw new InvalidOperationException($"shortcut not found: {shortcutId}");
            }

            var merged = new TerminalShortcutRecord
            {
                ShortcutId = current.ShortcutId,
                Label = (request.Label ?? current.Label).Trim(),
                Command = (request.Command ?? current.Command).Trim(),
                GroupName = NormalizeGroupName(request.GroupName ?? current.GroupName),
                PressEnter = request.PressEnter ?? current.PressEnter,
                Enabled = request.Enabled ?? current.Enabled,
                SortOrder = request.SortOrder ?? current.SortOrder,
                CreatedAt = current.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
            };
            Validate(merged);

            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE terminal_shortcuts
                SET label = $label,
                    command = $command,
                    group_name = $groupName,
                    press_enter = $pressEnter,
                    enabled = $enabled,
                    sort_order = $sortOrder,
                    updated_at = $updatedAt
                WHERE shortcut_id = $shortcutId;
                """;
            Bind(update, merged);
            update.ExecuteNonQuery();
            NormalizeSortOrder(connection);
            return GetRequired(merged.ShortcutId);
        }
    }

    public object Delete(string shortcutId)
    {
        if (string.IsNullOrWhiteSpace(shortcutId))
        {
            throw new InvalidOperationException("shortcut_id is required");
        }

        lock (_sync)
        {
            using var connection = OpenConnection();
            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM terminal_shortcuts WHERE shortcut_id = $shortcutId;";
            delete.Parameters.AddWithValue("$shortcutId", shortcutId.Trim());
            var rows = delete.ExecuteNonQuery();
            if (rows == 0)
            {
                throw new InvalidOperationException($"shortcut not found: {shortcutId}");
            }

            NormalizeSortOrder(connection);
            return new { ok = true, shortcut_id = shortcutId.Trim() };
        }
    }

    public TerminalShortcutRecord GetRequired(string shortcutId)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            var record = ReadShortcut(connection, shortcutId);
            if (record is null)
            {
                throw new InvalidOperationException($"shortcut not found: {shortcutId}");
            }

            return record;
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
            CREATE TABLE IF NOT EXISTS terminal_shortcuts (
              shortcut_id TEXT PRIMARY KEY,
              label TEXT NOT NULL,
              command TEXT NOT NULL,
              group_name TEXT NOT NULL,
              press_enter INTEGER NOT NULL DEFAULT 1,
              enabled INTEGER NOT NULL DEFAULT 1,
              sort_order INTEGER NOT NULL DEFAULT 0,
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

    private static TerminalShortcutRecord? ReadShortcut(SqliteConnection connection, string shortcutId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT shortcut_id, label, command, group_name, press_enter, enabled, sort_order, created_at, updated_at
            FROM terminal_shortcuts
            WHERE shortcut_id = $shortcutId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$shortcutId", shortcutId.Trim());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    private static TerminalShortcutRecord ReadRecord(SqliteDataReader reader)
    {
        return new TerminalShortcutRecord
        {
            ShortcutId = reader.GetString(0),
            Label = reader.GetString(1),
            Command = reader.GetString(2),
            GroupName = reader.GetString(3),
            PressEnter = reader.GetInt64(4) != 0,
            Enabled = reader.GetInt64(5) != 0,
            SortOrder = reader.GetInt32(6),
            CreatedAt = reader.GetString(7),
            UpdatedAt = reader.GetString(8)
        };
    }

    private static TerminalShortcutRecord NormalizeCreate(CreateTerminalShortcutRequest request, int sortOrder)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return new TerminalShortcutRecord
        {
            ShortcutId = string.IsNullOrWhiteSpace(request.ShortcutId) ? $"shortcut-{Guid.NewGuid():N}" : request.ShortcutId.Trim(),
            Label = (request.Label ?? string.Empty).Trim(),
            Command = (request.Command ?? string.Empty).Trim(),
            GroupName = NormalizeGroupName(request.GroupName),
            PressEnter = request.PressEnter ?? true,
            Enabled = request.Enabled ?? true,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static void Validate(TerminalShortcutRecord item)
    {
        if (string.IsNullOrWhiteSpace(item.ShortcutId))
        {
            throw new InvalidOperationException("shortcut_id is required");
        }
        if (string.IsNullOrWhiteSpace(item.Label))
        {
            throw new InvalidOperationException("label is required");
        }
        if (string.IsNullOrWhiteSpace(item.Command))
        {
            throw new InvalidOperationException("command is required");
        }

        item.GroupName = NormalizeGroupName(item.GroupName);
        item.SortOrder = Math.Max(0, item.SortOrder);
    }

    private static string NormalizeGroupName(string? groupName)
    {
        var normalized = (groupName ?? string.Empty).Trim();
        return normalized.Length == 0 ? "custom" : normalized;
    }

    private static void Bind(SqliteCommand command, TerminalShortcutRecord item)
    {
        command.Parameters.AddWithValue("$shortcutId", item.ShortcutId);
        command.Parameters.AddWithValue("$label", item.Label);
        command.Parameters.AddWithValue("$command", item.Command);
        command.Parameters.AddWithValue("$groupName", item.GroupName);
        command.Parameters.AddWithValue("$pressEnter", item.PressEnter ? 1 : 0);
        command.Parameters.AddWithValue("$enabled", item.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", item.SortOrder);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt);
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt);
    }

    private static int GetNextSortOrder(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM terminal_shortcuts;";
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static void NormalizeSortOrder(SqliteConnection connection)
    {
        using var read = connection.CreateCommand();
        read.CommandText = """
            SELECT shortcut_id
            FROM terminal_shortcuts
            ORDER BY sort_order ASC, created_at ASC, shortcut_id ASC;
            """;
        using var reader = read.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }
        reader.Close();

        for (var index = 0; index < ids.Count; index += 1)
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE terminal_shortcuts SET sort_order = $sortOrder WHERE shortcut_id = $shortcutId;";
            update.Parameters.AddWithValue("$sortOrder", index);
            update.Parameters.AddWithValue("$shortcutId", ids[index]);
            update.ExecuteNonQuery();
        }
    }
}
