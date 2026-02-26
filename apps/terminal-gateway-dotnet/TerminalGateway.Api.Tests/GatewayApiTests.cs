using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Tests;

public class GatewayApiTests
{
    [Fact]
    public async Task Health_And_Projects_Endpoints_Work()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-dotnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "alpha"));

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var healthRes = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);

        var projectsRes = await client.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.OK, projectsRes.StatusCode);
        var projects = JsonDocument.Parse(await projectsRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(projects.GetProperty("items").EnumerateArray(), x => x.GetProperty("name").GetString() == "alpha");
    }

    [Fact]
    public async Task Create_Instance_And_SignalR_IO_Work()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/api/instances", new
        {
            command = "bash",
            args = new[] { "-i" },
            cols = 80,
            rows = 25,
            cwd = "/home/yueyuan"
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var instanceId = created.GetProperty("instance_id").GetString()!;
        Assert.True(created.TryGetProperty("hub_url", out _));

        await using var hub = BuildHubConnection(client);
        List<JsonElement> messages = [];
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
        var firstSnapshot = messages.First(msg => GetType(msg) == "term.snapshot");
        Assert.True(firstSnapshot.TryGetProperty("node_id", out _));
        Assert.True(firstSnapshot.TryGetProperty("node_name", out _));

        await hub.InvokeAsync("SendInput", new { instanceId, data = "echo hello-webcli-dotnet\r" });
        _ = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.patch" && JsonSerializer.Serialize(msg).Contains("hello-webcli-dotnet", StringComparison.Ordinal),
            TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task SignalR_Resize_And_Sync_Work()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/api/instances", new
        {
            command = "bash",
            args = new[] { "-i" },
            cols = 80,
            rows = 25,
            cwd = "/home/yueyuan"
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var instanceId = created.GetProperty("instance_id").GetString()!;

        await using var hub = BuildHubConnection(client);
        List<JsonElement> messages = [];
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

        await hub.InvokeAsync("RequestResize", new { instanceId, cols = 100, rows = 30, reqId = "resize-test" });

        var ack = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.resize.ack" && GetString(msg, "req_id") == "resize-test",
            TimeSpan.FromSeconds(8));
        Assert.Equal(100, ack.GetProperty("size").GetProperty("cols").GetInt32());
        Assert.Equal(30, ack.GetProperty("size").GetProperty("rows").GetInt32());

        var resizedSnapshot = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.snapshot" && msg.TryGetProperty("size", out var size)
                && size.GetProperty("cols").GetInt32() == 100
                && size.GetProperty("rows").GetInt32() == 30,
            TimeSpan.FromSeconds(8));
        Assert.Equal("term.snapshot", GetType(resizedSnapshot));

        await hub.InvokeAsync("RequestSync", new { instanceId, type = "history", reqId = "history-test", before = "h-1", limit = 20 });
        var history = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.history.chunk" && GetString(msg, "req_id") == "history-test",
            TimeSpan.FromSeconds(8));
        Assert.Equal("term.history.chunk", GetType(history));
    }

    [Fact]
    public async Task Files_Read_Endpoint_Works()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var file = Path.Combine(tempDir, "a.txt");
        await File.WriteAllTextAsync(file, "line1\nline2\nline3\n");

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var listRes = await client.GetAsync($"/api/files/list?path={Uri.EscapeDataString(tempDir)}");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        var readRes = await client.GetAsync($"/api/files/read?path={Uri.EscapeDataString(file)}&max_lines=2");
        Assert.Equal(HttpStatusCode.OK, readRes.StatusCode);
        var payload = JsonDocument.Parse(await readRes.Content.ReadAsStringAsync()).RootElement;
        Assert.True(payload.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task Exited_Instance_Should_Be_Removed()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/api/instances", new
        {
            command = "/bin/bash -lc \"echo bye-webcli-dotnet\"",
            cols = 80,
            rows = 25,
            cwd = "/home/yueyuan"
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var instanceId = created.GetProperty("instance_id").GetString()!;

        await Task.Delay(6200);

        var list = JsonDocument.Parse(await client.GetStringAsync("/api/instances")).RootElement;
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetString() == instanceId);
    }

    [Fact]
    public async Task Nodes_Endpoint_Should_Return_Master_Node()
    {
        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["NODE_ID"] = "master-a",
            ["NODE_NAME"] = "Master A"
        });
        using var client = app.CreateClient();

        var response = await client.GetAsync("/api/nodes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var items = payload.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, x =>
            x.GetProperty("node_id").GetString() == "master-a" &&
            x.GetProperty("node_name").GetString() == "Master A" &&
            x.GetProperty("node_role").GetString() == "master" &&
            x.GetProperty("node_online").GetBoolean());
    }

    [Fact]
    public async Task ClusterHub_Register_And_Heartbeat_Should_Appear_In_Nodes()
    {
        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-test-token",
            ["NODE_ID"] = "master-1",
            ["NODE_NAME"] = "Master 1"
        });
        using var client = app.CreateClient();

        await using var clusterHub = BuildClusterHubConnection(client);
        await clusterHub.StartAsync();

        await clusterHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-test-token",
            nodeId = "slave-1",
            nodeName = "Slave 1",
            nodeLabel = "region-a",
            instanceCount = 2
        });
        await clusterHub.InvokeAsync("Heartbeat", new
        {
            token = "cluster-test-token",
            nodeId = "slave-1",
            instanceCount = 3
        });

        var response = await client.GetAsync("/api/nodes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var items = payload.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, x =>
            x.GetProperty("node_id").GetString() == "slave-1" &&
            x.GetProperty("node_name").GetString() == "Slave 1" &&
            x.GetProperty("node_role").GetString() == "slave" &&
            x.GetProperty("node_online").GetBoolean() &&
            x.GetProperty("instance_count").GetInt32() == 3);
    }

    [Fact]
    public async Task Cluster_Node_Should_Be_Offline_After_Heartbeat_Timeout()
    {
        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-timeout-token",
            ["NODE_ID"] = "master-timeout",
            ["NODE_NAME"] = "Master Timeout",
            ["NODE_HEARTBEAT_TIMEOUT_SECONDS"] = "1"
        });
        using var client = app.CreateClient();

        await using var clusterHub = BuildClusterHubConnection(client);
        await clusterHub.StartAsync();
        await clusterHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-timeout-token",
            nodeId = "slave-timeout",
            nodeName = "Slave Timeout",
            instanceCount = 1
        });

        await Task.Delay(6200);

        var response = await client.GetAsync("/api/nodes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var items = payload.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, x =>
            x.GetProperty("node_id").GetString() == "slave-timeout" &&
            !x.GetProperty("node_online").GetBoolean());
    }

    [Fact]
    public async Task Master_Node_Proxy_APIs_Should_Route_To_Slave_Through_ClusterHub()
    {
        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-test-token",
            ["NODE_ID"] = "master-1",
            ["NODE_NAME"] = "Master 1"
        });
        using var client = app.CreateClient();

        await using var slaveHub = BuildClusterHubConnection(client);
        var slaveInstances = new HashSet<string>(StringComparer.Ordinal);

        slaveHub.On<ClusterCommandEnvelope>("ClusterCommand", async command =>
        {
            switch (command.Type)
            {
                case "instance.create":
                {
                    var instanceId = $"slave-inst-{Guid.NewGuid():N}";
                    lock (slaveInstances)
                    {
                        slaveInstances.Add(instanceId);
                    }

                    await slaveHub.InvokeAsync("SubmitCommandResult", new
                    {
                        commandId = command.CommandId,
                        nodeId = command.NodeId,
                        ok = true,
                        payload = new { instance_id = instanceId }
                    });
                    break;
                }
                case "instance.input":
                {
                    var instanceId = command.Payload.GetProperty("instance_id").GetString() ?? string.Empty;
                    var exists = false;
                    lock (slaveInstances)
                    {
                        exists = slaveInstances.Contains(instanceId);
                    }

                    await slaveHub.InvokeAsync("SubmitCommandResult", new
                    {
                        commandId = command.CommandId,
                        nodeId = command.NodeId,
                        ok = exists,
                        error = exists ? null : "instance not found"
                    });
                    break;
                }
                case "instance.resize":
                {
                    var instanceId = command.Payload.GetProperty("instance_id").GetString() ?? string.Empty;
                    var exists = false;
                    lock (slaveInstances)
                    {
                        exists = slaveInstances.Contains(instanceId);
                    }

                    await slaveHub.InvokeAsync("SubmitCommandResult", new
                    {
                        commandId = command.CommandId,
                        nodeId = command.NodeId,
                        ok = exists,
                        error = exists ? null : "instance not found"
                    });
                    break;
                }
                case "instance.terminate":
                {
                    var instanceId = command.Payload.GetProperty("instance_id").GetString() ?? string.Empty;
                    var exists = false;
                    lock (slaveInstances)
                    {
                        exists = slaveInstances.Remove(instanceId);
                    }

                    await slaveHub.InvokeAsync("SubmitCommandResult", new
                    {
                        commandId = command.CommandId,
                        nodeId = command.NodeId,
                        ok = exists,
                        error = exists ? null : "instance not found"
                    });
                    break;
                }
                case "files.upload":
                {
                    var instanceId = command.Payload.GetProperty("instance_id").GetString() ?? string.Empty;
                    var exists = false;
                    lock (slaveInstances)
                    {
                        exists = slaveInstances.Contains(instanceId);
                    }

                    await slaveHub.InvokeAsync("SubmitCommandResult", new
                    {
                        commandId = command.CommandId,
                        nodeId = command.NodeId,
                        ok = exists,
                        error = exists ? null : "instance not found",
                        payload = new { path = exists ? $"/tmp/slave-upload-{Guid.NewGuid():N}.png" : string.Empty, size = exists ? 8 : 0 }
                    });
                    break;
                }
                default:
                {
                    await slaveHub.InvokeAsync("SubmitCommandResult", new
                    {
                        commandId = command.CommandId,
                        nodeId = command.NodeId,
                        ok = false,
                        error = $"unsupported command: {command.Type}"
                    });
                    break;
                }
            }
        });

        await slaveHub.StartAsync();
        await slaveHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-test-token",
            nodeId = "slave-1",
            nodeName = "Slave 1",
            instanceCount = 0
        });

        var createRes = await client.PostAsJsonAsync("/api/nodes/slave-1/instances", new
        {
            command = "bash",
            args = new[] { "-i" },
            cols = 80,
            rows = 25,
            cwd = "/home/yueyuan"
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var instanceId = created.GetProperty("instance_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(instanceId));

        var inputRes = await client.PostAsJsonAsync($"/api/nodes/slave-1/instances/{instanceId}/input", new { data = "echo hi" });
        Assert.Equal(HttpStatusCode.OK, inputRes.StatusCode);

        var resizeRes = await client.PostAsJsonAsync($"/api/nodes/slave-1/instances/{instanceId}/resize", new { cols = 120, rows = 40 });
        Assert.Equal(HttpStatusCode.OK, resizeRes.StatusCode);

        using var remoteUploadBody = new MultipartFormDataContent();
        var remoteImage = new ByteArrayContent(Encoding.UTF8.GetBytes("png-data"));
        remoteImage.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        remoteUploadBody.Add(remoteImage, "file", "screen.png");
        remoteUploadBody.Add(new StringContent(instanceId!), "instance_id");
        var remoteUploadRes = await client.PostAsync("/api/nodes/slave-1/files/upload", remoteUploadBody);
        Assert.Equal(HttpStatusCode.OK, remoteUploadRes.StatusCode);
        var remoteUploadPayload = JsonDocument.Parse(await remoteUploadRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("slave-1", remoteUploadPayload.GetProperty("node_id").GetString());
        Assert.Equal(instanceId, remoteUploadPayload.GetProperty("instance_id").GetString());
        Assert.True(remoteUploadPayload.GetProperty("upload").GetProperty("path").GetString()!.Contains("slave-upload", StringComparison.Ordinal));

        var deleteRes = await client.DeleteAsync($"/api/nodes/slave-1/instances/{instanceId}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);
    }

    [Fact]
    public async Task Local_Node_File_Upload_Should_Write_Into_Instance_Upload_Directory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir,
            ["NODE_ID"] = "master-upload"
        });
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/api/instances", new
        {
            command = "bash",
            args = new[] { "-i" },
            cols = 80,
            rows = 25,
            cwd = tempDir
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var instanceId = created.GetProperty("instance_id").GetString()!;

        using var uploadBody = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(Encoding.UTF8.GetBytes("png-bytes-content"));
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        uploadBody.Add(imageContent, "file", "terminal.png");
        uploadBody.Add(new StringContent(instanceId), "instance_id");
        var uploadRes = await client.PostAsync("/api/nodes/master-upload/files/upload", uploadBody);
        Assert.Equal(HttpStatusCode.OK, uploadRes.StatusCode);

        var payload = JsonDocument.Parse(await uploadRes.Content.ReadAsStringAsync()).RootElement;
        var path = payload.GetProperty("upload").GetProperty("path").GetString();
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(File.Exists(path));
        Assert.Contains($"{Path.DirectorySeparatorChar}.webcli-uploads{Path.DirectorySeparatorChar}", path!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cluster_PublishTerminalEvent_Should_Deduplicate_And_Report_SeqGap()
    {
        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-event-token",
            ["NODE_ID"] = "master-events",
            ["NODE_NAME"] = "Master Events"
        });
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/api/instances", new
        {
            command = "bash",
            args = new[] { "-i" },
            cols = 80,
            rows = 25,
            cwd = "/home/yueyuan"
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement;
        var instanceId = created.GetProperty("instance_id").GetString()!;

        await using var terminalHub = BuildHubConnection(client);
        List<JsonElement> messages = [];
        var gate = new object();
        terminalHub.On<JsonElement>("TerminalEvent", msg =>
        {
            lock (gate)
            {
                messages.Add(msg.Clone());
            }
        });
        await terminalHub.StartAsync();
        await terminalHub.InvokeAsync("JoinInstance", new { instanceId });
        _ = await WaitForMessageAsync(messages, gate, msg => GetType(msg) == "term.snapshot", TimeSpan.FromSeconds(8));

        await using var clusterHub = BuildClusterHubConnection(client);
        await clusterHub.StartAsync();
        await clusterHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-event-token",
            nodeId = "slave-evt",
            nodeName = "Slave EVT",
            instanceCount = 1
        });

        var payloadSeq1 = new
        {
            v = 1,
            type = "term.patch",
            instance_id = instanceId,
            node_id = "slave-evt",
            node_name = "Slave EVT",
            seq = 1,
            rows = new[] { new { y = 0, segs = new object[] { new object[] { "from-slave-seq1", 0 } } } }
        };
        await clusterHub.InvokeAsync("PublishTerminalEvent", new
        {
            token = "cluster-event-token",
            eventId = "evt-1",
            nodeId = "slave-evt",
            instanceId,
            seq = 1,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            type = "term.patch",
            payload = payloadSeq1
        });
        _ = await WaitForMessageAsync(messages, gate, msg => JsonSerializer.Serialize(msg).Contains("from-slave-seq1", StringComparison.Ordinal), TimeSpan.FromSeconds(8));

        await clusterHub.InvokeAsync("PublishTerminalEvent", new
        {
            token = "cluster-event-token",
            eventId = "evt-1",
            nodeId = "slave-evt",
            instanceId,
            seq = 1,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            type = "term.patch",
            payload = payloadSeq1
        });

        await clusterHub.InvokeAsync("PublishTerminalEvent", new
        {
            token = "cluster-event-token",
            eventId = "evt-3",
            nodeId = "slave-evt",
            instanceId,
            seq = 3,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            type = "term.patch",
            payload = new
            {
                v = 1,
                type = "term.patch",
                instance_id = instanceId,
                node_id = "slave-evt",
                node_name = "Slave EVT",
                seq = 3,
                rows = new[] { new { y = 0, segs = new object[] { new object[] { "from-slave-seq3", 0 } } } }
            }
        });

        _ = await WaitForMessageAsync(messages, gate, msg => GetType(msg) == "term.route" && GetString(msg, "reason") == "seq_gap", TimeSpan.FromSeconds(8));

        var countSeq1 = 0;
        lock (gate)
        {
            countSeq1 = messages.Count(msg => JsonSerializer.Serialize(msg).Contains("from-slave-seq1", StringComparison.Ordinal));
        }

        Assert.Equal(1, countSeq1);
    }


    private static HubConnection BuildHubConnection(HttpClient client)
    {
        var baseAddress = client.BaseAddress ?? throw new InvalidOperationException("missing base address");
        var target = new Uri(baseAddress, "/hubs/terminal");
        return new HubConnectionBuilder()
            .WithUrl(target)
            .Build();
    }

    private static HubConnection BuildClusterHubConnection(HttpClient client)
    {
        var baseAddress = client.BaseAddress ?? throw new InvalidOperationException("missing base address");
        var target = new Uri(baseAddress, "/hubs/cluster");
        return new HubConnectionBuilder()
            .WithUrl(target)
            .Build();
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

internal sealed class GatewayFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public GatewayFactory(IReadOnlyDictionary<string, string?>? settings = null)
    {
        _settings = settings ?? new Dictionary<string, string?>();
        UseKestrel(0);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("PORT", "0");
        builder.UseSetting("HOST", "127.0.0.1");
        builder.UseSetting("HISTORY_LIMIT", "200");

        foreach (var kv in _settings)
        {
            builder.UseSetting(kv.Key, kv.Value);
        }
    }
}
