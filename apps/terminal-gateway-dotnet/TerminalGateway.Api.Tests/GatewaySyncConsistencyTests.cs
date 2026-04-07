using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace TerminalGateway.Api.Tests;

public class GatewaySyncConsistencyTests
{
    [Fact]
    public async Task RequestSyncScreen_ShouldNotDriftSeq_WhenNoNewOutput()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/api/instances", new
        {
            command = "/bin/cat",
            cols = 80,
            rows = 25,
            cwd = TestPaths.DefaultCwd
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var instanceId = created.GetProperty("instance_id").GetString()!;

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
        var snapshot1 = await WaitForMessageAsync(messages, gate, msg => GetType(msg) == "term.snapshot", TimeSpan.FromSeconds(8));
        var firstSnapshotTs = snapshot1.GetProperty("ts").GetInt64();

        await hub.InvokeAsync("RequestSync", new { instanceId, type = "screen" });
        var snapshot2 = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.snapshot" && msg.GetProperty("ts").GetInt64() > firstSnapshotTs,
            TimeSpan.FromSeconds(8));

        Assert.Equal(snapshot1.GetProperty("seq").GetInt32(), snapshot2.GetProperty("seq").GetInt32());
    }

    private static HubConnection BuildHubConnection(HttpClient client)
    {
        var baseAddress = client.BaseAddress ?? throw new InvalidOperationException("missing base address");
        var target = new Uri(baseAddress, "/hubs/terminal");
        return new HubConnectionBuilder().WithUrl(target).Build();
    }

    private static string? GetType(JsonElement msg)
    {
        var type = msg.TryGetProperty("type", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return type switch
        {
            "term.snapshot" => "term.snapshot",
            "term.raw" => "term.raw",
            "term.resize.ack" => "term.resize.ack",
            "term.sync.complete" => "term.sync.complete",
            "term.sync.required" => "term.sync.required",
            "term.owner.changed" => "term.owner.changed",
            _ => type
        };
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

        throw new TimeoutException("timed out waiting signalr frame");
    }
}
