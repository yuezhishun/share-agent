using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using TerminalGateway.Api.Tests.Oracle;

namespace TerminalGateway.Api.Tests;

public class SnapshotOracleConsistencyTests
{
    [Fact]
    [Trait("Category", "oracle")]
    public async Task RequestSyncSnapshot_ShouldConvergeWithOracle()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var instanceId = await CreateCatInstanceAsync(client);
        await using var hub = BuildHubConnection(client);

        var messages = new List<JsonElement>();
        var gate = new object();
        hub.On<JsonElement>("TerminalEvent", msg =>
        {
            lock (gate)
            {
                messages.Add(msg.Clone());
            }
        });

        await hub.StartAsync();
        await hub.InvokeAsync("JoinInstance", new { instanceId });
        _ = await WaitForMessageAsync(messages, gate, msg => GetType(msg) == "term.snapshot", TimeSpan.FromSeconds(8));

        const string input = "oracle-sync-line\n";
        await hub.InvokeAsync("SendInput", new { instanceId, data = input });
        var liveRaw = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.raw"
                && JsonSerializer.Serialize(msg).Contains("oracle-sync-line", StringComparison.Ordinal),
            TimeSpan.FromSeconds(8));

        await hub.InvokeAsync("RequestSync", new { instanceId, type = "screen" });
        var snapshot = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.snapshot" && msg.GetProperty("ts").GetInt64() > liveRaw.GetProperty("ts").GetInt64(),
            TimeSpan.FromSeconds(8));

        using var oracle = new XTermOracleAdapter(80, 25);
        oracle.Feed(input);
        var expected = TerminalFrameNormalizer.FromOracle(oracle.Export());
        var actual = TerminalFrameNormalizer.FromSnapshot(snapshot);
        TerminalOracleAssert.EqualLoose(expected, actual);
    }

    [Fact]
    [Trait("Category", "oracle")]
    public async Task ResizeAckAndSnapshot_ShouldMatchOracleSize()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var instanceId = await CreateCatInstanceAsync(client);
        await using var hub = BuildHubConnection(client);

        var messages = new List<JsonElement>();
        var gate = new object();
        hub.On<JsonElement>("TerminalEvent", msg =>
        {
            lock (gate)
            {
                messages.Add(msg.Clone());
            }
        });

        await hub.StartAsync();
        await hub.InvokeAsync("JoinInstance", new { instanceId });
        _ = await WaitForMessageAsync(messages, gate, msg => GetType(msg) == "term.snapshot", TimeSpan.FromSeconds(8));

        await hub.InvokeAsync("RequestResize", new { instanceId, cols = 100, rows = 30, reqId = "oracle-resize" });
        _ = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.resize.ack" && GetString(msg, "req_id") == "oracle-resize",
            TimeSpan.FromSeconds(8));

        const string input = "resize-check\n";
        await hub.InvokeAsync("SendInput", new { instanceId, data = input });
        var liveRaw = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.raw" && JsonSerializer.Serialize(msg).Contains("resize-check", StringComparison.Ordinal),
            TimeSpan.FromSeconds(8));

        await hub.InvokeAsync("RequestSync", new { instanceId, type = "screen" });
        var snapshot = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.snapshot"
                && msg.GetProperty("size").GetProperty("cols").GetInt32() == 100
                && msg.GetProperty("ts").GetInt64() > liveRaw.GetProperty("ts").GetInt64(),
            TimeSpan.FromSeconds(8));

        using var oracle = new XTermOracleAdapter(80, 25);
        oracle.Resize(100, 30);
        oracle.Feed(input);

        var expected = TerminalFrameNormalizer.FromOracle(oracle.Export());
        var actual = TerminalFrameNormalizer.FromSnapshot(snapshot);
        TerminalOracleAssert.EqualLoose(expected, actual);
    }

    private static async Task<string> CreateCatInstanceAsync(HttpClient client)
    {
        var createRes = await client.PostAsJsonAsync("/api/instances", new
        {
            command = "/bin/cat",
            cols = 80,
            rows = 25,
            cwd = "/home/yueyuan"
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var instanceId = created.GetProperty("instance_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(instanceId));
        return instanceId!;
    }

    private static HubConnection BuildHubConnection(HttpClient client)
    {
        var baseAddress = client.BaseAddress ?? throw new InvalidOperationException("missing base address");
        var target = new Uri(baseAddress, "/hubs/terminal");
        return new HubConnectionBuilder().WithUrl(target).Build();
    }

    private static string? GetType(JsonElement msg) => GetString(msg, "type");

    private static string? GetString(JsonElement msg, string name)
    {
        return msg.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static async Task<JsonElement> WaitForMessageAsync(List<JsonElement> messages, object gate, Func<JsonElement, bool> predicate, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            lock (gate)
            {
                foreach (var msg in messages)
                {
                    if (predicate(msg))
                    {
                        return msg;
                    }
                }
            }

            await Task.Delay(50);
        }

        lock (gate)
        {
            var summary = string.Join(", ", messages.Select(msg => GetType(msg) ?? "<unknown>"));
            throw new TimeoutException($"timed out waiting signalr frame; received: [{summary}]");
        }
    }
}
