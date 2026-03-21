using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Tests;

public class GatewayDisplayOwnershipTests
{
    [Fact]
    public async Task ObserverResize_ShouldBeRejected_WithoutChangingGeometry()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var instanceId = await CreateInstanceAsync(client);
        await using var ownerHub = BuildHubConnection(client);
        await using var observerHub = BuildHubConnection(client);
        var ownerMessages = new List<JsonElement>();
        var observerMessages = new List<JsonElement>();
        var ownerGate = new object();
        var observerGate = new object();

        ownerHub.On<JsonElement>("TerminalEvent", msg =>
        {
            lock (ownerGate)
            {
                ownerMessages.Add(msg.Clone());
            }
        });
        observerHub.On<JsonElement>("TerminalEvent", msg =>
        {
            lock (observerGate)
            {
                observerMessages.Add(msg.Clone());
            }
        });

        await ownerHub.StartAsync();
        await observerHub.StartAsync();
        await ownerHub.InvokeAsync("JoinInstance", new { instanceId });
        var initialSnapshot = await WaitForMessageAsync(ownerMessages, ownerGate, msg => GetType(msg) == "term.snapshot", TimeSpan.FromSeconds(8));

        await observerHub.InvokeAsync("JoinInstance", new { instanceId });
        _ = await WaitForMessageAsync(observerMessages, observerGate, msg => GetType(msg) == "term.snapshot", TimeSpan.FromSeconds(8));

        await observerHub.InvokeAsync("RequestResize", new { instanceId, cols = 100, rows = 30, reqId = "observer-reject" });
        var ack = await WaitForMessageAsync(observerMessages, observerGate, msg => GetType(msg) == "term.resize.ack" && msg.GetProperty("req_id").GetString() == "observer-reject", TimeSpan.FromSeconds(8));

        Assert.False(ack.GetProperty("accepted").GetBoolean());
        Assert.Equal("not_owner", ack.GetProperty("reason").GetString());
        Assert.Equal(initialSnapshot.GetProperty("size").GetProperty("cols").GetInt32(), ack.GetProperty("size").GetProperty("cols").GetInt32());
        Assert.Equal(initialSnapshot.GetProperty("size").GetProperty("rows").GetInt32(), ack.GetProperty("size").GetProperty("rows").GetInt32());
    }

    [Fact]
    public async Task OwnerResize_ShouldPublishNewRenderEpochSnapshot()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var instanceId = await CreateInstanceAsync(client);
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
        var initialSnapshot = await WaitForMessageAsync(messages, gate, msg => GetType(msg) == "term.snapshot", TimeSpan.FromSeconds(8));
        var initialRenderEpoch = initialSnapshot.GetProperty("render_epoch").GetInt32();

        await hub.InvokeAsync("RequestResize", new { instanceId, cols = 110, rows = 35, reqId = "owner-resize" });
        var ack = await WaitForMessageAsync(messages, gate, msg => GetType(msg) == "term.resize.ack" && msg.GetProperty("req_id").GetString() == "owner-resize", TimeSpan.FromSeconds(8));
        var resizedSnapshot = await WaitForMessageAsync(messages, gate, msg =>
            GetType(msg) == "term.snapshot"
            && msg.GetProperty("render_epoch").GetInt32() > initialRenderEpoch
            && msg.GetProperty("size").GetProperty("cols").GetInt32() == 110
            && msg.GetProperty("size").GetProperty("rows").GetInt32() == 35,
            TimeSpan.FromSeconds(8));

        Assert.True(ack.GetProperty("accepted").GetBoolean());
        Assert.Equal(110, resizedSnapshot.GetProperty("size").GetProperty("cols").GetInt32());
        Assert.Equal(35, resizedSnapshot.GetProperty("size").GetProperty("rows").GetInt32());
        Assert.True(resizedSnapshot.GetProperty("render_epoch").GetInt32() > initialRenderEpoch);
    }

    [Fact]
    public async Task LeaveAll_ShouldReassignDisplayOwner_OnLegacyHub()
    {
        await LeaveAll_ShouldReassignDisplayOwnerAsync("/hubs/terminal");
    }

    [Fact]
    public async Task LeaveAll_ShouldReassignDisplayOwner_OnV2Hub()
    {
        await LeaveAll_ShouldReassignDisplayOwnerAsync("/hubs/terminal-v2");
    }

    private static async Task<string> CreateInstanceAsync(HttpClient client)
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
        return created.GetProperty("instance_id").GetString()!;
    }

    private static HubConnection BuildHubConnection(HttpClient client)
    {
        return BuildHubConnection(client, "/hubs/terminal-v2");
    }

    private static HubConnection BuildHubConnection(HttpClient client, string hubPath)
    {
        var baseAddress = client.BaseAddress ?? throw new InvalidOperationException("missing base address");
        var target = new Uri(baseAddress, hubPath);
        return new HubConnectionBuilder().WithUrl(target).Build();
    }

    private static async Task LeaveAll_ShouldReassignDisplayOwnerAsync(string hubPath)
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();
        var manager = app.Services.GetRequiredService<InstanceManager>();

        var instanceId = await CreateInstanceAsync(client);
        await using var ownerHub = BuildHubConnection(client, hubPath);
        await using var observerHub = BuildHubConnection(client, hubPath);
        var observerMessages = new List<JsonElement>();
        var observerGate = new object();

        observerHub.On<JsonElement>("TerminalEvent", msg =>
        {
            lock (observerGate)
            {
                observerMessages.Add(msg.Clone());
            }
        });

        await ownerHub.StartAsync();
        await observerHub.StartAsync();
        await ownerHub.InvokeAsync("JoinInstance", new { instanceId });
        await observerHub.InvokeAsync("JoinInstance", new { instanceId });
        var snapshot = await WaitForMessageAsync(observerMessages, observerGate, msg => GetType(msg)?.Contains("snapshot", StringComparison.Ordinal) == true, TimeSpan.FromSeconds(8));
        var initialOwnerConnectionId = snapshot.GetProperty("owner_connection_id").GetString();

        await ownerHub.InvokeAsync("LeaveInstance", new { });
        var reassignedOwnerConnectionId = await WaitForConditionAsync(() =>
        {
            var current = manager.GetDisplayOwner(instanceId);
            return !string.IsNullOrWhiteSpace(current) && !string.Equals(current, initialOwnerConnectionId, StringComparison.Ordinal)
                ? current
                : null;
        }, TimeSpan.FromSeconds(8));

        Assert.False(string.IsNullOrWhiteSpace(initialOwnerConnectionId));
        Assert.False(string.IsNullOrWhiteSpace(reassignedOwnerConnectionId));
        Assert.NotEqual(initialOwnerConnectionId, reassignedOwnerConnectionId);
    }

    private static string? GetType(JsonElement msg)
    {
        var type = msg.TryGetProperty("type", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return type switch
        {
            "term.v2.snapshot" => "term.snapshot",
            "term.v2.raw" => "term.raw",
            "term.v2.resize.ack" => "term.resize.ack",
            "term.v2.sync.complete" => "term.sync.complete",
            "term.v2.sync.required" => "term.sync.required",
            "term.v2.owner.changed" => "term.owner.changed",
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

    private static async Task<T> WaitForConditionAsync<T>(Func<T?> probe, TimeSpan timeout) where T : class
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            var result = probe();
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("timed out waiting condition");
    }
}
