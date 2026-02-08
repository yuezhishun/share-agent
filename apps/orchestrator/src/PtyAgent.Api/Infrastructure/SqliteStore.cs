using Microsoft.Data.Sqlite;
using PtyAgent.Api.Domain;

namespace PtyAgent.Api.Infrastructure;

public sealed class SqliteStore
{
    private readonly SqliteOptions _options;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public SqliteStore(SqliteOptions options)
    {
        _options = options;
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DbPath) ?? ".");
        Directory.CreateDirectory(_options.LogsPath);
        Directory.CreateDirectory(_options.WorkdirsPath);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_options.DbPath}");
        connection.Open();
        return connection;
    }

    public async Task InitializeAsync()
    {
        const string sql = """
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS tasks (
          task_id TEXT PRIMARY KEY,
          title TEXT NOT NULL,
          intent TEXT NOT NULL,
          constraints TEXT NULL,
          priority INTEGER NOT NULL,
          status TEXT NOT NULL,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL,
          is_complex INTEGER NOT NULL,
          cli_type TEXT NULL,
          command TEXT NULL,
          planner_session_id TEXT NULL,
          executor_session_id TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS task_links (
          source_input_id TEXT PRIMARY KEY,
          task_id TEXT NOT NULL,
          link_type TEXT NOT NULL,
          confidence REAL NOT NULL,
          reason TEXT NOT NULL,
          created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS plans (
          plan_id TEXT PRIMARY KEY,
          task_id TEXT NOT NULL,
          planner_session_id TEXT NOT NULL,
          milestones_json TEXT NOT NULL,
          io_contracts_json TEXT NOT NULL,
          acceptance_criteria TEXT NOT NULL,
          risks TEXT NOT NULL,
          created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS handoffs (
          handoff_id TEXT PRIMARY KEY,
          task_id TEXT NOT NULL,
          from_plan_id TEXT NOT NULL,
          executor_session_id TEXT NOT NULL,
          handoff_checklist TEXT NOT NULL,
          context_bundle_ref TEXT NOT NULL,
          created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS sessions (
          session_id TEXT PRIMARY KEY,
          task_id TEXT NOT NULL,
          cli_type TEXT NOT NULL,
          workdir TEXT NOT NULL,
          env_profile TEXT NULL,
          status TEXT NOT NULL,
          pid INTEGER NULL,
          started_at TEXT NOT NULL,
          ended_at TEXT NULL,
          mode TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS events (
          event_id TEXT PRIMARY KEY,
          task_id TEXT NOT NULL,
          session_id TEXT NULL,
          event_type TEXT NOT NULL,
          severity TEXT NOT NULL,
          payload TEXT NOT NULL,
          timestamp TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS evaluations (
          record_id TEXT PRIMARY KEY,
          task_id TEXT NOT NULL,
          rule_id TEXT NOT NULL,
          drift_score REAL NOT NULL,
          action_taken TEXT NOT NULL,
          created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS knowledge (
          item_id TEXT PRIMARY KEY,
          task_id TEXT NOT NULL,
          kind TEXT NOT NULL,
          content TEXT NOT NULL,
          embedding_ref TEXT NULL,
          tags TEXT NOT NULL,
          created_at TEXT NOT NULL
        );
        """;

        await _mutex.WaitAsync();
        try
        {
            await using var connection = Open();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task InsertTaskAsync(TaskItem task)
    {
        await ExecuteAsync(
            "INSERT INTO tasks(task_id,title,intent,constraints,priority,status,created_at,updated_at,is_complex,cli_type,command,planner_session_id,executor_session_id) VALUES($task_id,$title,$intent,$constraints,$priority,$status,$created_at,$updated_at,$is_complex,$cli_type,$command,$planner_session_id,$executor_session_id)",
            [
                ("$task_id", task.TaskId.ToString()),
                ("$title", task.Title),
                ("$intent", task.Intent),
                ("$constraints", task.Constraints),
                ("$priority", task.Priority),
                ("$status", task.Status.ToString()),
                ("$created_at", task.CreatedAt.ToString("O")),
                ("$updated_at", task.UpdatedAt.ToString("O")),
                ("$is_complex", task.IsComplex ? 1 : 0),
                ("$cli_type", task.CliType),
                ("$command", task.Command),
                ("$planner_session_id", task.PlannerSessionId?.ToString()),
                ("$executor_session_id", task.ExecutorSessionId?.ToString())
            ]);
    }

    public async Task InsertTaskLinkAsync(TaskLink link)
    {
        await ExecuteAsync(
            "INSERT INTO task_links(source_input_id,task_id,link_type,confidence,reason,created_at) VALUES($source_input_id,$task_id,$link_type,$confidence,$reason,$created_at)",
            [
                ("$source_input_id", link.SourceInputId.ToString()),
                ("$task_id", link.TaskId.ToString()),
                ("$link_type", link.LinkType.ToString()),
                ("$confidence", link.Confidence),
                ("$reason", link.Reason),
                ("$created_at", link.CreatedAt.ToString("O"))
            ]);
    }

    public async Task UpdateTaskStatusAsync(Guid taskId, PtyAgent.Api.Domain.TaskStatus status, Guid? plannerSessionId = null, Guid? executorSessionId = null)
    {
        await ExecuteAsync(
            "UPDATE tasks SET status=$status, updated_at=$updated_at, planner_session_id=COALESCE($planner_session_id, planner_session_id), executor_session_id=COALESCE($executor_session_id, executor_session_id) WHERE task_id=$task_id",
            [
                ("$task_id", taskId.ToString()),
                ("$status", status.ToString()),
                ("$updated_at", DateTimeOffset.UtcNow.ToString("O")),
                ("$planner_session_id", plannerSessionId?.ToString()),
                ("$executor_session_id", executorSessionId?.ToString())
            ]);
    }

    public async Task<TaskItem?> GetTaskAsync(Guid taskId)
    {
        await _mutex.WaitAsync();
        try
        {
            await using var connection = Open();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT task_id,title,intent,constraints,priority,status,created_at,updated_at,is_complex,cli_type,command,planner_session_id,executor_session_id FROM tasks WHERE task_id=$task_id";
            cmd.Parameters.AddWithValue("$task_id", taskId.ToString());
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new TaskItem(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4),
                Enum.Parse<PtyAgent.Api.Domain.TaskStatus>(reader.GetString(5)),
                DateTimeOffset.Parse(reader.GetString(6)),
                DateTimeOffset.Parse(reader.GetString(7)),
                reader.GetInt32(8) == 1,
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
                reader.IsDBNull(12) ? null : Guid.Parse(reader.GetString(12))
            );
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<TaskItem>> ListRecentTasksAsync(int limit = 50)
    {
        await _mutex.WaitAsync();
        try
        {
            var result = new List<TaskItem>();
            await using var connection = Open();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT task_id,title,intent,constraints,priority,status,created_at,updated_at,is_complex,cli_type,command,planner_session_id,executor_session_id FROM tasks ORDER BY created_at DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new TaskItem(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt32(4),
                    Enum.Parse<PtyAgent.Api.Domain.TaskStatus>(reader.GetString(5)),
                    DateTimeOffset.Parse(reader.GetString(6)),
                    DateTimeOffset.Parse(reader.GetString(7)),
                    reader.GetInt32(8) == 1,
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
                    reader.IsDBNull(12) ? null : Guid.Parse(reader.GetString(12))
                ));
            }

            return result;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task InsertEventAsync(ProgressEvent evt)
    {
        await ExecuteAsync(
            "INSERT INTO events(event_id,task_id,session_id,event_type,severity,payload,timestamp) VALUES($event_id,$task_id,$session_id,$event_type,$severity,$payload,$timestamp)",
            [
                ("$event_id", evt.EventId.ToString()),
                ("$task_id", evt.TaskId.ToString()),
                ("$session_id", evt.SessionId?.ToString()),
                ("$event_type", evt.EventType),
                ("$severity", evt.Severity),
                ("$payload", evt.Payload),
                ("$timestamp", evt.Timestamp.ToString("O"))
            ]);
    }

    public async Task<IReadOnlyList<ProgressEvent>> ListEventsByTaskAsync(Guid taskId)
    {
        await _mutex.WaitAsync();
        try
        {
            var results = new List<ProgressEvent>();
            await using var connection = Open();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT event_id,task_id,session_id,event_type,severity,payload,timestamp FROM events WHERE task_id=$task_id ORDER BY timestamp";
            cmd.Parameters.AddWithValue("$task_id", taskId.ToString());
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new ProgressEvent(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    DateTimeOffset.Parse(reader.GetString(6))
                ));
            }

            return results;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task InsertSessionAsync(ExecutionSession session)
    {
        await ExecuteAsync(
            "INSERT INTO sessions(session_id,task_id,cli_type,workdir,env_profile,status,pid,started_at,ended_at,mode) VALUES($session_id,$task_id,$cli_type,$workdir,$env_profile,$status,$pid,$started_at,$ended_at,$mode)",
            [
                ("$session_id", session.SessionId.ToString()),
                ("$task_id", session.TaskId.ToString()),
                ("$cli_type", session.CliType),
                ("$workdir", session.Workdir),
                ("$env_profile", session.EnvProfile),
                ("$status", session.Status.ToString()),
                ("$pid", session.Pid),
                ("$started_at", session.StartedAt.ToString("O")),
                ("$ended_at", session.EndedAt?.ToString("O")),
                ("$mode", session.Mode)
            ]);
    }

    public async Task UpdateSessionStatusAsync(Guid sessionId, SessionStatus status, int? pid = null, DateTimeOffset? endedAt = null)
    {
        await ExecuteAsync(
            "UPDATE sessions SET status=$status, pid=COALESCE($pid,pid), ended_at=COALESCE($ended_at,ended_at) WHERE session_id=$session_id",
            [
                ("$session_id", sessionId.ToString()),
                ("$status", status.ToString()),
                ("$pid", pid),
                ("$ended_at", endedAt?.ToString("O"))
            ]);
    }

    public async Task<ExecutionSession?> GetSessionAsync(Guid sessionId)
    {
        await _mutex.WaitAsync();
        try
        {
            await using var connection = Open();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT session_id,task_id,cli_type,workdir,env_profile,status,pid,started_at,ended_at,mode FROM sessions WHERE session_id=$session_id";
            cmd.Parameters.AddWithValue("$session_id", sessionId.ToString());
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new ExecutionSession(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                Enum.Parse<SessionStatus>(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                DateTimeOffset.Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
                reader.GetString(9)
            );
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<ExecutionSession>> ListSessionsByTaskAsync(Guid taskId)
    {
        await _mutex.WaitAsync();
        try
        {
            var sessions = new List<ExecutionSession>();
            await using var connection = Open();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT session_id,task_id,cli_type,workdir,env_profile,status,pid,started_at,ended_at,mode FROM sessions WHERE task_id=$task_id ORDER BY started_at DESC";
            cmd.Parameters.AddWithValue("$task_id", taskId.ToString());
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                sessions.Add(new ExecutionSession(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    Enum.Parse<SessionStatus>(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    DateTimeOffset.Parse(reader.GetString(7)),
                    reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
                    reader.GetString(9)
                ));
            }

            return sessions;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task InsertPlanAsync(PlanArtifact plan)
    {
        await ExecuteAsync(
            "INSERT INTO plans(plan_id,task_id,planner_session_id,milestones_json,io_contracts_json,acceptance_criteria,risks,created_at) VALUES($plan_id,$task_id,$planner_session_id,$milestones_json,$io_contracts_json,$acceptance_criteria,$risks,$created_at)",
            [
                ("$plan_id", plan.PlanId.ToString()),
                ("$task_id", plan.TaskId.ToString()),
                ("$planner_session_id", plan.PlannerSessionId.ToString()),
                ("$milestones_json", plan.MilestonesJson),
                ("$io_contracts_json", plan.IoContractsJson),
                ("$acceptance_criteria", plan.AcceptanceCriteria),
                ("$risks", plan.Risks),
                ("$created_at", plan.CreatedAt.ToString("O"))
            ]);
    }

    public async Task<PlanArtifact?> GetLatestPlanByTaskAsync(Guid taskId)
    {
        await _mutex.WaitAsync();
        try
        {
            await using var connection = Open();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT plan_id,task_id,planner_session_id,milestones_json,io_contracts_json,acceptance_criteria,risks,created_at FROM plans WHERE task_id=$task_id ORDER BY created_at DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$task_id", taskId.ToString());
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new PlanArtifact(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7))
            );
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task InsertHandoffAsync(ExecutionHandoff handoff)
    {
        await ExecuteAsync(
            "INSERT INTO handoffs(handoff_id,task_id,from_plan_id,executor_session_id,handoff_checklist,context_bundle_ref,created_at) VALUES($handoff_id,$task_id,$from_plan_id,$executor_session_id,$handoff_checklist,$context_bundle_ref,$created_at)",
            [
                ("$handoff_id", handoff.HandoffId.ToString()),
                ("$task_id", handoff.TaskId.ToString()),
                ("$from_plan_id", handoff.FromPlanId.ToString()),
                ("$executor_session_id", handoff.ExecutorSessionId.ToString()),
                ("$handoff_checklist", handoff.HandoffChecklist),
                ("$context_bundle_ref", handoff.ContextBundleRef),
                ("$created_at", handoff.CreatedAt.ToString("O"))
            ]);
    }

    public async Task InsertEvaluationAsync(EvaluationRecord evaluation)
    {
        await ExecuteAsync(
            "INSERT INTO evaluations(record_id,task_id,rule_id,drift_score,action_taken,created_at) VALUES($record_id,$task_id,$rule_id,$drift_score,$action_taken,$created_at)",
            [
                ("$record_id", evaluation.RecordId.ToString()),
                ("$task_id", evaluation.TaskId.ToString()),
                ("$rule_id", evaluation.RuleId),
                ("$drift_score", evaluation.DriftScore),
                ("$action_taken", evaluation.ActionTaken),
                ("$created_at", evaluation.CreatedAt.ToString("O"))
            ]);
    }

    public async Task InsertKnowledgeItemAsync(KnowledgeItem item)
    {
        await ExecuteAsync(
            "INSERT INTO knowledge(item_id,task_id,kind,content,embedding_ref,tags,created_at) VALUES($item_id,$task_id,$kind,$content,$embedding_ref,$tags,$created_at)",
            [
                ("$item_id", item.ItemId.ToString()),
                ("$task_id", item.TaskId.ToString()),
                ("$kind", item.Kind),
                ("$content", item.Content),
                ("$embedding_ref", item.EmbeddingRef),
                ("$tags", item.Tags),
                ("$created_at", item.CreatedAt.ToString("O"))
            ]);
    }

    public async Task<IReadOnlyList<KnowledgeItem>> SearchKnowledgeAsync(string query)
    {
        await _mutex.WaitAsync();
        try
        {
            var result = new List<KnowledgeItem>();
            await using var connection = Open();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT item_id,task_id,kind,content,embedding_ref,tags,created_at FROM knowledge WHERE content LIKE $query OR tags LIKE $query ORDER BY created_at DESC LIMIT 20";
            cmd.Parameters.AddWithValue("$query", $"%{query}%");
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new KnowledgeItem(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    DateTimeOffset.Parse(reader.GetString(6))
                ));
            }

            return result;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<(int total, int running, int done, int failed, IReadOnlyList<ProgressEvent> events)> GetProgressSummaryAsync(TimeSpan window)
    {
        await _mutex.WaitAsync();
        try
        {
            await using var connection = Open();
            int total = await CountAsync(connection, "SELECT COUNT(*) FROM tasks");
            int running = await CountAsync(connection, "SELECT COUNT(*) FROM tasks WHERE status IN ('Planning','Executing','Replanning','BlockedForDecision')");
            int done = await CountAsync(connection, "SELECT COUNT(*) FROM tasks WHERE status='Done'");
            int failed = await CountAsync(connection, "SELECT COUNT(*) FROM tasks WHERE status='Failed'");

            var recent = new List<ProgressEvent>();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT event_id,task_id,session_id,event_type,severity,payload,timestamp FROM events WHERE timestamp >= $since ORDER BY timestamp DESC LIMIT 50";
            cmd.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.Subtract(window).ToString("O"));
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                recent.Add(new ProgressEvent(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    DateTimeOffset.Parse(reader.GetString(6))
                ));
            }

            return (total, running, done, failed, recent);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private async Task ExecuteAsync(string sql, IEnumerable<(string name, object? value)> parameters)
    {
        await _mutex.WaitAsync();
        try
        {
            await using var connection = Open();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _mutex.Release();
        }
    }
}
