using System.Collections.Concurrent;
using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Pty;

namespace TerminalGateway.Api.Services;

public sealed class SessionManager
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly IPtyEngine _ptyEngine;
    private readonly ProfileService _profileService;
    private readonly SettingsService _settingsService;
    private readonly WriteTokenService _writeTokenService;
    private readonly ISystemTimeProvider _timeProvider;
    private readonly int _maxOutputBufferBytes;

    public SessionManager(
        IPtyEngine ptyEngine,
        ProfileService profileService,
        SettingsService settingsService,
        WriteTokenService writeTokenService,
        ISystemTimeProvider timeProvider,
        int maxOutputBufferBytes)
    {
        _ptyEngine = ptyEngine;
        _profileService = profileService;
        _settingsService = settingsService;
        _writeTokenService = writeTokenService;
        _timeProvider = timeProvider;
        _maxOutputBufferBytes = Math.Clamp(maxOutputBufferBytes, 1024, 64 * 1024 * 1024);
    }

    public async Task<(object Session, string WriteToken)> CreateAsync(CreateSessionRequest options, CancellationToken cancellationToken = default)
    {
        var sessionId = string.IsNullOrWhiteSpace(options.SessionId) ? Guid.NewGuid().ToString() : options.SessionId.Trim();
        if (_sessions.ContainsKey(sessionId))
        {
            throw new InvalidOperationException($"session already exists: {sessionId}");
        }

        var taskId = string.IsNullOrWhiteSpace(options.TaskId) ? Guid.NewGuid().ToString() : options.TaskId.Trim();
        var launch = ResolveLaunchOptions(options, sessionId, taskId);
        var runtime = await _ptyEngine.CreateAsync(new PtyLaunchOptions
        {
            Executable = launch.Shell,
            Args = launch.Args,
            Cwd = launch.Cwd,
            Env = launch.Env,
            Cols = launch.Cols,
            Rows = launch.Rows
        }, cancellationToken);

        var now = _timeProvider.UtcNow;
        var writeToken = _writeTokenService.GenerateToken();
        var record = new SessionRecord
        {
            SessionId = sessionId,
            TaskId = taskId,
            CliType = launch.CliType,
            Mode = launch.Mode,
            ProfileId = launch.ProfileId,
            Title = launch.Title,
            Shell = launch.Shell,
            Cwd = launch.Cwd,
            Args = [.. launch.Args],
            CreatedAt = now,
            LastActivityAt = now,
            WriteTokenHash = _writeTokenService.HashToken(writeToken),
            MaxOutputBufferBytes = _maxOutputBufferBytes,
            ReplayBuffer = new SessionReplayBuffer(),
            PtySession = runtime
        };

        runtime.OutputReceived += data => OnOutput(record, data);
        runtime.Exited += code => _ = OnExitedAsync(record, code);

        if (!_sessions.TryAdd(sessionId, record))
        {
            await runtime.DisposeAsync();
            throw new InvalidOperationException($"session already exists: {sessionId}");
        }

        if (launch.StartupCommands.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(100, cancellationToken);
                if (record.Status != "running")
                {
                    return;
                }
                foreach (var cmd in launch.StartupCommands)
                {
                    await runtime.WriteAsync(cmd + "\r", cancellationToken);
                }
            }, cancellationToken);
        }

        return (SummarizeSession(record), writeToken);
    }

    private void OnOutput(SessionRecord record, string data)
    {
        var peers = new List<System.Net.WebSockets.WebSocket>();
        SessionReplayBuffer.AppendResult append;
        lock (record.Sync)
        {
            record.LastActivityAt = _timeProvider.UtcNow;
            append = record.ReplayBuffer.Append(data, record.MaxOutputBufferBytes);
            if (append.Truncated)
            {
                record.OutputTruncated = true;
            }
            peers.AddRange(record.Subscribers);
        }

        var frame = new
        {
            type = "output",
            sessionId = record.SessionId,
            stream = "stdout",
            data,
            seqStart = append.SeqStart,
            seqEnd = append.SeqEnd,
            truncatedSince = false
        };
        _ = BroadcastAsync(peers, frame);
    }

    private async Task OnExitedAsync(SessionRecord record, int? code)
    {
        List<System.Net.WebSockets.WebSocket> peers;
        lock (record.Sync)
        {
            record.Status = "exited";
            record.ExitCode = code;
            record.LastActivityAt = _timeProvider.UtcNow;
            peers = [.. record.Subscribers];
            record.Subscribers.Clear();
            record.WriterPeer = null;
        }

        var frame = new { type = "exit", sessionId = record.SessionId, exitCode = code, signal = (string?)null };
        await BroadcastAsync(peers, frame);
        foreach (var peer in peers)
        {
            try
            {
                if (peer.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    await peer.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "session exited", CancellationToken.None);
                }
            }
            catch
            {
            }
        }
    }

    public object Status(string sessionId) => SummarizeSession(RequireSession(sessionId));

    public IReadOnlyList<object> List(bool includeExited, string? profileId, string? taskId)
    {
        return _sessions.Values
            .Where(x => includeExited || x.Status == "running")
            .Where(x => string.IsNullOrWhiteSpace(profileId) || x.ProfileId == profileId)
            .Where(x => string.IsNullOrWhiteSpace(taskId) || x.TaskId == taskId)
            .OrderByDescending(x => x.LastActivityAt)
            .Select(SummarizeSession)
            .ToList();
    }

    public async Task WriteAsync(string sessionId, string? data, CancellationToken cancellationToken = default)
    {
        var s = RequireSession(sessionId);
        await s.PtySession.WriteAsync(data ?? string.Empty, cancellationToken);
        s.LastActivityAt = _timeProvider.UtcNow;
    }

    public async Task ResizeAsync(string sessionId, int? cols, int? rows, CancellationToken cancellationToken = default)
    {
        var s = RequireSession(sessionId);
        await s.PtySession.ResizeAsync(cols ?? 160, rows ?? 40, cancellationToken);
        s.LastActivityAt = _timeProvider.UtcNow;
    }

    public async Task TerminateAsync(string sessionId, string? signal, CancellationToken cancellationToken = default)
    {
        var s = RequireSession(sessionId);
        await s.PtySession.TerminateAsync(signal ?? "SIGTERM", cancellationToken);
        s.LastActivityAt = _timeProvider.UtcNow;
    }

    public object Remove(string sessionId)
    {
        var s = RequireSession(sessionId);
        if (s.Status == "running")
        {
            throw new InvalidOperationException("cannot remove running session");
        }

        _sessions.TryRemove(sessionId, out _);
        return new { ok = true, sessionId };
    }

    public object PruneExited()
    {
        var removed = 0;
        foreach (var kv in _sessions.ToArray())
        {
            if (kv.Value.Status != "exited")
            {
                continue;
            }
            if (_sessions.TryRemove(kv.Key, out _))
            {
                removed++;
            }
        }

        return new { ok = true, removed };
    }

    public object Snapshot(string sessionId, int? limitBytes)
    {
        var s = RequireSession(sessionId);
        var max = ValidationHelpers.ClampInt(limitBytes, 1, s.MaxOutputBufferBytes, s.MaxOutputBufferBytes);
        var snapshot = s.ReplayBuffer.Snapshot(max, s.MaxOutputBufferBytes, s.OutputTruncated, s.SessionId, s.Status, s.ExitCode);
        return new
        {
            sessionId = snapshot.SessionId,
            status = snapshot.Status,
            exitCode = snapshot.ExitCode,
            data = snapshot.Data,
            bytes = snapshot.Bytes,
            totalBytes = snapshot.TotalBytes,
            truncated = snapshot.Truncated,
            maxOutputBufferBytes = snapshot.MaxOutputBufferBytes,
            headSeq = snapshot.HeadSeq,
            tailSeq = snapshot.TailSeq
        };
    }

    public object History(string sessionId, int? beforeSeq, int? limitBytes)
    {
        var s = RequireSession(sessionId);
        var max = ValidationHelpers.ClampInt(limitBytes, 1024, s.MaxOutputBufferBytes, 256 * 1024);
        var history = s.ReplayBuffer.History(beforeSeq, max, s.OutputTruncated, s.SessionId);
        return new
        {
            sessionId = history.SessionId,
            chunks = history.Chunks.Select(x => new { data = x.Data, seqStart = x.SeqStart, seqEnd = x.SeqEnd }).ToList(),
            hasMore = history.HasMore,
            nextBeforeSeq = history.NextBeforeSeq,
            truncated = history.Truncated
        };
    }

    public async Task AttachAsync(string sessionId, System.Net.WebSockets.WebSocket peer, bool replay, string replayMode, int? sinceSeq, string? writeToken, CancellationToken cancellationToken)
    {
        var s = RequireSession(sessionId);
        var mode = NormalizeReplayMode(replayMode, replay ? "full" : "none");

        bool writable;
        lock (s.Sync)
        {
            writable = _writeTokenService.IsMatch(writeToken, s.WriteTokenHash);
            if (writable && s.WriterPeer is not null && s.WriterPeer != peer)
            {
                writable = false;
            }
            if (writable)
            {
                s.WriterPeer = peer;
            }
        }

        if (s.Status == "exited")
        {
            var data = s.ReplayBuffer.JoinAllData();
            if ((replay || mode is "full" or "tail") && data.Length > 0)
            {
                await SendAsync(peer, new
                {
                    type = "output",
                    sessionId = s.SessionId,
                    stream = "stdout",
                    data,
                    replay = true,
                    seqStart = s.ReplayBuffer.HeadSeq,
                    seqEnd = s.ReplayBuffer.TailSeq,
                    truncatedSince = false
                }, cancellationToken);
            }

            await SendAsync(peer, new { type = "exit", sessionId = s.SessionId, exitCode = s.ExitCode, signal = (string?)null }, cancellationToken);
            await peer.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "session exited", cancellationToken);
            return;
        }

        lock (s.Sync)
        {
            s.Subscribers.Add(peer);
        }

        await SendAsync(peer, new
        {
            type = "ready",
            sessionId = s.SessionId,
            pid = s.PtySession.Pid,
            status = s.Status,
            writable,
            taskId = s.TaskId,
            profileId = s.ProfileId,
            title = s.Title,
            cwd = s.Cwd,
            shell = s.Shell,
            args = s.Args,
            cliType = s.CliType,
            mode = s.Mode,
            outputBytes = s.ReplayBuffer.OutputBytes,
            outputTruncated = s.OutputTruncated,
            maxOutputBufferBytes = s.MaxOutputBufferBytes,
            headSeq = s.ReplayBuffer.HeadSeq,
            tailSeq = s.ReplayBuffer.TailSeq,
            canDeltaReplay = true
        }, cancellationToken);

        if (replay || mode is "full" or "tail")
        {
            var data = s.ReplayBuffer.JoinAllData();
            if (data.Length > 0)
            {
                await SendAsync(peer, new
                {
                    type = "output",
                    sessionId = s.SessionId,
                    stream = "stdout",
                    data,
                    replay = true,
                    seqStart = s.ReplayBuffer.HeadSeq,
                    seqEnd = s.ReplayBuffer.TailSeq,
                    truncatedSince = false
                }, cancellationToken);
            }
            return;
        }

        if (mode == "none" && sinceSeq.HasValue)
        {
            var delta = s.ReplayBuffer.CollectDelta(sinceSeq.Value);
            if (delta.TruncatedSince)
            {
                await SendAsync(peer, new
                {
                    type = "output",
                    sessionId = s.SessionId,
                    stream = "stdout",
                    data = string.Empty,
                    replay = false,
                    seqStart = s.ReplayBuffer.HeadSeq,
                    seqEnd = s.ReplayBuffer.TailSeq,
                    truncatedSince = true
                }, cancellationToken);
                return;
            }

            foreach (var item in delta.Chunks)
            {
                await SendAsync(peer, new
                {
                    type = "output",
                    sessionId = s.SessionId,
                    stream = "stdout",
                    data = item.Data,
                    replay = false,
                    seqStart = item.SeqStart,
                    seqEnd = item.SeqEnd,
                    truncatedSince = false
                }, cancellationToken);
            }
        }
    }

    public void Detach(string sessionId, System.Net.WebSockets.WebSocket peer)
    {
        if (!_sessions.TryGetValue(sessionId, out var s))
        {
            return;
        }

        lock (s.Sync)
        {
            s.Subscribers.Remove(peer);
            if (s.WriterPeer == peer)
            {
                s.WriterPeer = null;
            }
        }
    }

    public bool IsPeerWritable(string sessionId, System.Net.WebSockets.WebSocket peer)
    {
        var s = RequireSession(sessionId);
        return string.IsNullOrWhiteSpace(s.WriteTokenHash) || s.WriterPeer == peer;
    }

    public IReadOnlyList<ProfileRecord> ListProfiles() => _profileService.List();
    public ProfileRecord CreateProfile(CreateProfileRequest request) => _profileService.Create(request);
    public ProfileRecord UpdateProfile(string profileId, UpdateProfileRequest request) => _profileService.Update(profileId, request);
    public object DeleteProfile(string profileId) => _profileService.Delete(profileId);

    public IReadOnlyList<QuickCommandItem> GetGlobalQuickCommands() => _settingsService.GetGlobalQuickCommands();
    public IReadOnlyList<QuickCommandItem> SetGlobalQuickCommands(IEnumerable<QuickCommandItem> items) => _settingsService.SetGlobalQuickCommands(items);
    public IReadOnlyList<string> GetFsAllowedRoots() => _settingsService.GetFsAllowedRoots();
    public IReadOnlyList<string> SetFsAllowedRoots(IEnumerable<string> items) => _settingsService.SetFsAllowedRoots(items);

    private SessionRecord RequireSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return session;
        }

        throw new InvalidOperationException($"session not found: {sessionId}");
    }

    private static string NormalizeReplayMode(string? replayMode, string fallback)
    {
        var mode = (replayMode ?? string.Empty).Trim().ToLowerInvariant();
        return mode is "none" or "tail" or "full" ? mode : fallback;
    }

    private static async Task BroadcastAsync(IEnumerable<System.Net.WebSockets.WebSocket> peers, object payload)
    {
        var tasks = peers.Select(peer => SendAsync(peer, payload, CancellationToken.None));
        await Task.WhenAll(tasks);
    }

    public static async Task SendAsync(System.Net.WebSockets.WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        if (socket.State != System.Net.WebSockets.WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, cancellationToken);
    }

    private object SummarizeSession(SessionRecord s)
    {
        return new
        {
            sessionId = s.SessionId,
            taskId = s.TaskId,
            cliType = s.CliType,
            mode = s.Mode,
            profileId = s.ProfileId,
            title = s.Title,
            shell = s.Shell,
            cwd = s.Cwd,
            args = s.Args,
            pid = s.PtySession.Pid,
            status = s.Status,
            createdAt = s.CreatedAt.ToString("O"),
            lastActivityAt = s.LastActivityAt.ToString("O"),
            exitCode = s.ExitCode,
            outputBytes = s.ReplayBuffer.OutputBytes,
            outputTruncated = s.OutputTruncated,
            maxOutputBufferBytes = s.MaxOutputBufferBytes,
            backend = "porta-pty"
        };
    }

    private LaunchOptions ResolveLaunchOptions(CreateSessionRequest options, string sessionId, string taskId)
    {
        var profile = _profileService.Get(options.ProfileId);

        var templateContext = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workspaceRoot"] = !string.IsNullOrWhiteSpace(options.WorkspaceRoot) ? options.WorkspaceRoot!.Trim() : Directory.GetCurrentDirectory(),
            ["taskId"] = taskId,
            ["profileName"] = profile?.Name ?? string.Empty
        };

        var cliType = Pick(options.CliType, profile?.CliType, "custom");
        var shell = Pick(RenderTemplate(options.Shell, templateContext), RenderTemplate(profile?.Shell, templateContext), ResolveDefaultExecutable(cliType));
        var cwd = Pick(RenderTemplate(options.Cwd, templateContext), RenderTemplate(profile?.Cwd, templateContext), "/tmp");
        var mode = Pick(options.Mode, "execute");
        var title = Pick(RenderTemplate(options.Title, templateContext), RenderTemplate(profile?.Name, templateContext), sessionId[..Math.Min(8, sessionId.Length)]);
        var cols = ValidationHelpers.ClampInt(options.Cols, 40, 400, 160);
        var rows = ValidationHelpers.ClampInt(options.Rows, 10, 200, 40);

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry pair in Environment.GetEnvironmentVariables())
        {
            env[(string)pair.Key] = Convert.ToString(pair.Value) ?? string.Empty;
        }

        foreach (var kv in profile?.Env ?? [])
        {
            env[kv.Key] = RenderTemplate(kv.Value, templateContext);
        }
        foreach (var kv in options.Env ?? [])
        {
            env[kv.Key] = RenderTemplate(kv.Value, templateContext);
        }

        var command = RenderTemplate(options.Command, templateContext);
        var args = (options.Args ?? profile?.Args ?? [])
            .Select(x => RenderTemplate(x, templateContext))
            .Where(x => x.Length > 0)
            .ToList();
        var startupCommands = (options.StartupCommands ?? profile?.StartupCommands ?? [])
            .Select(x => RenderTemplate(x, templateContext))
            .Where(x => x.Length > 0)
            .ToList();

        List<string> resolvedArgs = [];
        if (command.Length > 0)
        {
            resolvedArgs = IsShellLike(shell) ? ["-lc", command] : [command];
        }
        else if (args.Count > 0)
        {
            resolvedArgs = args;
        }
        else if (IsInteractiveShell(shell))
        {
            resolvedArgs = ["-i"];
        }

        return new LaunchOptions
        {
            ProfileId = profile?.ProfileId,
            Title = title,
            Shell = shell,
            Cwd = cwd,
            CliType = cliType,
            Mode = mode,
            Cols = cols,
            Rows = rows,
            Env = env,
            Args = resolvedArgs,
            StartupCommands = startupCommands
        };
    }

    private static string Pick(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length > 0)
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    private static string RenderTemplate(string? value, IReadOnlyDictionary<string, string> context)
    {
        var input = value ?? string.Empty;
        if (input.Length == 0)
        {
            return string.Empty;
        }

        var output = input;
        output = output.Replace("${workspaceRoot}", context.GetValueOrDefault("workspaceRoot", string.Empty), StringComparison.Ordinal);
        output = output.Replace("${taskId}", context.GetValueOrDefault("taskId", string.Empty), StringComparison.Ordinal);
        output = output.Replace("${profileName}", context.GetValueOrDefault("profileName", string.Empty), StringComparison.Ordinal);
        return output.Trim();
    }

    private static string ResolveDefaultExecutable(string cliType)
    {
        var normalized = cliType.ToLowerInvariant();
        return normalized switch
        {
            "codex" => "codex",
            "claude" => "claude",
            "bash" => "/bin/bash",
            _ => "/bin/bash"
        };
    }

    private static bool IsShellLike(string executable)
    {
        var shell = executable.Trim().ToLowerInvariant();
        return shell.Contains("bash", StringComparison.Ordinal) || shell.Contains("zsh", StringComparison.Ordinal) || shell.EndsWith("/sh", StringComparison.Ordinal) || shell == "sh";
    }

    private static bool IsInteractiveShell(string executable)
    {
        var shell = executable.Trim().ToLowerInvariant();
        return shell.Contains("bash", StringComparison.Ordinal) || shell.Contains("zsh", StringComparison.Ordinal);
    }

    private sealed class LaunchOptions
    {
        public string? ProfileId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Shell { get; set; } = string.Empty;
        public string Cwd { get; set; } = string.Empty;
        public string CliType { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public int Cols { get; set; }
        public int Rows { get; set; }
        public Dictionary<string, string> Env { get; set; } = [];
        public List<string> Args { get; set; } = [];
        public List<string> StartupCommands { get; set; } = [];
    }
}
