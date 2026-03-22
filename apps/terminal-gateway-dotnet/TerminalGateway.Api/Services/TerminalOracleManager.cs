using System.Collections.Concurrent;
using XTerm;
using XTerm.Options;

namespace TerminalGateway.Api.Services;

public sealed record OracleScreenFrame(int Cols, int Rows, int CursorX, int CursorY, IReadOnlyList<string> VisibleLines, bool AlternateScreen);

public sealed class TerminalOracleManager : IDisposable
{
    private readonly ConcurrentDictionary<string, OracleSession> _sessions = new(StringComparer.Ordinal);
    private readonly InstanceManager _manager;

    public TerminalOracleManager(InstanceManager manager)
    {
        _manager = manager;
        _manager.Created += HandleCreated;
        _manager.Raw += HandleRaw;
        _manager.Resized += HandleResized;
        _manager.Exited += HandleExited;
    }

    public object? BuildSnapshot(string instanceId)
    {
        var state = _manager.Get(instanceId);
        if (state is null)
        {
            return null;
        }

        var session = EnsureSession(state);
        string ownerConnectionId;
        long instanceEpoch;
        long renderEpoch;
        int seq;
        string ansi;
        bool ansiTruncated;
        int cols;
        int rows;
        int historyAvailable;
        string newestHistoryCursor;
        lock (state.Sync)
        {
            ownerConnectionId = state.DisplayOwnerConnectionId;
            instanceEpoch = state.InstanceEpoch;
            renderEpoch = state.RenderEpoch;
            seq = state.Seq;
            cols = state.Cols;
            rows = state.Rows;
            ansi = string.Concat(state.RawChunks.Select(x => x.Data));
            ansiTruncated = state.RawChunks.Count > 0 && state.RawChunks[0].Seq > 1;
            historyAvailable = state.History.Count;
            newestHistoryCursor = state.History.NewestCursor();
        }

        OracleScreenFrame frame;
        lock (session.Sync)
        {
            EnsureSize(session, cols, rows);
            frame = ExportUnsafe(session);
        }

        return new
        {
            v = 1,
            type = "term.snapshot",
            instance_id = state.Id,
            node_id = _manager.NodeId,
            node_name = _manager.NodeName,
            instance_epoch = instanceEpoch,
            render_epoch = renderEpoch,
            owner_connection_id = ownerConnectionId,
            seq,
            base_seq = seq,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            size = new { cols = frame.Cols, rows = frame.Rows },
            cursor = new { x = frame.CursorX, y = frame.CursorY, visible = true },
            alternate_screen = frame.AlternateScreen,
            ansi,
            ansi_truncated = ansiTruncated,
            styles = new Dictionary<string, object>
            {
                ["0"] = new { fg = (int?)null, bg = (int?)null, bold = false, italic = false, underline = false, inverse = false }
            },
            rows = frame.VisibleLines.Select((line, index) => new { y = index, segs = new object[] { new object[] { line, 0 } } }).ToList(),
            history = new { available = historyAvailable, newest_cursor = newestHistoryCursor }
        };
    }

    public void Dispose()
    {
        _manager.Created -= HandleCreated;
        _manager.Raw -= HandleRaw;
        _manager.Resized -= HandleResized;
        _manager.Exited -= HandleExited;
        foreach (var session in _sessions.Values)
        {
            lock (session.Sync)
            {
                session.Terminal.Dispose();
            }
        }
        _sessions.Clear();
    }

    private void HandleCreated(string instanceId)
    {
        var state = _manager.Get(instanceId);
        if (state is null)
        {
            return;
        }

        EnsureSession(state);
    }

    private void HandleRaw(string instanceId, object payload)
    {
        var state = _manager.Get(instanceId);
        if (state is null)
        {
            return;
        }

        var session = EnsureSession(state);
        var element = System.Text.Json.JsonSerializer.SerializeToElement(payload);
        if (!element.TryGetProperty("data", out var dataValue) || dataValue.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            return;
        }

        var chunk = dataValue.GetString() ?? string.Empty;
        if (chunk.Length == 0)
        {
            return;
        }

        lock (session.Sync)
        {
            EnsureSize(session, state.Cols, state.Rows);
            session.Terminal.Write(chunk);
        }
    }

    private void HandleResized(string instanceId, int cols, int rows)
    {
        if (!_sessions.TryGetValue(instanceId, out var session))
        {
            return;
        }

        lock (session.Sync)
        {
            EnsureSize(session, cols, rows);
        }
    }

    private void HandleExited(string instanceId, object payload)
    {
        if (_sessions.TryRemove(instanceId, out var session))
        {
            lock (session.Sync)
            {
                session.Terminal.Dispose();
            }
        }
    }

    private OracleSession EnsureSession(InstanceManager.InstanceState state)
    {
        return _sessions.GetOrAdd(state.Id, _ =>
        {
            var created = new OracleSession(state.Cols, state.Rows);
            var replay = _manager.RawReplay(state.Id);
            if (!string.IsNullOrEmpty(replay))
            {
                lock (created.Sync)
                {
                    created.Terminal.Write(replay);
                }
            }
            return created;
        });
    }

    private static void EnsureSize(OracleSession session, int cols, int rows)
    {
        var safeCols = Math.Max(1, cols);
        var safeRows = Math.Max(1, rows);
        if (session.Terminal.Cols == safeCols && session.Terminal.Rows == safeRows)
        {
            return;
        }

        session.Terminal.Resize(safeCols, safeRows);
    }

    private static OracleScreenFrame ExportUnsafe(OracleSession session)
    {
        var lines = session.Terminal.GetVisibleLines() ?? [];
        return new OracleScreenFrame(
            session.Terminal.Cols,
            session.Terminal.Rows,
            Math.Max(0, session.Terminal.Buffer.X),
            Math.Max(0, session.Terminal.Buffer.Y),
            lines.ToList(),
            false);
    }

    private sealed class OracleSession
    {
        public OracleSession(int cols, int rows)
        {
            Terminal = new Terminal(new TerminalOptions
            {
                Cols = Math.Max(1, cols),
                Rows = Math.Max(1, rows),
                ConvertEol = true,
                Scrollback = 5000
            });
        }

        public object Sync { get; } = new();
        public Terminal Terminal { get; }
    }
}
