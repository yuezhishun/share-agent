using System.Collections.Concurrent;
using System.Collections;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Pty;

namespace TerminalGateway.Api.Services;

public sealed class InstanceManager
{
    private readonly ConcurrentDictionary<string, InstanceState> _instances = new(StringComparer.Ordinal);
    private readonly IPtyEngine _ptyEngine;
    private readonly int _historyLimit;
    private readonly int _rawReplayMaxBytes;
    private readonly int _defaultCols;
    private readonly int _defaultRows;
    private readonly string _nodeId;
    private readonly string _nodeName;
    private readonly string _nodeRole;
    private readonly IReadOnlyList<string> _pathPrefixes;

    public event Action<string, object>? Patch;
    public event Action<string, object>? Raw;
    public event Action<string, object>? Exited;
    public event Action<string, object>? StateChanged;
    public event Action<string>? Created;
    public event Action<string, int, int>? Resized;
    public string NodeId => _nodeId;
    public string NodeName => _nodeName;
    public string NodeRole => _nodeRole;
    private readonly ConcurrentDictionary<string, long> _metrics = new(StringComparer.Ordinal);

    public InstanceManager(
        IPtyEngine ptyEngine,
        int historyLimit,
        int rawReplayMaxBytes,
        int defaultCols,
        int defaultRows,
        string nodeId,
        string nodeName,
        string nodeRole,
        IReadOnlyList<string>? pathPrefixes = null)
    {
        _ptyEngine = ptyEngine;
        _historyLimit = Math.Max(1, historyLimit);
        _rawReplayMaxBytes = Math.Max(1024, rawReplayMaxBytes);
        _defaultCols = Math.Clamp(defaultCols, 1, 500);
        _defaultRows = Math.Clamp(defaultRows, 1, 300);
        _nodeId = nodeId;
        _nodeName = nodeName;
        _nodeRole = nodeRole;
        _pathPrefixes = NormalizePathPrefixes(pathPrefixes);
    }

    public async Task<InstanceSummary> CreateAsync(CreateInstanceRequest input, string defaultBasePath, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();
        var command = (input.Command ?? string.Empty).Trim();
        if (command.Length == 0)
        {
            throw new InvalidOperationException("command is required");
        }

        var cols = SanitizeDimension(input.Cols ?? _defaultCols, _defaultCols, 1, 500);
        var rows = SanitizeDimension(input.Rows ?? _defaultRows, _defaultRows, 1, 300);
        var cwd = ResolveWithinBase(defaultBasePath, input.Cwd);
        if (cwd is null)
        {
            throw new UnauthorizedAccessException("cwd is outside allowed base");
        }

        var parsed = CommandParser.Parse(command);
        var args = (input.Args is { Count: > 0 } ? input.Args : parsed.Args).ToList();
        args = EnsureInteractiveShellArgs(parsed.File, args);
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            env[(string)entry.Key] = Convert.ToString(entry.Value) ?? string.Empty;
        }

        foreach (var kv in input.Env ?? [])
        {
            env[kv.Key] = kv.Value;
        }
        env["PATH"] = BuildPathWithPrefixes(env.TryGetValue("PATH", out var currentPath) ? currentPath : string.Empty, _pathPrefixes);

        var runtime = await _ptyEngine.CreateAsync(new PtyLaunchOptions
        {
            Executable = parsed.File,
            Args = args,
            Cwd = cwd,
            Env = env,
            Cols = cols,
            Rows = rows
        }, cancellationToken);

        var state = new InstanceState
        {
            Id = id,
            Command = command,
            Cwd = cwd,
            Cols = cols,
            Rows = rows,
            CreatedAt = DateTimeOffset.UtcNow,
            Pty = runtime,
            History = new HistoryRing(_historyLimit),
            Buffer = new TerminalStateBuffer(_historyLimit * 2),
            InstanceEpoch = 1,
            RenderEpoch = 1
        };

        runtime.OutputReceived += data => OnOutput(state, data);
        runtime.Exited += code => _ = OnExitedAsync(state, code);

