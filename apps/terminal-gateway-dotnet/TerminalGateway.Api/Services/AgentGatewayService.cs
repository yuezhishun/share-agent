using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using TerminalGateway.Api.Endpoints;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class AgentGatewayService : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly AgentCatalogService _catalog;
    private readonly GatewayOptions _options;
    private readonly ConcurrentDictionary<string, AgentSessionState> _sessions = new(StringComparer.Ordinal);
    private readonly IHubContext<AgentHub> _hubContext;
    private int _requestId;

    public AgentGatewayService(AgentCatalogService catalog, GatewayOptions options, IHubContext<AgentHub> hubContext)
    {
        _catalog = catalog;
        _options = options;
        _hubContext = hubContext;
    }

    public IReadOnlyList<AgentSessionSummary> ListSessions() => _sessions.Values
        .OrderByDescending(x => x.UpdatedAt)
        .Select(ToSummary)
        .ToList();

    public AgentSessionSummary? GetSession(string gatewaySessionId)
        => _sessions.TryGetValue(gatewaySessionId, out var session) ? ToSummary(session) : null;

    public IReadOnlyList<AgentGatewayEventEnvelope> GetEvents(string gatewaySessionId)
        => GetRequiredSession(gatewaySessionId).Events.ToList();

    public async Task<AgentSessionSummary> ConnectAsync(AgentSessionConnectRequest request, CancellationToken cancellationToken)
    {
        var backend = (request.Backend ?? string.Empty).Trim();
        if (!_catalog.TryGet(backend, out var descriptor))
        {
            throw new InvalidOperationException($"unsupported backend: {backend}");
        }

        if (descriptor.RequiresCustomTransport)
        {
            throw new NotSupportedException($"backend {backend} requires a custom transport adapter and is not available via stdio ACP in this build");
        }

        var workingDirectory = ResolveWorkingDirectory(request.WorkingDirectory);
        var (fileName, args) = _catalog.ResolveLaunch(backend, request.CliPath, request.ExtraArgs);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true
        };
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        foreach (var pair in request.Environment ?? [])
        {
            process.StartInfo.Environment[pair.Key] = pair.Value;
        }

        var gatewaySessionId = Guid.NewGuid().ToString("N");
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId) ? gatewaySessionId : request.ConversationId.Trim();
        var state = new AgentSessionState
        {
            GatewaySessionId = gatewaySessionId,
            ConversationId = conversationId,
            Backend = backend,
            WorkingDirectory = workingDirectory,
            Process = process,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = "starting"
        };

        process.Exited += (_, _) =>
        {
            state.Status = process.ExitCode == 0 ? "exited" : "failed";
            state.UpdatedAt = DateTimeOffset.UtcNow;
            Publish(state, "session.disconnected", new
            {
                exit_code = process.ExitCode,
                status = state.Status
            });
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"failed to start backend {backend}");
        }

        state.StandardInput = process.StandardInput;
        _sessions[gatewaySessionId] = state;
        _ = Task.Run(() => ReadStdoutLoopAsync(state, cancellationToken), CancellationToken.None);
        _ = Task.Run(() => ReadStderrLoopAsync(state, cancellationToken), CancellationToken.None);

        await InitializeSessionAsync(state, request, cancellationToken);
        state.Status = "connected";
        state.UpdatedAt = DateTimeOffset.UtcNow;
        Publish(state, "session.connected", new
        {
            session_id = state.SessionId,
            backend,
            conversation_id = conversationId
        });

        if (!string.IsNullOrWhiteSpace(request.SessionMode))
        {
            await SetModeAsync(gatewaySessionId, request.SessionMode!, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.ModelId))
        {
            await SetModelAsync(gatewaySessionId, request.ModelId!, cancellationToken);
        }

        return ToSummary(state);
    }

    public async Task PromptAsync(string gatewaySessionId, string text, CancellationToken cancellationToken)
    {
        var session = GetRequiredSession(gatewaySessionId);
        await SendRequestAsync<JsonElement>(session, "session/prompt", new
        {
            sessionId = session.SessionId,
            prompt = new[]
            {
                new { type = "text", text }
            }
        }, cancellationToken);
    }

    public async Task CancelAsync(string gatewaySessionId, CancellationToken cancellationToken)
    {
        var session = GetRequiredSession(gatewaySessionId);
        await SendRequestAsync<JsonElement>(session, "session/cancel", new
        {
            sessionId = session.SessionId
        }, cancellationToken);
    }

    public async Task SetModeAsync(string gatewaySessionId, string mode, CancellationToken cancellationToken)
    {
        var session = GetRequiredSession(gatewaySessionId);
        await SendRequestAsync<JsonElement>(session, "session/set_mode", new
        {
            sessionId = session.SessionId,
            mode
        }, cancellationToken);
    }

    public async Task SetModelAsync(string gatewaySessionId, string modelId, CancellationToken cancellationToken)
    {
        var session = GetRequiredSession(gatewaySessionId);
        await SendRequestAsync<JsonElement>(session, "session/set_model", new
        {
            sessionId = session.SessionId,
            modelId
        }, cancellationToken);
    }

    public async Task SetConfigOptionAsync(string gatewaySessionId, string configId, JsonElement value, CancellationToken cancellationToken)
    {
        var session = GetRequiredSession(gatewaySessionId);
        await SendRequestAsync<JsonElement>(session, "session/set_config_option", new
        {
            sessionId = session.SessionId,
            configId,
            value
        }, cancellationToken);
    }

    public async Task RespondPermissionAsync(string gatewaySessionId, AgentPermissionResponseRequest request, CancellationToken cancellationToken)
    {
        var session = GetRequiredSession(gatewaySessionId);
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            throw new InvalidOperationException("requestId is required");
        }

        if (!session.PendingPermissionRequests.TryRemove(request.RequestId, out var jsonRpcId))
        {
            throw new InvalidOperationException($"permission request not found: {request.RequestId}");
        }

        var payload = request.Payload.ValueKind == JsonValueKind.Undefined || request.Payload.ValueKind == JsonValueKind.Null
            ? JsonSerializer.SerializeToElement(new { optionId = request.OptionId ?? "allow" }, Json)
            : request.Payload;

        await WriteMessageAsync(session, new
        {
            jsonrpc = "2.0",
            id = jsonRpcId,
            result = payload
        }, cancellationToken);
    }

    public async Task DisconnectAsync(string gatewaySessionId)
    {
        if (!_sessions.TryRemove(gatewaySessionId, out var session))
        {
            return;
        }

        session.Cts.Cancel();
        try
        {
            if (!session.Process.HasExited)
            {
                session.Process.Kill(entireProcessTree: true);
                await session.Process.WaitForExitAsync();
            }
        }
        catch
        {
        }
        finally
        {
            session.Process.Dispose();
            Publish(session, "session.disconnected", new { status = "disposed" });
        }
    }

    public void Dispose()
    {
        foreach (var sessionId in _sessions.Keys.ToList())
        {
            DisconnectAsync(sessionId).GetAwaiter().GetResult();
        }
    }

    private async Task InitializeSessionAsync(AgentSessionState session, AgentSessionConnectRequest request, CancellationToken cancellationToken)
    {
        await SendRequestAsync<JsonElement>(session, "initialize", new
        {
            protocolVersion = 1,
            clientCapabilities = new
            {
                fs = new
                {
                    readTextFile = true,
                    writeTextFile = true
                }
            }
        }, cancellationToken);

        JsonElement response;
        if (!string.IsNullOrWhiteSpace(request.ResumeSessionId))
        {
            session.SessionId = request.ResumeSessionId!.Trim();
            response = await SendRequestAsync<JsonElement>(session, "session/load", new
            {
                sessionId = session.SessionId
            }, cancellationToken);
        }
        else if (request.InitializeOnly)
        {
            response = default;
        }
        else
        {
            response = await SendRequestAsync<JsonElement>(session, "session/new", new
            {
                cwd = session.WorkingDirectory,
                mcpServers = Array.Empty<object>()
            }, cancellationToken);

            if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("sessionId", out var sessionIdProp))
            {
                session.SessionId = sessionIdProp.GetString();
            }
        }

        session.LastSessionResponse = response;
    }

    private async Task<T> SendRequestAsync<T>(AgentSessionState session, string method, object parameters, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.PendingRequests[requestId] = tcs;
        await WriteMessageAsync(session, new
        {
            jsonrpc = "2.0",
            id = requestId,
            method,
            @params = parameters
        }, cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
        using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));

        var result = await tcs.Task;
        if (typeof(T) == typeof(JsonElement))
        {
            return (T)(object)result;
        }

        return JsonSerializer.Deserialize<T>(result.GetRawText(), Json)
            ?? throw new InvalidOperationException($"failed to deserialize response for {method}");
    }

    private async Task WriteMessageAsync(AgentSessionState session, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        await session.StandardInput!.WriteLineAsync(json.AsMemory(), cancellationToken);
        await session.StandardInput.FlushAsync();
    }

    private async Task ReadStdoutLoopAsync(AgentSessionState session, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);
        while (!linked.IsCancellationRequested && !session.Process.HasExited)
        {
            var line = await session.Process.StandardOutput.ReadLineAsync(linked.Token);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            HandleIncomingMessage(session, line);
        }
    }

    private async Task ReadStderrLoopAsync(AgentSessionState session, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);
        while (!linked.IsCancellationRequested && !session.Process.HasExited)
        {
            var line = await session.Process.StandardError.ReadLineAsync(linked.Token);
            if (line is null)
            {
                break;
            }

            session.UpdatedAt = DateTimeOffset.UtcNow;
            Publish(session, "session.stderr", new { text = line });
        }
    }

    private void HandleIncomingMessage(AgentSessionState session, string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (Exception ex)
        {
            Publish(session, "session.error", new { error = $"invalid json from agent: {ex.Message}", raw = line });
            return;
        }

        using (document)
        {
            var root = document.RootElement.Clone();
            session.UpdatedAt = DateTimeOffset.UtcNow;

            if (root.TryGetProperty("id", out var idProp) && (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _)))
            {
                if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt32(out var requestId) && session.PendingRequests.TryRemove(requestId, out var tcs))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        tcs.TrySetException(new InvalidOperationException(error.GetRawText()));
                    }
                    else
                    {
                        tcs.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : default);
                    }
                }
                return;
            }

            if (root.TryGetProperty("method", out var methodProp))
            {
                var method = methodProp.GetString() ?? string.Empty;
                var parameters = root.TryGetProperty("params", out var paramsProp) ? paramsProp.Clone() : default;

                if (method == "session/request_permission")
                {
                    var requestId = Guid.NewGuid().ToString("N");
                    var rpcId = root.TryGetProperty("id", out var requestIdProp)
                        ? requestIdProp.Clone()
                        : JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("N"), Json);
                    session.PendingPermissionRequests[requestId] = rpcId;
                    Publish(session, "session.permission_request", new
                    {
                        request_id = requestId,
                        data = parameters
                    });
                    return;
                }

                if (method == "session/update")
                {
                    Publish(session, "session.update", parameters);
                    if (TryExtractEndTurn(parameters, out var payload))
                    {
                        Publish(session, "session.end_turn", payload);
                    }
                    return;
                }

                Publish(session, method, parameters);
            }
        }
    }

    private static bool TryExtractEndTurn(JsonElement payload, out object result)
    {
        result = new { };
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("update", out var update))
        {
            return false;
        }

        if (update.ValueKind == JsonValueKind.Object
            && update.TryGetProperty("type", out var typeProp)
            && string.Equals(typeProp.GetString(), "end_turn", StringComparison.OrdinalIgnoreCase))
        {
            result = new { update };
            return true;
        }

        return false;
    }

    private void Publish(AgentSessionState session, string eventType, object payload)
    {
        Publish(session, eventType, JsonSerializer.SerializeToElement(payload, Json));
    }

    private void Publish(AgentSessionState session, string eventType, JsonElement payload)
    {
        var envelope = new AgentGatewayEventEnvelope
        {
            GatewaySessionId = session.GatewaySessionId,
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload
        };
        lock (session.Events)
        {
            session.Events.Add(envelope);
            if (session.Events.Count > 200)
            {
                session.Events.RemoveAt(0);
            }
        }
        _ = _hubContext.Clients.Group(session.GatewaySessionId).SendAsync("AgentEvent", envelope);
    }

    private AgentSessionState GetRequiredSession(string gatewaySessionId)
    {
        if (!_sessions.TryGetValue(gatewaySessionId, out var session))
        {
            throw new InvalidOperationException($"agent session not found: {gatewaySessionId}");
        }

        return session;
    }

    private string ResolveWorkingDirectory(string? workingDirectory)
    {
        var basePath = Path.GetFullPath(_options.FilesBasePath);
        var requestedPath = string.IsNullOrWhiteSpace(workingDirectory)
            ? basePath
            : ResolveRequestedPath(basePath, workingDirectory.Trim());

        if (!IsPathWithinBasePath(requestedPath, basePath))
        {
            throw new UnauthorizedAccessException($"working directory is outside FILES_BASE_PATH: {requestedPath}");
        }

        Directory.CreateDirectory(requestedPath);
        return requestedPath;
    }

    private static string ResolveRequestedPath(string basePath, string workingDirectory)
    {
        if (Path.IsPathRooted(workingDirectory))
        {
            return Path.GetFullPath(workingDirectory);
        }

        return Path.GetFullPath(Path.Combine(basePath, workingDirectory));
    }

    private static bool IsPathWithinBasePath(string path, string basePath)
    {
        if (string.Equals(path, basePath, StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedBasePath = basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedBasePath, StringComparison.Ordinal);
    }

    private AgentSessionSummary ToSummary(AgentSessionState state) => new()
    {
        GatewaySessionId = state.GatewaySessionId,
        ConversationId = state.ConversationId,
        Backend = state.Backend,
        NodeId = _options.NodeId,
        Status = state.Status,
        SessionId = state.SessionId,
        WorkingDirectory = state.WorkingDirectory,
        CreatedAt = state.CreatedAt,
        UpdatedAt = state.UpdatedAt,
        PendingPermissionCount = state.PendingPermissionRequests.Count
    };

    private sealed class AgentSessionState
    {
        public required string GatewaySessionId { get; init; }
        public required string ConversationId { get; init; }
        public required string Backend { get; init; }
        public required string WorkingDirectory { get; init; }
        public required Process Process { get; init; }
        public StreamWriter? StandardInput { get; set; }
        public string? SessionId { get; set; }
        public string Status { get; set; } = "starting";
        public JsonElement LastSessionResponse { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public CancellationTokenSource Cts { get; } = new();
        public ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> PendingRequests { get; } = new();
        public ConcurrentDictionary<string, JsonElement> PendingPermissionRequests { get; } = new(StringComparer.Ordinal);
        public List<AgentGatewayEventEnvelope> Events { get; } = [];
    }
}
