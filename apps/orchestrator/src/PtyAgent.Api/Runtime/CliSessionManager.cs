using System.Collections.Concurrent;
using PtyAgent.Api.Domain;
using PtyAgent.Api.Infrastructure;
using PtyAgent.Api.Runtime.Terminal;
using PtyAgent.Api.Services;

namespace PtyAgent.Api.Runtime;

public sealed class CliSessionManager
{
    private readonly SqliteStore _store;
    private readonly RuntimeEventPublisher _publisher;
    private readonly SqliteOptions _options;
    private readonly RuntimeOptions _runtimeOptions;
    private readonly ILogger<CliSessionManager> _logger;
    private readonly IReadOnlyDictionary<string, ITerminalBackend> _backends;
    private readonly ConcurrentDictionary<Guid, SessionRuntime> _sessions = new();

    public CliSessionManager(
        SqliteStore store,
        RuntimeEventPublisher publisher,
        SqliteOptions options,
        RuntimeOptions runtimeOptions,
        IEnumerable<ITerminalBackend> backends,
        ILogger<CliSessionManager> logger)
    {
        _store = store;
        _publisher = publisher;
        _options = options;
        _runtimeOptions = runtimeOptions;
        _logger = logger;
        _backends = backends.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ExecutionSession> StartAsync(Guid taskId, string cliType, string mode, string command, CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var workdir = Path.Combine(_options.WorkdirsPath, taskId.ToString(), sessionId.ToString());
        Directory.CreateDirectory(workdir);

        var session = new ExecutionSession(
            sessionId,
            taskId,
            cliType,
            workdir,
            null,
            SessionStatus.Starting,
            null,
            DateTimeOffset.UtcNow,
            null,
            mode);

        await _store.InsertSessionAsync(session);

        var logPath = Path.Combine(_options.LogsPath, $"{sessionId}.log");
        var logWriter = new StreamWriter(File.Open(logPath, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };

        ITerminalSession? terminalSession = null;
        Exception? lastError = null;

        foreach (var candidate in ResolveCandidateBackends())
        {
            if (!_backends.TryGetValue(candidate, out var backend))
            {
                continue;
            }

            try
            {
                terminalSession = await backend.StartAsync(
                    new TerminalLaunchOptions(taskId, sessionId, cliType, mode, command, workdir, _runtimeOptions.PtyColumns, _runtimeOptions.PtyRows),
                    cancellationToken);
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Terminal backend {Backend} failed for session {SessionId}", candidate, sessionId);
                await _publisher.PublishAsync(taskId, sessionId, "pty_fallback", "warn", $"backend={candidate} failed, trying next backend");
            }
        }

        if (terminalSession is null)
        {
            logWriter.Dispose();
            throw new InvalidOperationException("No terminal backend is available.", lastError);
        }

        var runtime = new SessionRuntime(terminalSession, logWriter);
        _sessions[sessionId] = runtime;

        terminalSession.Exited += async (_, args) =>
        {
            await OnSessionExitedAsync(taskId, sessionId, mode, runtime, args.ExitCode);
        };

        _ = Task.Run(() => PumpAsync(taskId, sessionId, terminalSession.OutputReader, "stdout", logWriter, cancellationToken), cancellationToken);
        if (terminalSession.ErrorReader is not null)
        {
            _ = Task.Run(() => PumpAsync(taskId, sessionId, terminalSession.ErrorReader, "stderr", logWriter, cancellationToken), cancellationToken);
        }

        await _store.UpdateSessionStatusAsync(sessionId, SessionStatus.Running, terminalSession.Pid);
        await _publisher.PublishAsync(taskId, sessionId, "session_started", "info", $"mode={mode}; pid={terminalSession.Pid}; backend={terminalSession.BackendName}; cli={cliType}");
        return session with { Status = SessionStatus.Running, Pid = terminalSession.Pid };
    }

    public async Task SendInputAsync(Guid sessionId, string input)
    {
        if (!_sessions.TryGetValue(sessionId, out var runtime))
        {
            throw new InvalidOperationException("Session is not active.");
        }

        await runtime.TerminalSession.SendInputAsync(input, CancellationToken.None);
        await runtime.LogWriter.WriteLineAsync($"[STDIN] {input}");
    }

    public async Task TerminateAsync(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var runtime))
        {
            return;
        }

        try
        {
            await runtime.TerminalSession.TerminateAsync(CancellationToken.None);
            await _store.UpdateSessionStatusAsync(sessionId, SessionStatus.Terminated, endedAt: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to terminate session {SessionId}", sessionId);
        }
    }

    private async Task OnSessionExitedAsync(Guid taskId, Guid sessionId, string mode, SessionRuntime runtime, int exitCode)
    {
        try
        {
            await runtime.LogWriter.WriteLineAsync($"[EXIT] code={exitCode} at {DateTimeOffset.UtcNow:O}");
            var status = exitCode == 0 ? SessionStatus.Exited : SessionStatus.Failed;
            await _store.UpdateSessionStatusAsync(sessionId, status, endedAt: DateTimeOffset.UtcNow);
            await _publisher.PublishAsync(taskId, sessionId, "session_exited", status == SessionStatus.Exited ? "info" : "error", $"mode={mode}; code={exitCode}");
        }
        finally
        {
            runtime.LogWriter.Dispose();
            await runtime.TerminalSession.DisposeAsync();
            _sessions.TryRemove(sessionId, out _);
        }
    }

    private async Task PumpAsync(Guid taskId, Guid sessionId, TextReader reader, string source, StreamWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                await writer.WriteLineAsync($"[{source.ToUpperInvariant()}] {line}");
                await _publisher.PublishAsync(taskId, sessionId, "session_output", source == "stderr" ? "warn" : "info", line);
            }
        }
        catch (Exception ex)
        {
            if (ex is ObjectDisposedException)
            {
                return;
            }

            _logger.LogWarning(ex, "Output pump failed for session {SessionId}", sessionId);
        }
    }

    private IEnumerable<string> ResolveCandidateBackends()
    {
        var configured = _runtimeOptions.TerminalBackend ?? "auto";

        if (configured.Equals("process", StringComparison.OrdinalIgnoreCase))
        {
            yield return "process";
            yield break;
        }

        if (configured.Equals("nodepty", StringComparison.OrdinalIgnoreCase))
        {
            yield return "nodepty";
            yield return "process";
            yield break;
        }

        yield return "nodepty";
        yield return "process";
    }

    private sealed class SessionRuntime
    {
        public SessionRuntime(ITerminalSession terminalSession, StreamWriter logWriter)
        {
            TerminalSession = terminalSession;
            LogWriter = logWriter;
        }

        public ITerminalSession TerminalSession { get; }
        public StreamWriter LogWriter { get; }
    }
}
