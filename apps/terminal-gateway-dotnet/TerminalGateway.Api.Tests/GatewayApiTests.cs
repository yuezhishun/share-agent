using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TerminalGateway.Api.Tests;

public class GatewayApiTests
{
    [Fact]
    public async Task Gateway_Spawn_And_WsOutput_Works()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid().ToString();
        var createRes = await client.PostAsJsonAsync("/internal/sessions", new
        {
            sessionId,
            taskId = Guid.NewGuid().ToString(),
            cliType = "custom",
            mode = "execute",
            shell = "/bin/bash",
            cwd = "/tmp",
            command = "echo hello-gateway-dotnet; sleep 2",
            cols = 120,
            rows = 30
        }, headers: app.InternalHeaders);

        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

        using var ws = await ConnectWebSocketAsync(client, $"/ws/terminal?sessionId={sessionId}&replay=1");
        var messages = await ReceiveUntilAsync(ws, msg => msg.TryGetProperty("type", out var t) && t.GetString() == "output" && msg.GetProperty("data").GetString()!.Contains("hello-gateway-dotnet"), TimeSpan.FromSeconds(8));
        Assert.Contains(messages, x => x.GetProperty("type").GetString() == "ready");
        Assert.Contains(messages, x => x.GetProperty("type").GetString() == "output");
    }

    [Fact]
    public async Task Gateway_PingPong_Works()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid().ToString();
        var createRes = await client.PostAsJsonAsync("/internal/sessions", new
        {
            sessionId,
            taskId = Guid.NewGuid().ToString(),
            shell = "/bin/bash",
            cwd = "/tmp",
            command = ""
        }, headers: app.InternalHeaders);
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

        using var ws = await ConnectWebSocketAsync(client, $"/ws/terminal?sessionId={sessionId}");
        await WaitForReadyAsync(ws, TimeSpan.FromSeconds(8));
        await SendWsAsync(ws, new { type = "ping", ts = 123L });
        var messages = await ReceiveUntilAsync(ws, msg => msg.TryGetProperty("type", out var t) && t.GetString() == "pong", TimeSpan.FromSeconds(8));
        var pong = Assert.Single(messages, x => x.GetProperty("type").GetString() == "pong");
        Assert.Equal(123L, pong.GetProperty("ts").GetInt64());
    }

    [Fact]
    public async Task PublicCreate_ReturnsWriteToken_ButListDoesNot()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/sessions", new { shell = "/bin/bash", cwd = "/tmp", command = "sleep 2" });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var sessionId = created.GetProperty("sessionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(created.GetProperty("writeToken").GetString()));

        var list = await client.GetFromJsonAsync<JsonElement>("/sessions?includeExited=true");
        var found = list.EnumerateArray().First(x => x.GetProperty("sessionId").GetString() == sessionId);
        Assert.False(found.TryGetProperty("writeToken", out _));
    }

    [Fact]
    public async Task WriteToken_ControlsWritableAccess()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/sessions", new { shell = "/bin/bash", cwd = "/tmp", command = "" });
        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var sessionId = created.GetProperty("sessionId").GetString()!;
        var writeToken = created.GetProperty("writeToken").GetString()!;

        using var roWs = await ConnectWebSocketAsync(client, $"/ws/terminal?sessionId={sessionId}");
        var roReady = await WaitForReadyAsync(roWs, TimeSpan.FromSeconds(8));
        Assert.False(roReady.GetProperty("writable").GetBoolean());
        await SendWsAsync(roWs, new { type = "input", data = "echo denied\r" });
        var roMessages = await ReceiveUntilAsync(roWs, msg => msg.TryGetProperty("code", out var c) && c.GetString() == "READ_ONLY", TimeSpan.FromSeconds(8));
        Assert.Contains(roMessages, x => x.GetProperty("type").GetString() == "error");

        using var rwWs = await ConnectWebSocketAsync(client, $"/ws/terminal?sessionId={sessionId}&writeToken={WebUtility.UrlEncode(writeToken)}");
        var rwReady = await WaitForReadyAsync(rwWs, TimeSpan.FromSeconds(8));
        Assert.True(rwReady.GetProperty("writable").GetBoolean());
        await SendWsAsync(rwWs, new { type = "input", data = "echo writable-ok\r" });
        var rwMessages = await ReceiveUntilAsync(rwWs, msg => msg.TryGetProperty("type", out var t) && t.GetString() == "output" && msg.GetProperty("data").GetString()!.Contains("writable-ok"), TimeSpan.FromSeconds(8));
        Assert.Contains(rwMessages, x => x.GetProperty("type").GetString() == "output");
    }

    [Fact]
    public async Task Snapshot_And_History_Work()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/sessions", new
        {
            shell = "/bin/bash",
            cwd = "/tmp",
            command = "echo one; echo two; echo three"
        });
        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var sessionId = created.GetProperty("sessionId").GetString()!;

        await Task.Delay(300);

        var snapshotRes = await client.GetAsync($"/sessions/{sessionId}/snapshot?limitBytes=4096");
        Assert.Equal(HttpStatusCode.OK, snapshotRes.StatusCode);
        var snapshot = JsonDocument.Parse(await snapshotRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains("three", snapshot.GetProperty("data").GetString());

        var tailSeq = snapshot.GetProperty("tailSeq").GetInt32();
        var historyRes = await client.GetAsync($"/sessions/{sessionId}/history?beforeSeq={tailSeq}&limitBytes=64");
        Assert.Equal(HttpStatusCode.OK, historyRes.StatusCode);
        var history = JsonDocument.Parse(await historyRes.Content.ReadAsStringAsync()).RootElement;
        Assert.True(history.GetProperty("chunks").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ProfilesCrud_Works()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/profiles", new
        {
            name = "custom-tools",
            shell = "/bin/bash",
            cwd = "/tmp",
            startupCommands = new[] { "pwd" }
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var profileId = created.GetProperty("profileId").GetString()!;

        var updateRes = await client.PutAsJsonAsync($"/profiles/{profileId}", new { cwd = "/var/tmp", startupCommands = new[] { "pwd", "ls" } });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        var deleteRes = await client.DeleteAsync($"/profiles/{profileId}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);
    }

    [Fact]
    public async Task FsAndProjects_Endpoints_Work()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-dotnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "alpha"));
        Directory.CreateDirectory(Path.Combine(tempDir, "beta", "child"));
        var codexConfig = Path.Combine(tempDir, "config.toml");
        await File.WriteAllTextAsync(codexConfig, "[projects.\"/workspace/demo-a\"]\ntrust_level=\"trusted\"\n");

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["TERMINAL_FS_ALLOWED_ROOTS"] = tempDir,
            ["TERMINAL_CODEX_CONFIG_PATH"] = codexConfig
        });
        using var client = app.CreateClient();

        var fsRes = await client.GetAsync($"/fs/dirs?path={WebUtility.UrlEncode(tempDir)}");
        Assert.Equal(HttpStatusCode.OK, fsRes.StatusCode);

        var projectsRes = await client.GetAsync("/projects/discover");
        Assert.Equal(HttpStatusCode.OK, projectsRes.StatusCode);
        var projects = JsonDocument.Parse(await projectsRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(projects.GetProperty("items").EnumerateArray(), x => x.GetProperty("path").GetString() == "/workspace/demo-a");
    }

    [Fact]
    public async Task InternalEndpoints_RequireToken()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var res = await client.PostAsJsonAsync("/internal/sessions", new { shell = "/bin/bash", cwd = "/tmp", command = "echo hi" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    private static async Task SendWsAsync(WebSocket socket, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonElement> WaitForReadyAsync(WebSocket socket, TimeSpan timeout)
    {
        var messages = await ReceiveUntilAsync(socket, msg => msg.TryGetProperty("type", out var t) && t.GetString() == "ready", timeout);
        return messages.First(x => x.GetProperty("type").GetString() == "ready");
    }

    private static async Task<ClientWebSocket> ConnectWebSocketAsync(HttpClient client, string pathAndQuery, CancellationToken cancellationToken = default)
    {
        var baseAddress = client.BaseAddress ?? throw new InvalidOperationException("missing base address");
        var target = BuildWsUri(baseAddress, pathAndQuery);
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(target, cancellationToken);
        return socket;
    }

    private static Uri BuildWsUri(Uri baseAddress, string pathAndQuery)
    {
        var queryPath = pathAndQuery.StartsWith("/", StringComparison.Ordinal) ? pathAndQuery : "/" + pathAndQuery;
        var builder = new UriBuilder(baseAddress)
        {
            Scheme = baseAddress.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = string.Empty,
            Query = string.Empty
        };
        return new Uri(builder.Uri, queryPath);
    }

    private static async Task<List<JsonElement>> ReceiveUntilAsync(WebSocket socket, Func<JsonElement, bool> predicate, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        List<JsonElement> messages = [];
        var buffer = new byte[16 * 1024];

        while (!timeoutCts.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeoutCts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return messages;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (ms.Length == 0)
            {
                continue;
            }

            JsonElement msg;
            try
            {
                msg = JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            messages.Add(msg);
            if (predicate(msg))
            {
                return messages;
            }
        }

        var messageSummary = string.Join(", ", messages.Select(static msg => msg.TryGetProperty("type", out var t) ? t.GetString() : "<unknown>"));
        throw new TimeoutException($"timed out waiting websocket frame; received: [{messageSummary}]");
    }
}

internal sealed class GatewayFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public GatewayFactory(IReadOnlyDictionary<string, string?>? settings = null)
    {
        _settings = settings ?? new Dictionary<string, string?>();
        UseKestrel(0);
    }

    public Dictionary<string, string> InternalHeaders => new(StringComparer.Ordinal)
    {
        ["X-Internal-Token"] = "it-token"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("TERMINAL_GATEWAY_TOKEN", "it-token");
        builder.UseSetting("TERMINAL_WS_TOKEN", "it-ws-token");
        builder.UseSetting("PORT", "0");
        builder.UseSetting("HOST", "127.0.0.1");
        builder.UseSetting("TERMINAL_SETTINGS_STORE_FILE", Path.Combine(Path.GetTempPath(), $"tg-settings-{Guid.NewGuid():N}.json"));

        foreach (var kv in _settings)
        {
            builder.UseSetting(kv.Key, kv.Value);
        }
    }
}

internal static class HttpClientExtensions
{
    public static async Task<HttpResponseMessage> PostAsJsonAsync(this HttpClient client, string requestUri, object value, Dictionary<string, string>? headers)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(value)
        };

        if (headers is not null)
        {
            foreach (var kv in headers)
            {
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
        }

        return await client.SendAsync(req);
    }
}
