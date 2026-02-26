using System.Collections.Concurrent;
using System.Collections;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
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

    public event Action<string, object>? Patch;
    public event Action<string, object>? Raw;
    public event Action<string, object>? Exited;
    public string NodeId => _nodeId;
    public string NodeName => _nodeName;
    public string NodeRole => _nodeRole;

    public InstanceManager(IPtyEngine ptyEngine, int historyLimit, int rawReplayMaxBytes, int defaultCols, int defaultRows, string nodeId, string nodeName, string nodeRole)
    {
        _ptyEngine = ptyEngine;
        _historyLimit = Math.Max(1, historyLimit);
        _rawReplayMaxBytes = Math.Max(1024, rawReplayMaxBytes);
        _defaultCols = Math.Clamp(defaultCols, 1, 500);
        _defaultRows = Math.Clamp(defaultRows, 1, 300);
        _nodeId = nodeId;
        _nodeName = nodeName;
        _nodeRole = nodeRole;
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
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            env[(string)entry.Key] = Convert.ToString(entry.Value) ?? string.Empty;
        }

        foreach (var kv in input.Env ?? [])
        {
            env[kv.Key] = kv.Value;
        }

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
            Buffer = new TerminalStateBuffer(_historyLimit * 2)
        };

        runtime.OutputReceived += data => OnOutput(state, data);
        runtime.Exited += code => _ = OnExitedAsync(state, code);

        if (!_instances.TryAdd(id, state))
        {
            await runtime.DisposeAsync();
            throw new InvalidOperationException("instance id collision");
        }

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

        var safeCols = SanitizeDimension(cols, state.Cols, 1, 500);
        var safeRows = SanitizeDimension(rows, state.Rows, 1, 300);
        state.Cols = safeCols;
        state.Rows = safeRows;
        _ = state.Pty.ResizeAsync(safeCols, safeRows, CancellationToken.None);
        return Snapshot(state, advanceSeq: true);
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
            return string.Concat(state.RawChunks);
        }
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
        lock (state.Sync)
        {
            PushRaw(state, data);

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
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
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

    private void PushRaw(InstanceState state, string chunk)
    {
        state.RawChunks.Add(chunk);
        state.RawBytes += Encoding.UTF8.GetByteCount(chunk);
        while (state.RawBytes > _rawReplayMaxBytes && state.RawChunks.Count > 1)
        {
            var removed = state.RawChunks[0];
            state.RawChunks.RemoveAt(0);
            state.RawBytes -= Encoding.UTF8.GetByteCount(removed);
        }
    }

    private object BuildPatch(InstanceState state)
    {
        state.Seq++;
        var lines = GetVisibleTail(state.Buffer.VisibleLines, state.Rows);
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
            seq = state.Seq,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            cursor = new { x = 0, y = Math.Max(0, lines.Count - 1), visible = true },
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
            if (advanceSeq)
            {
                state.Seq++;
            }

            var lines = GetVisibleTail(state.Buffer.VisibleLines, state.Rows);
            state.LastVisibleSignatures = lines;

            return new
            {
                v = 1,
                type = "term.snapshot",
                instance_id = state.Id,
                node_id = _nodeId,
                node_name = _nodeName,
                seq = state.Seq,
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                size = new { cols = state.Cols, rows = state.Rows },
                cursor = new { x = 0, y = Math.Max(0, lines.Count - 1), visible = true },
                styles = new Dictionary<string, object>
                {
                    ["0"] = new { fg = (int?)null, bg = (int?)null, bold = false, italic = false, underline = false, inverse = false }
                },
                rows = lines.Select((line, index) => new { y = index, segs = new object[] { new object[] { line, 0 } } }).ToList(),
                history = new { available = state.History.Count, newest_cursor = state.History.NewestCursor() }
            };
        }
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
        public int Seq { get; set; }
        public required IPtyRuntimeSession Pty { get; init; }
        public required HistoryRing History { get; init; }
        public required TerminalStateBuffer Buffer { get; init; }
        public HashSet<WebSocket> Clients { get; } = [];
        public List<string> RawChunks { get; } = [];
        public int RawBytes { get; set; }
        public List<string> LastVisibleSignatures { get; set; } = [];
    }
}