        if (!_instances.TryAdd(id, state))
        {
            await runtime.DisposeAsync();
            throw new InvalidOperationException("instance id collision");
        }

        Created?.Invoke(id);

        return ToSummary(state);
    }

    public IReadOnlyList<InstanceSummary> List()
    {
        return _instances.Values
            .Select(ToSummary)
            .OrderByDescending(x => x.CreatedAt, StringComparer.Ordinal)
            .ToList();
    }

    public InstanceState? Get(string instanceId)
    {
        return _instances.TryGetValue(instanceId, out var state) ? state : null;
    }

    public bool AttachClient(string instanceId, WebSocket socket)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return false;
        }

        lock (state.Sync)
        {
            state.Clients.Add(socket);
        }

        return true;
    }

    public void DetachClient(string instanceId, WebSocket socket)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return;
        }

        lock (state.Sync)
        {
            state.Clients.Remove(socket);
        }
    }

    public bool WriteStdin(string instanceId, string data)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return false;
        }

        _ = state.Pty.WriteAsync(data, CancellationToken.None);
        return true;
    }

    public object? Resize(string instanceId, int cols, int rows)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return null;
        }

        RecordMetric("resize_requests_total");
        int safeCols;
        int safeRows;
        object snapshot;
        lock (state.Sync)
        {
            safeCols = SanitizeDimension(cols, state.Cols, 1, 500);
            safeRows = SanitizeDimension(rows, state.Rows, 1, 300);
            state.Cols = safeCols;
            state.Rows = safeRows;
            state.RenderEpoch++;
            snapshot = BuildSnapshotUnsafe(state);
        }

        _ = state.Pty.ResizeAsync(safeCols, safeRows, CancellationToken.None);
        Resized?.Invoke(instanceId, safeCols, safeRows);
        RecordMetric("resize_applied_total");
        return snapshot;
    }

    public ResizeDecision RequestResize(string instanceId, string connectionId, int cols, int rows)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return ResizeDecision.NotFound();
        }

        RecordMetric("resize_requests_total");
        int safeCols;
        int safeRows;
        object snapshot;
        lock (state.Sync)
        {
            if (!string.Equals(state.DisplayOwnerConnectionId, connectionId, StringComparison.Ordinal))
            {
                RecordMetric("resize_rejected_not_owner_total");
                return ResizeDecision.Rejected(state.Cols, state.Rows, state.RenderEpoch, state.DisplayOwnerConnectionId);
            }

            safeCols = SanitizeDimension(cols, state.Cols, 1, 500);
            safeRows = SanitizeDimension(rows, state.Rows, 1, 300);
            state.Cols = safeCols;
            state.Rows = safeRows;
            state.RenderEpoch++;
            snapshot = BuildSnapshotUnsafe(state);
        }

        _ = state.Pty.ResizeAsync(safeCols, safeRows, CancellationToken.None);
        Resized?.Invoke(instanceId, safeCols, safeRows);
        RecordMetric("resize_applied_total");
        return ResizeDecision.FromAccepted(snapshot, safeCols, safeRows, GetRenderEpoch(snapshot));
    }

    public object? Snapshot(string instanceId, bool advanceSeq)
    {
        var state = Get(instanceId);
        return state is null ? null : Snapshot(state, advanceSeq);
    }

    public object? HistoryChunk(string instanceId, string reqId, string before, int limit)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return null;
        }

        var result = state.History.Fetch(before, limit);
        return new
        {
            v = 1,
            type = "term.history.chunk",
            instance_id = instanceId,
            node_id = _nodeId,
            node_name = _nodeName,
            req_id = reqId,
            lines = result.Lines.Select(x => new { segs = new object[] { new object[] { x.Text, 0 } } }).ToList(),
            next_before = result.NextBefore,
            exhausted = result.Exhausted
        };
    }

    public string? RawReplay(string instanceId)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return null;
        }

        lock (state.Sync)
        {
            return string.Concat(state.RawChunks.Select(x => x.Data));
        }
    }

    public object? RawReplayEvent(string instanceId, int? sinceSeq = null, string? reqId = null)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return null;
        }

        string replay;
        var requestedSince = Math.Max(0, sinceSeq ?? 0);
        var effectiveSince = requestedSince;
        var oldestSeq = 0;
        var latestSeq = 0;
        var fromSeq = 0;
        var truncated = false;
        lock (state.Sync)
        {
            if (state.RawChunks.Count > 0)
            {
                oldestSeq = state.RawChunks[0].Seq;
            }
            latestSeq = Math.Max(0, state.Seq);

            if (requestedSince > 0 && oldestSeq > 0 && requestedSince < oldestSeq - 1)
            {
                truncated = true;
                effectiveSince = oldestSeq - 1;
            }

            var selected = state.RawChunks.Where(x => x.Seq > effectiveSince).ToList();
            replay = string.Concat(selected.Select(x => x.Data));
            fromSeq = selected.Count > 0
                ? selected[0].Seq
                : Math.Max(0, effectiveSince + 1);
        }

        return new
        {
            v = 1,
            type = "term.raw",
            instance_id = state.Id,
            node_id = _nodeId,
            node_name = _nodeName,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            replay = true,
            req_id = reqId,
            since_seq = requestedSince,
            from_seq = fromSeq,
            to_seq = latestSeq,
            seq = latestSeq,
            reset = requestedSince <= 0 || truncated,
            truncated,
            oldest_seq = oldestSeq,
            data = replay
        };
    }

    public bool Terminate(string instanceId)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return false;
        }

        _ = state.Pty.TerminateAsync("SIGTERM", CancellationToken.None);
        return true;
    }

    private void OnOutput(InstanceState state, string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        List<WebSocket> peers;
        object patch;
        object raw;
        int seq;
        long ts;
        lock (state.Sync)
        {
            state.Seq++;
            seq = state.Seq;
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            PushRaw(state, seq, data, ts);

            var committed = state.Buffer.ApplyChunk(data);
            foreach (var line in committed)
            {
                state.History.Push(line);
            }

            raw = new
            {
                v = 1,
                type = "term.raw",
                instance_id = state.Id,
                node_id = _nodeId,
                node_name = _nodeName,
                seq,
                ts,
                data
            };

            patch = BuildPatch(state);
            peers = state.Clients.ToList();
        }

        Raw?.Invoke(state.Id, raw);
        Patch?.Invoke(state.Id, patch);
        _ = BroadcastAsync(peers, patch);
    }

    private async Task OnExitedAsync(InstanceState state, int? code)
    {
        List<WebSocket> peers;
        lock (state.Sync)
        {
            state.Status = "exited";
            peers = state.Clients.ToList();
            state.Clients.Clear();
        }

        var exit = new
        {
            v = 1,
            type = "term.exit",
            instance_id = state.Id,
            node_id = _nodeId,
            node_name = _nodeName,
            code = code ?? 0,
            signal = (string?)null,
            message = "Process completed"
        };

        Exited?.Invoke(state.Id, exit);
        await BroadcastAsync(peers, exit);
        foreach (var peer in peers)
        {
            try
            {
                if (peer.State == WebSocketState.Open)
                {
                    await peer.CloseAsync(WebSocketCloseStatus.NormalClosure, "exit", CancellationToken.None);
                }
            }
            catch
            {
            }
        }

        _instances.TryRemove(state.Id, out _);
        await state.Pty.DisposeAsync();
    }

    private void PushRaw(InstanceState state, int seq, string chunk, long ts)
    {
        var bytes = Encoding.UTF8.GetByteCount(chunk);
        state.RawChunks.Add(new InstanceState.RawChunk
        {
            Seq = seq,
            Ts = ts,
            Data = chunk,
            Bytes = bytes
        });
        state.RawBytes += bytes;
        while (state.RawBytes > _rawReplayMaxBytes && state.RawChunks.Count > 1)
        {
            var removed = state.RawChunks[0];
            state.RawChunks.RemoveAt(0);
            state.RawBytes -= removed.Bytes;
        }
    }

    private object BuildPatch(InstanceState state)
    {
        var lines = GetVisibleTail(state.Buffer.VisibleLines, state.Rows);
        var cursor = BuildCursor(state, lines);
        var changed = new List<object>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var signature = line;
            var previous = i < state.LastVisibleSignatures.Count ? state.LastVisibleSignatures[i] : null;
            if (!string.Equals(previous, signature, StringComparison.Ordinal))
            {
                changed.Add(new { y = i, segs = new object[] { new object[] { line, 0 } } });
            }
        }

        state.LastVisibleSignatures = lines;

        return new
        {
            v = 1,
            type = "term.patch",
            instance_id = state.Id,
            node_id = _nodeId,
            node_name = _nodeName,
            instance_epoch = state.InstanceEpoch,
            render_epoch = state.RenderEpoch,
            seq = state.Seq,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            size = new { cols = state.Cols, rows = state.Rows },
            cursor,
            styles = new Dictionary<string, object>
            {
                ["0"] = new { fg = (int?)null, bg = (int?)null, bold = false, italic = false, underline = false, inverse = false }
            },
            rows = changed
        };
    }

    private object Snapshot(InstanceState state, bool advanceSeq)
    {
        lock (state.Sync)
        {
            var snapshot = BuildSnapshotUnsafe(state);
            RecordMetric("snapshot_sent_total");
            return snapshot;
        }
    }

    private object BuildSnapshotUnsafe(InstanceState state)
    {
        var lines = GetVisibleTail(state.Buffer.VisibleLines, state.Rows);
        var cursor = BuildCursor(state, lines);
        state.LastVisibleSignatures = lines;
        var ansi = string.Concat(state.RawChunks.Select(x => x.Data));
        var ansiTruncated = state.RawChunks.Count > 0 && state.RawChunks[0].Seq > 1;

        return new
        {
            v = 1,
            type = "term.snapshot",
            instance_id = state.Id,
            node_id = _nodeId,
            node_name = _nodeName,
            instance_epoch = state.InstanceEpoch,
            render_epoch = state.RenderEpoch,
            owner_connection_id = state.DisplayOwnerConnectionId,
            seq = state.Seq,
            base_seq = state.Seq,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            size = new { cols = state.Cols, rows = state.Rows },
            cursor,
            ansi,
            ansi_truncated = ansiTruncated,
            styles = new Dictionary<string, object>
            {
                ["0"] = new { fg = (int?)null, bg = (int?)null, bold = false, italic = false, underline = false, inverse = false }
            },
            rows = lines.Select((line, index) => new { y = index, segs = new object[] { new object[] { line, 0 } } }).ToList(),
            history = new { available = state.History.Count, newest_cursor = state.History.NewestCursor() }
        };
    }

    public bool IsDisplayOwner(string instanceId, string connectionId)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return false;
        }

        lock (state.Sync)
        {
            return string.Equals(state.DisplayOwnerConnectionId, connectionId, StringComparison.Ordinal);
        }
    }

    public object? SetDisplayOwner(string instanceId, string? connectionId)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return null;
        }

        object? changed = null;
        lock (state.Sync)
        {
            var normalized = (connectionId ?? string.Empty).Trim();
            if (string.Equals(state.DisplayOwnerConnectionId, normalized, StringComparison.Ordinal))
            {
                return null;
            }

            state.DisplayOwnerConnectionId = normalized;
            changed = BuildOwnerChangedUnsafe(state);
        }

        StateChanged?.Invoke(state.Id, changed);
        return changed;
    }

    public string? GetDisplayOwner(string instanceId)
    {
        var state = Get(instanceId);
        if (state is null)
        {
            return null;
        }

        lock (state.Sync)
        {
            return state.DisplayOwnerConnectionId;
        }
    }

    public IReadOnlyDictionary<string, long> MetricsSnapshot()
    {
        return _metrics.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    }

    public void RecordMetric(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _metrics.AddOrUpdate(name.Trim(), 1, static (_, current) => current + 1);
    }

    private object BuildOwnerChangedUnsafe(InstanceState state)
    {
        return new
        {
            v = 1,
            type = "term.owner.changed",
            instance_id = state.Id,
            node_id = _nodeId,
            node_name = _nodeName,
            instance_epoch = state.InstanceEpoch,
            render_epoch = state.RenderEpoch,
            owner_connection_id = state.DisplayOwnerConnectionId,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static long GetRenderEpoch(object snapshot)
    {
        var element = JsonSerializer.SerializeToElement(snapshot);
        if (element.TryGetProperty("render_epoch", out var renderEpoch) && renderEpoch.TryGetInt64(out var value))
        {
            return value;
        }

        return 0;
    }

    private static object BuildCursor(InstanceState state, IReadOnlyList<string> lines)
    {
        var safeCols = Math.Max(1, state.Cols);
        var safeRows = Math.Max(1, state.Rows);

        var y = lines.Count <= 0 ? 0 : lines.Count - 1;
        if (state.Buffer.IsOnFreshLine && lines.Count < safeRows)
        {
            y = lines.Count;
        }

        var x = state.Buffer.IsOnFreshLine
            ? 0
            : Math.Clamp(state.Buffer.CursorColumn, 0, safeCols - 1);
        y = Math.Clamp(y, 0, safeRows - 1);
        return new { x, y, visible = true };
    }

    private static List<string> GetVisibleTail(IReadOnlyList<string> lines, int rows)
    {
        return lines.Skip(Math.Max(0, lines.Count - rows)).ToList();
    }

    private static int SanitizeDimension(int value, int fallback, int min, int max)
    {
        if (value <= 0)
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static List<string> EnsureInteractiveShellArgs(string executable, List<string> args)
    {
        if (IsBashOrZshExecutable(executable))
        {
            return EnsureBashOrZshInteractiveLoginArgs(args);
        }

        if (!IsShExecutable(executable))
        {
            return args;
        }

        if (HasShellFlag(args, "i", "--interactive") || HasShellFlag(args, "c", "--command"))
        {
            return args;
        }

        if (args.Any(arg =>
        {
            var value = (arg ?? string.Empty).Trim();
            return value.Length > 0 && !value.StartsWith("-", StringComparison.Ordinal);
        }))
        {
            return args;
        }

        var normalized = new List<string>(args.Count + 1) { "-i" };
        normalized.AddRange(args);
        return normalized;
    }

    private static List<string> EnsureBashOrZshInteractiveLoginArgs(List<string> args)
    {
        if (HasShellFlag(args, "c", "--command"))
        {
            return args;
        }

        if (args.Any(arg =>
        {
            var value = (arg ?? string.Empty).Trim();
            return value.Length > 0 && !value.StartsWith("-", StringComparison.Ordinal);
        }))
        {
            return args;
        }

        var hasInteractive = HasShellFlag(args, "i", "--interactive");
        var hasLogin = HasShellFlag(args, "l", "--login");
        if (hasInteractive && hasLogin)
        {
            return args;
        }

        var normalized = new List<string>(args);
        if (!hasLogin)
        {
            normalized.Insert(0, "-l");
        }
        if (!hasInteractive)
        {
            normalized.Insert(0, "-i");
        }

        return normalized;
    }

    private static bool IsBashOrZshExecutable(string executable)
    {
        var fileName = Path.GetFileName((executable ?? string.Empty).Trim()).ToLowerInvariant();
        return fileName is "bash" or "bash.exe" or "zsh" or "zsh.exe";
    }

    private static bool IsShExecutable(string executable)
    {
        var fileName = Path.GetFileName((executable ?? string.Empty).Trim()).ToLowerInvariant();
        return fileName is "sh" or "sh.exe";
    }

    private static bool HasShellFlag(IEnumerable<string> args, string shortFlag, string longFlag)
    {
        foreach (var arg in args)
        {
            var value = (arg ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (string.Equals(value, longFlag, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(value, $"-{shortFlag}", StringComparison.Ordinal))
            {
                return true;
            }

            if (IsShortFlagCluster(value) && value.IndexOf(shortFlag, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsShortFlagCluster(string arg)
    {
        if (arg.Length < 3 || arg[0] != '-' || arg[1] == '-')
        {
            return false;
        }

        for (var i = 1; i < arg.Length; i++)
        {
            if (!char.IsLetter(arg[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> NormalizePathPrefixes(IReadOnlyList<string>? pathPrefixes)
    {
        if (pathPrefixes is null || pathPrefixes.Count == 0)
        {
            return [];
        }

        return pathPrefixes
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildPathWithPrefixes(string? currentPath, IReadOnlyList<string> prefixes)
    {
        var items = new List<string>();
        foreach (var prefix in prefixes)
        {
            var normalized = (prefix ?? string.Empty).Trim();
            if (normalized.Length == 0 || items.Contains(normalized, StringComparer.Ordinal))
            {
                continue;
            }
            items.Add(normalized);
        }

        foreach (var segment in (currentPath ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.Length == 0 || items.Contains(segment, StringComparer.Ordinal))
            {
                continue;
            }
            items.Add(segment);
        }

        return string.Join(Path.PathSeparator, items);
    }

    private static string? ResolveWithinBase(string basePath, string? inputPath)
    {
        var root = Path.GetFullPath(basePath);
        var path = string.IsNullOrWhiteSpace(inputPath) ? root : Path.GetFullPath(inputPath.Trim());
        if (path == root || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return path;
        }

        return null;
    }

    private static async Task BroadcastAsync(IEnumerable<WebSocket> peers, object payload)
    {
        foreach (var peer in peers)
        {
            await SendAsync(peer, payload, CancellationToken.None);
        }
    }

    public static async Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private InstanceSummary ToSummary(InstanceState state)
    {
        var clients = 0;
        lock (state.Sync)
        {
            clients = state.Clients.Count;
        }

        return new InstanceSummary
        {
            Id = state.Id,
            Command = state.Command,
            Cwd = state.Cwd,
            Cols = state.Cols,
            Rows = state.Rows,
            CreatedAt = state.CreatedAt.ToString("O"),
            Status = state.Status,
            Clients = clients,
            NodeId = _nodeId,
            NodeName = _nodeName,
            NodeRole = _nodeRole,
            NodeOnline = true
        };
    }

    public sealed class InstanceState
    {
        public object Sync { get; } = new();
        public string Id { get; init; } = string.Empty;
        public string Command { get; init; } = string.Empty;
        public string Cwd { get; init; } = string.Empty;
        public int Cols { get; set; }
        public int Rows { get; set; }
        public DateTimeOffset CreatedAt { get; init; }
        public string Status { get; set; } = "running";
        public long InstanceEpoch { get; set; }
        public long RenderEpoch { get; set; }
        public string DisplayOwnerConnectionId { get; set; } = string.Empty;
        public int Seq { get; set; }
        public required IPtyRuntimeSession Pty { get; init; }
        public required HistoryRing History { get; init; }
        public required TerminalStateBuffer Buffer { get; init; }
        public HashSet<WebSocket> Clients { get; } = [];
        public List<RawChunk> RawChunks { get; } = [];
        public int RawBytes { get; set; }
        public List<string> LastVisibleSignatures { get; set; } = [];

        public sealed class RawChunk
        {
            public required int Seq { get; init; }
            public required long Ts { get; init; }
            public required string Data { get; init; }
            public required int Bytes { get; init; }
        }
    }

    public sealed class ResizeDecision
    {
        public bool Found { get; init; }
        public bool Accepted { get; init; }
        public object? Snapshot { get; init; }
        public int Cols { get; init; }
        public int Rows { get; init; }
        public long RenderEpoch { get; init; }
        public string OwnerConnectionId { get; init; } = string.Empty;

        public static ResizeDecision NotFound() => new() { Found = false };

        public static ResizeDecision Rejected(int cols, int rows, long renderEpoch, string? ownerConnectionId) => new()
        {
            Found = true,
            Accepted = false,
            Cols = cols,
            Rows = rows,
            RenderEpoch = renderEpoch,
            OwnerConnectionId = (ownerConnectionId ?? string.Empty).Trim()
        };

        public static ResizeDecision FromAccepted(object snapshot, int cols, int rows, long renderEpoch) => new()
        {
            Found = true,
            Accepted = true,
            Snapshot = snapshot,
            Cols = cols,
            Rows = rows,
            RenderEpoch = renderEpoch
        };
    }
}
