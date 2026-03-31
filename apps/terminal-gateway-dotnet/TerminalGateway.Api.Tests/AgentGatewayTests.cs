using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TerminalGateway.Api.Tests;

public sealed class AgentGatewayTests
{
    [Fact]
    public async Task Custom_Agent_Session_Can_Stream_And_Handle_Permission_Request()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var repoRoot = FindRepositoryRoot();
        var fixturePath = Path.Combine(repoRoot, "apps", "terminal-gateway-dotnet", "TerminalGateway.Api.Tests", "Fixtures", "fake-acp-agent.py");

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var connectResponse = await client.PostAsJsonAsync("/api/agent-sessions", new
        {
            backend = "custom",
            cli_path = fixturePath,
            working_directory = tempDir
        });

        Assert.Equal(HttpStatusCode.OK, connectResponse.StatusCode);
        var connectPayload = JsonDocument.Parse(await connectResponse.Content.ReadAsStringAsync()).RootElement;
        var session = connectPayload.GetProperty("session");
        var gatewaySessionId = session.GetProperty("gateway_session_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(gatewaySessionId));

        var promptResponse = await client.PostAsJsonAsync($"/api/agent-sessions/{gatewaySessionId}/prompt", new { text = "hello" });
        Assert.Equal(HttpStatusCode.OK, promptResponse.StatusCode);

        var update = await WaitForAgentEventAsync(client, gatewaySessionId!, msg =>
            msg.GetProperty("event_type").GetString() == "session.update"
            && msg.GetProperty("payload").GetProperty("update").GetProperty("content").GetProperty("text").GetString() == "echo:hello");
        Assert.Equal("session.update", update.GetProperty("event_type").GetString());

        var permissionResponse = await client.PostAsJsonAsync($"/api/agent-sessions/{gatewaySessionId}/prompt", new { text = "permission please" });
        Assert.Equal(HttpStatusCode.OK, permissionResponse.StatusCode);

        var permission = await WaitForAgentEventAsync(client, gatewaySessionId!, msg => msg.GetProperty("event_type").GetString() == "session.permission_request");
        var requestId = permission.GetProperty("payload").GetProperty("request_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(requestId));

        var decisionResponse = await client.PostAsJsonAsync($"/api/agent-sessions/{gatewaySessionId}/permission", new
        {
            requestId,
            optionId = "allow"
        });
        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);

        var endTurn = await WaitForAgentEventAsync(client, gatewaySessionId!, msg => msg.GetProperty("event_type").GetString() == "session.end_turn");
        Assert.Equal("session.end_turn", endTurn.GetProperty("event_type").GetString());
    }

    [Fact]
    public async Task Connect_Should_Reject_Working_Directory_With_Shared_Base_Prefix()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var siblingDir = $"{tempDir}-other";
        Directory.CreateDirectory(siblingDir);
        var repoRoot = FindRepositoryRoot();
        var fixturePath = Path.Combine(repoRoot, "apps", "terminal-gateway-dotnet", "TerminalGateway.Api.Tests", "Fixtures", "fake-acp-agent.py");

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var connectResponse = await client.PostAsJsonAsync("/api/agent-sessions", new
        {
            backend = "custom",
            cli_path = fixturePath,
            working_directory = siblingDir
        });

        Assert.Equal(HttpStatusCode.Forbidden, connectResponse.StatusCode);
    }

    [Fact]
    public async Task Connect_Should_Reject_Working_Directory_That_Escapes_Base_Path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var repoRoot = FindRepositoryRoot();
        var fixturePath = Path.Combine(repoRoot, "apps", "terminal-gateway-dotnet", "TerminalGateway.Api.Tests", "Fixtures", "fake-acp-agent.py");

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var connectResponse = await client.PostAsJsonAsync("/api/agent-sessions", new
        {
            backend = "custom",
            cli_path = fixturePath,
            working_directory = "../escape"
        });

        Assert.Equal(HttpStatusCode.Forbidden, connectResponse.StatusCode);
    }

    [Fact]
    public async Task Permission_Response_Should_Preserve_String_JsonRpc_Id()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var repoRoot = FindRepositoryRoot();
        var fixturePath = Path.Combine(repoRoot, "apps", "terminal-gateway-dotnet", "TerminalGateway.Api.Tests", "Fixtures", "fake-acp-agent.py");

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var connectResponse = await client.PostAsJsonAsync("/api/agent-sessions", new
        {
            backend = "custom",
            cli_path = fixturePath,
            working_directory = tempDir
        });

        Assert.Equal(HttpStatusCode.OK, connectResponse.StatusCode);
        var connectPayload = JsonDocument.Parse(await connectResponse.Content.ReadAsStringAsync()).RootElement;
        var gatewaySessionId = connectPayload.GetProperty("session").GetProperty("gateway_session_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(gatewaySessionId));

        var permissionResponse = await client.PostAsJsonAsync($"/api/agent-sessions/{gatewaySessionId}/prompt", new { text = "string permission please" });
        Assert.Equal(HttpStatusCode.OK, permissionResponse.StatusCode);

        var permission = await WaitForAgentEventAsync(client, gatewaySessionId!, msg => msg.GetProperty("event_type").GetString() == "session.permission_request");
        var requestId = permission.GetProperty("payload").GetProperty("request_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(requestId));

        var decisionResponse = await client.PostAsJsonAsync($"/api/agent-sessions/{gatewaySessionId}/permission", new
        {
            requestId,
            optionId = "allow"
        });
        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);

        var update = await WaitForAgentEventAsync(client, gatewaySessionId!, msg =>
            msg.GetProperty("event_type").GetString() == "session.update"
            && msg.GetProperty("payload").GetProperty("update").GetProperty("content").GetProperty("text").GetString() == "permission-ok");
        Assert.Equal("session.update", update.GetProperty("event_type").GetString());
    }

    private static async Task<JsonElement> WaitForAgentEventAsync(
        HttpClient client,
        string gatewaySessionId,
        Func<JsonElement, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            using var response = await client.GetAsync($"/api/agent-sessions/{gatewaySessionId}/events");
            response.EnsureSuccessStatusCode();
            var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            var items = payload.GetProperty("items").EnumerateArray().ToList();
            foreach (var item in items)
            {
                try
                {
                    if (predicate(item))
                    {
                        return item;
                    }
                }
                catch
                {
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("timed out waiting for agent event");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "apps", "terminal-gateway-dotnet")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("failed to locate repository root");
    }
}
