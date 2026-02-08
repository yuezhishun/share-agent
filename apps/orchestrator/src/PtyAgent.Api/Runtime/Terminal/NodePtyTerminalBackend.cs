using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using PtyAgent.Api.Infrastructure;

namespace PtyAgent.Api.Runtime.Terminal;

public sealed class NodePtyTerminalBackend : ITerminalBackend
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RuntimeOptions _runtimeOptions;

    public NodePtyTerminalBackend(IHttpClientFactory httpClientFactory, RuntimeOptions runtimeOptions)
    {
        _httpClientFactory = httpClientFactory;
        _runtimeOptions = runtimeOptions;
    }

    public string Name => "nodepty";

    public async Task<ITerminalSession> StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient("terminal-gateway");
        http.Timeout = TimeSpan.FromMilliseconds(_runtimeOptions.TerminalGatewayTimeoutMs);
        http.DefaultRequestHeaders.Remove("X-Internal-Token");
        http.DefaultRequestHeaders.Add("X-Internal-Token", _runtimeOptions.TerminalGatewayToken);

        var createReq = new
        {
            sessionId = options.SessionId,
            taskId = options.TaskId,
            cliType = options.CliType,
            mode = options.Mode,
            shell = ResolveExecutable(options.CliType),
            cwd = options.Workdir,
            command = default(string?),
            args = ResolveArgs(options.CliType, options.Command),
            env = new Dictionary<string, string>(),
            cols = options.Cols,
            rows = options.Rows
        };

        using var createRes = await http.PostAsync("/internal/sessions", JsonContent.Create(createReq), cancellationToken);
        createRes.EnsureSuccessStatusCode();

        var created = await createRes.Content.ReadFromJsonAsync<StartSessionResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Invalid terminal-gateway response.");

        var wsBase = new Uri(_runtimeOptions.TerminalGatewayBaseUrl);
        var wsScheme = wsBase.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var wsUri = new Uri($"{wsScheme}://{wsBase.Host}:{wsBase.Port}/ws/terminal?sessionId={options.SessionId}&token={Uri.EscapeDataString(_runtimeOptions.TerminalGatewayToken)}");

        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Accept", "application/json");
        await ws.ConnectAsync(wsUri, cancellationToken);

        return new NodePtyTerminalSession(http, ws, options.SessionId, created.Pid);
    }

    private static string ResolveExecutable(string cliType)
    {
        var normalized = (cliType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "claude")
        {
            return "claude";
        }
        if (normalized == "codex" || string.IsNullOrWhiteSpace(normalized))
        {
            return "codex";
        }
        if (normalized == "bash")
        {
            return "/bin/bash";
        }
        return normalized;
    }

    private static string[] ResolveArgs(string cliType, string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Array.Empty<string>();
        }

        var executable = ResolveExecutable(cliType);
        if (executable.Contains("bash", StringComparison.OrdinalIgnoreCase)
            || executable.Contains("zsh", StringComparison.OrdinalIgnoreCase)
            || executable.EndsWith("/sh", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("sh", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "-lc", command };
        }

        return new[] { command };
    }

    private sealed class NodePtyTerminalSession : ITerminalSession
    {
        private readonly HttpClient _http;
        private readonly ClientWebSocket _socket;
        private readonly Guid _sessionId;
        private readonly LineChannelReader _output = new();
        private readonly CancellationTokenSource _cts = new();

        public NodePtyTerminalSession(HttpClient http, ClientWebSocket socket, Guid sessionId, int pid)
        {
            _http = http;
            _socket = socket;
            _sessionId = sessionId;
            Pid = pid;
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }

        public event EventHandler<TerminalExitedEventArgs>? Exited;

        public int Pid { get; }
        public string BackendName => "nodepty";
        public TextReader OutputReader => _output;
        public TextReader? ErrorReader => null;

        public async Task SendInputAsync(string input, CancellationToken cancellationToken)
        {
            var payload = new { data = input + "\r" };
            using var res = await _http.PostAsync($"/internal/sessions/{_sessionId}/input", JsonContent.Create(payload), cancellationToken);
            res.EnsureSuccessStatusCode();
        }

        public async Task TerminateAsync(CancellationToken cancellationToken)
        {
            using var res = await _http.PostAsync($"/internal/sessions/{_sessionId}/terminate", JsonContent.Create(new { signal = "SIGTERM" }), cancellationToken);
            res.EnsureSuccessStatusCode();
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None);
                }
            }
            catch
            {
                // ignore close race
            }
            finally
            {
                _socket.Dispose();
                _output.Complete();
                _cts.Dispose();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var segment = new ArraySegment<byte>(buffer);
            var textBuilder = new StringBuilder();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await _socket.ReceiveAsync(segment, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    textBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    var message = textBuilder.ToString();
                    textBuilder.Clear();
                    HandleMessage(message);
                }
            }
            catch (OperationCanceledException)
            {
                // expected on dispose
            }
            catch
            {
                Exited?.Invoke(this, new TerminalExitedEventArgs(1));
                _output.Complete();
            }
        }

        private void HandleMessage(string raw)
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            if (string.Equals(type, "output", StringComparison.OrdinalIgnoreCase))
            {
                var data = root.GetProperty("data").GetString() ?? string.Empty;
                _output.Push(data);
                return;
            }

            if (string.Equals(type, "exit", StringComparison.OrdinalIgnoreCase))
            {
                var exitCode = root.TryGetProperty("exitCode", out var v) && v.TryGetInt32(out var code) ? code : 0;
                _output.Complete();
                Exited?.Invoke(this, new TerminalExitedEventArgs(exitCode));
            }
        }

    }

    private sealed class LineChannelReader : TextReader
    {
        private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();
        private readonly StringBuilder _pending = new();

        public void Push(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            _pending.Append(chunk);
            while (true)
            {
                var text = _pending.ToString();
                var idx = text.IndexOf('\n', StringComparison.Ordinal);
                if (idx < 0)
                {
                    break;
                }

                var line = text[..idx].TrimEnd('\r');
                _channel.Writer.TryWrite(line);
                _pending.Clear();
                _pending.Append(text[(idx + 1)..]);
            }
        }

        public void Complete()
        {
            if (_pending.Length > 0)
            {
                _channel.Writer.TryWrite(_pending.ToString().TrimEnd('\r'));
                _pending.Clear();
            }
            _channel.Writer.TryComplete();
        }

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _channel.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }
    }

    private sealed record StartSessionResponse(Guid SessionId, int Pid, string Status, string Backend);
}
