using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;
using TerminalGateway.Api.Tests.Oracle;

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
    public async Task Process_Run_Endpoint_Works()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-process-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/processes/run", new
        {
            file = "sh",
            args = new[] { "-c", "printf 'hello-process'" },
            cwd = tempDir
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(payload.GetProperty("is_success").GetBoolean());
        Assert.Equal(0, payload.GetProperty("exit_code").GetInt32());
        Assert.Equal("hello-process", payload.GetProperty("standard_output").GetString());
    }

    [Fact]
    public async Task Process_Pipeline_Run_Endpoint_Works()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-process-pipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/processes/run", new
        {
            file = "sh",
            args = new[] { "-c", "printf 'alpha\\nbeta\\n'" },
            cwd = tempDir,
            pipeline = new object[]
            {
                new
                {
                    file = "wc",
                    args = new[] { "-l" }
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(payload.GetProperty("is_success").GetBoolean());
        Assert.Equal("2", (payload.GetProperty("standard_output").GetString() ?? string.Empty).Trim());
    }

    [Fact]
    public async Task Managed_Process_Endpoints_Work()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-process-managed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/processes", new
        {
            file = "sh",
            args = new[] { "-c", "echo start && sleep 0.2 && echo done" },
            cwd = tempDir
        });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()).RootElement;
        var processId = created.GetProperty("process_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(processId));

        var waitResponse = await client.PostAsync($"/api/processes/{processId}/wait?timeout_ms=5000", content: null);
        Assert.Equal(HttpStatusCode.OK, waitResponse.StatusCode);
        var waited = JsonDocument.Parse(await waitResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(waited.GetProperty("completed").GetBoolean());
        Assert.Equal("completed", waited.GetProperty("status").GetString());
        Assert.Contains("done", waited.GetProperty("result").GetProperty("standard_output").GetString() ?? string.Empty);

        var outputResponse = await client.GetAsync($"/api/processes/{processId}/output");
        Assert.Equal(HttpStatusCode.OK, outputResponse.StatusCode);
        var outputPayload = JsonDocument.Parse(await outputResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.NotEmpty(outputPayload.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Managed_Process_List_Get_And_Stop_Endpoints_Work()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-process-list-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/processes", new
        {
            file = "sh",
            args = new[] { "-c", "echo begin && sleep 2 && echo end" },
            cwd = tempDir,
            metadata = new Dictionary<string, object> { ["kind"] = "long-runner" }
        });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()).RootElement;
        var processId = created.GetProperty("process_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(processId));

        var listResponse = await client.GetAsync("/api/processes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(listPayload.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("process_id").GetString() == processId);

        var getResponse = await client.GetAsync($"/api/processes/{processId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var processPayload = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(processId, processPayload.GetProperty("process_id").GetString());
        Assert.Equal("long-runner", processPayload.GetProperty("metadata").GetProperty("kind").GetString());

        var stopResponse = await client.PostAsJsonAsync($"/api/processes/{processId}/stop", new { force = true });
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
        var stopped = JsonDocument.Parse(await stopResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(stopped.GetProperty("ok").GetBoolean());

        var afterStopResponse = await client.GetAsync($"/api/processes/{processId}");
        Assert.Equal(HttpStatusCode.OK, afterStopResponse.StatusCode);
        var afterStop = JsonDocument.Parse(await afterStopResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("failed", afterStop.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Managed_Process_Delete_Removes_Completed_Process()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-process-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/processes", new
        {
            file = "sh",
            args = new[] { "-c", "echo done" },
            cwd = tempDir
        });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()).RootElement;
        var processId = created.GetProperty("process_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(processId));

        var waitResponse = await client.PostAsync($"/api/processes/{processId}/wait?timeout_ms=5000", content: null);
        Assert.Equal(HttpStatusCode.OK, waitResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/processes/{processId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var deleted = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(deleted.GetProperty("ok").GetBoolean());

        var getResponse = await client.GetAsync($"/api/processes/{processId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Process_Run_Endpoint_Rejects_Cwd_Outside_Base()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-process-cwd-{Guid.NewGuid():N}");
        var outsideDir = Path.Combine(Path.GetTempPath(), $"tg-process-cwd-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(outsideDir);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/processes/run", new
        {
            file = "sh",
            args = new[] { "-c", "printf 'denied'" },
            cwd = outsideDir
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains("outside allowed base", payload.GetProperty("error").GetString());
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
            msg => GetType(msg) == "term.raw" && JsonSerializer.Serialize(msg).Contains("hello-webcli-dotnet", StringComparison.Ordinal),
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
        var snapshotCountBeforeResize = 0;
        lock (gate)
        {
            snapshotCountBeforeResize = messages.Count(msg => GetType(msg) == "term.snapshot");
        }

        await hub.InvokeAsync("RequestResize", new { instanceId, cols = 100, rows = 30, reqId = "resize-test" });

        var ack = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.resize.ack" && GetString(msg, "req_id") == "resize-test",
            TimeSpan.FromSeconds(8));
        Assert.Equal(100, ack.GetProperty("size").GetProperty("cols").GetInt32());
        Assert.Equal(30, ack.GetProperty("size").GetProperty("rows").GetInt32());
        await Task.Delay(250);
        var snapshotCountAfterResize = 0;
        lock (gate)
        {
            snapshotCountAfterResize = messages.Count(msg => GetType(msg) == "term.snapshot");
        }
        Assert.True(snapshotCountAfterResize > snapshotCountBeforeResize);

        await hub.InvokeAsync("SendInput", new { instanceId, data = "echo raw-sync-check\r" });
        _ = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.raw"
                && (!msg.TryGetProperty("replay", out var replayFlag) || replayFlag.ValueKind != JsonValueKind.True)
                && JsonSerializer.Serialize(msg).Contains("raw-sync-check", StringComparison.Ordinal),
            TimeSpan.FromSeconds(8));
        var liveRaw = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.raw"
                && (!msg.TryGetProperty("replay", out var replayFlag) || replayFlag.ValueKind != JsonValueKind.True)
                && JsonSerializer.Serialize(msg).Contains("raw-sync-check", StringComparison.Ordinal),
            TimeSpan.FromSeconds(8));
        Assert.True(liveRaw.TryGetProperty("seq", out var liveRawSeqProp) && liveRawSeqProp.GetInt32() > 0, "live term.raw missing seq");

        await hub.InvokeAsync("RequestSync", new { instanceId, type = "raw" });
        var rawReplay = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.raw"
                && msg.TryGetProperty("replay", out var replay)
                && replay.ValueKind == JsonValueKind.True
                && JsonSerializer.Serialize(msg).Contains("raw-sync-check", StringComparison.Ordinal),
            TimeSpan.FromSeconds(8));
        Assert.Equal("term.raw", GetType(rawReplay));
        Assert.True(rawReplay.TryGetProperty("to_seq", out var toSeqProp), "raw replay missing to_seq");
        Assert.True(rawReplay.TryGetProperty("seq", out var replaySeqProp), "raw replay missing seq");
        Assert.Equal(toSeqProp.GetInt32(), replaySeqProp.GetInt32());

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
        Assert.Equal("preview", payload.GetProperty("mode").GetString());

        var editReadRes = await client.GetAsync($"/api/files/read?path={Uri.EscapeDataString(file)}&mode=edit");
        Assert.Equal(HttpStatusCode.OK, editReadRes.StatusCode);
        var editPayload = JsonDocument.Parse(await editReadRes.Content.ReadAsStringAsync()).RootElement;
        Assert.False(editPayload.GetProperty("truncated").GetBoolean());
        Assert.Equal("edit", editPayload.GetProperty("mode").GetString());
        Assert.Equal("line1\nline2\nline3\n", editPayload.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Files_Edit_Mode_Should_Fall_Back_To_ReadOnly_Progressive_For_Large_File()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-files-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var file = Path.Combine(tempDir, "large.txt");
        var oversized = string.Join('\n', Enumerable.Range(1, 2500).Select(index => $"line-{index:D4}-{new string('a', 120)}"));
        await File.WriteAllTextAsync(file, oversized);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var response = await client.GetAsync($"/api/files/read?path={Uri.EscapeDataString(file)}&mode=edit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(payload.GetProperty("read_only").GetBoolean());
        Assert.True(payload.GetProperty("large_file").GetBoolean());
        Assert.Equal("progressive", payload.GetProperty("mode").GetString());
        Assert.True(payload.GetProperty("has_more_after").GetBoolean());
    }

    [Fact]
    public async Task Files_Progressive_Read_Should_Support_Load_More_And_Tail()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-files-progressive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var file = Path.Combine(tempDir, "progressive.txt");
        await File.WriteAllTextAsync(file, string.Join('\n', Enumerable.Range(1, 40).Select(index => $"line-{index:D2}")));

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir,
            ["TERMINAL_LARGE_FILE_THRESHOLD_BYTES"] = "16",
            ["TERMINAL_FILE_CHUNK_MAX_LINES"] = "5",
            ["TERMINAL_FILE_CHUNK_BYTES"] = "256"
        });
        using var client = app.CreateClient();

        var headResponse = await client.GetAsync($"/api/files/read?path={Uri.EscapeDataString(file)}&mode=edit");
        Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
        var head = JsonDocument.Parse(await headResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, head.GetProperty("cursor_start").GetInt32());
        Assert.Equal(5, head.GetProperty("cursor_end").GetInt32());
        Assert.True(head.GetProperty("has_more_after").GetBoolean());

        var moreResponse = await client.GetAsync($"/api/files/read?path={Uri.EscapeDataString(file)}&mode=progressive&line_offset=5&max_lines=5&chunk_bytes=256&direction=forward");
        Assert.Equal(HttpStatusCode.OK, moreResponse.StatusCode);
        var more = JsonDocument.Parse(await moreResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(5, more.GetProperty("cursor_start").GetInt32());
        Assert.Equal(10, more.GetProperty("cursor_end").GetInt32());
        Assert.Contains("line-06", more.GetProperty("content").GetString() ?? string.Empty, StringComparison.Ordinal);

        var tailResponse = await client.GetAsync($"/api/files/read?path={Uri.EscapeDataString(file)}&mode=progressive&max_lines=5&chunk_bytes=256&direction=tail");
        Assert.Equal(HttpStatusCode.OK, tailResponse.StatusCode);
        var tail = JsonDocument.Parse(await tailResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("tail", tail.GetProperty("mode").GetString());
        Assert.False(tail.GetProperty("has_more_after").GetBoolean());
        Assert.Contains("line-40", tail.GetProperty("content").GetString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Files_Mutate_And_Download_Endpoints_Work()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-files-ops-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var mkdirRes = await client.PostAsJsonAsync("/api/files/mkdir", new
        {
            path = tempDir,
            name = "docs"
        });
        Assert.Equal(HttpStatusCode.OK, mkdirRes.StatusCode);
        var mkdirPayload = JsonDocument.Parse(await mkdirRes.Content.ReadAsStringAsync()).RootElement;
        var docsDir = mkdirPayload.GetProperty("item").GetProperty("path").GetString()!;
        Assert.True(Directory.Exists(docsDir));

        using var uploadBody = new MultipartFormDataContent();
        uploadBody.Add(new StringContent(docsDir), "path");
        var textContent = new ByteArrayContent(Encoding.UTF8.GetBytes("hello file api"));
        textContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadBody.Add(textContent, "file", "note.txt");
        var uploadRes = await client.PostAsync("/api/files/upload", uploadBody);
        Assert.Equal(HttpStatusCode.OK, uploadRes.StatusCode);
        var uploadPayload = JsonDocument.Parse(await uploadRes.Content.ReadAsStringAsync()).RootElement;
        var uploadedPath = uploadPayload.GetProperty("upload").GetProperty("path").GetString()!;
        Assert.True(File.Exists(uploadedPath));

        var renameRes = await client.PostAsJsonAsync("/api/files/rename", new
        {
            path = uploadedPath,
            new_name = "renamed.txt"
        });
        Assert.Equal(HttpStatusCode.OK, renameRes.StatusCode);
        var renamePayload = JsonDocument.Parse(await renameRes.Content.ReadAsStringAsync()).RootElement;
        var renamedPath = renamePayload.GetProperty("item").GetProperty("path").GetString()!;
        Assert.True(File.Exists(renamedPath));

        var writeRes = await client.PostAsJsonAsync("/api/files/write", new
        {
            path = renamedPath,
            content = "updated text body"
        });
        Assert.Equal(HttpStatusCode.OK, writeRes.StatusCode);

        var readAfterWriteRes = await client.GetAsync($"/api/files/read?path={Uri.EscapeDataString(renamedPath)}&max_lines=20");
        Assert.Equal(HttpStatusCode.OK, readAfterWriteRes.StatusCode);
        var readAfterWritePayload = JsonDocument.Parse(await readAfterWriteRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains("updated text body", readAfterWritePayload.GetProperty("content").GetString() ?? string.Empty);

        var downloadRes = await client.GetAsync($"/api/files/download?path={Uri.EscapeDataString(renamedPath)}");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        var downloaded = await downloadRes.Content.ReadAsStringAsync();
        Assert.Equal("updated text body", downloaded);

        var downloadDirRes = await client.GetAsync($"/api/files/download?path={Uri.EscapeDataString(docsDir)}");
        Assert.Equal(HttpStatusCode.OK, downloadDirRes.StatusCode);
        Assert.Equal("application/zip", downloadDirRes.Content.Headers.ContentType?.MediaType);
        var zipBytes = await downloadDirRes.Content.ReadAsByteArrayAsync();
        using var zipStream = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entryNames = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
        Assert.Contains(entryNames, name => name.EndsWith("/renamed.txt", StringComparison.Ordinal));

        var removeFileRes = await client.DeleteAsync($"/api/files/remove?path={Uri.EscapeDataString(renamedPath)}");
        Assert.Equal(HttpStatusCode.OK, removeFileRes.StatusCode);
        Assert.False(File.Exists(renamedPath));

        var removeDirRes = await client.DeleteAsync($"/api/files/remove?path={Uri.EscapeDataString(docsDir)}");
        Assert.Equal(HttpStatusCode.OK, removeDirRes.StatusCode);
        Assert.False(Directory.Exists(docsDir));
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
    public async Task Master_Node_Proxy_Process_APIs_Should_Route_To_Slave_Through_ClusterHub()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-cluster-proc-slave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-process-token",
            ["NODE_ID"] = "master-process",
            ["NODE_NAME"] = "Master Process",
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        using var slaveProcesses = new ProcessApiService(new GatewayOptions
        {
            FilesBasePath = tempDir,
            ProcessManagerMaxConcurrency = 4
        });

        await using var slaveHub = BuildClusterHubConnection(client);
        slaveHub.On<ClusterCommandEnvelope>("ClusterCommand", async command =>
        {
            await SubmitProcessCommandResultAsync(slaveHub, slaveProcesses, command);
        });

        await slaveHub.StartAsync();
        await slaveHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-process-token",
            nodeId = "slave-process",
            nodeName = "Slave Process",
            instanceCount = 0
        });

        var runResponse = await client.PostAsJsonAsync("/api/nodes/slave-process/processes/run", new
        {
            file = "sh",
            args = new[] { "-c", "printf 'from-slave-run'" },
            cwd = tempDir
        });
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        var runPayload = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(runPayload.GetProperty("is_success").GetBoolean());
        Assert.Equal("from-slave-run", runPayload.GetProperty("standard_output").GetString());

        var createResponse = await client.PostAsJsonAsync("/api/nodes/slave-process/processes", new
        {
            file = "sh",
            args = new[] { "-c", "echo slave-managed-start && sleep 0.2 && echo slave-managed-done" },
            cwd = tempDir
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()).RootElement;
        var processId = created.GetProperty("process_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(processId));

        var listResponse = await client.GetAsync("/api/nodes/slave-process/processes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(listPayload.GetProperty("items").EnumerateArray(), item => item.GetProperty("process_id").GetString() == processId);

        var getResponse = await client.GetAsync($"/api/nodes/slave-process/processes/{processId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var waitResponse = await client.PostAsync($"/api/nodes/slave-process/processes/{processId}/wait?timeout_ms=5000", content: null);
        Assert.Equal(HttpStatusCode.OK, waitResponse.StatusCode);
        var waited = JsonDocument.Parse(await waitResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(waited.GetProperty("completed").GetBoolean());
        Assert.Equal("completed", waited.GetProperty("status").GetString());

        var outputResponse = await client.GetAsync($"/api/nodes/slave-process/processes/{processId}/output");
        Assert.Equal(HttpStatusCode.OK, outputResponse.StatusCode);
        var outputPayload = JsonDocument.Parse(await outputResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.NotEmpty(outputPayload.GetProperty("items").EnumerateArray());

        var deleteResponse = await client.DeleteAsync($"/api/nodes/slave-process/processes/{processId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var afterDeleteResponse = await client.GetAsync($"/api/nodes/slave-process/processes/{processId}");
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task Master_Node_Proxy_File_APIs_Should_Route_To_Slave_Through_ClusterHub()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-cluster-files-slave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "alpha"));
        await File.WriteAllTextAsync(Path.Combine(tempDir, "alpha", "hello.txt"), "from-slave-file");

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-files-token",
            ["NODE_ID"] = "master-files",
            ["NODE_NAME"] = "Master Files",
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        var slaveFiles = new FileApiService();
        await using var slaveHub = BuildClusterHubConnection(client);
        slaveHub.On<ClusterCommandEnvelope>("ClusterCommand", async command =>
        {
            await SubmitFileCommandResultAsync(slaveHub, slaveFiles, tempDir, command);
        });

        await slaveHub.StartAsync();
        await slaveHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-files-token",
            nodeId = "slave-files",
            nodeName = "Slave Files",
            instanceCount = 0
        });

        var listResponse = await client.GetAsync($"/api/nodes/slave-files/files/list?path={Uri.EscapeDataString(tempDir)}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(listPayload.GetProperty("items").EnumerateArray(), item => item.GetProperty("name").GetString() == "alpha");

        var readResponse = await client.GetAsync($"/api/nodes/slave-files/files/read?path={Uri.EscapeDataString(Path.Combine(tempDir, "alpha", "hello.txt"))}");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        var readPayload = JsonDocument.Parse(await readResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("from-slave-file", readPayload.GetProperty("content").GetString());

        var writePath = Path.Combine(tempDir, "alpha", "new.txt");
        var writeResponse = await client.PostAsJsonAsync("/api/nodes/slave-files/files/write", new
        {
            path = writePath,
            content = "new-slave-content"
        });
        Assert.Equal(HttpStatusCode.OK, writeResponse.StatusCode);
        Assert.Equal("new-slave-content", await File.ReadAllTextAsync(writePath));

        var mkdirResponse = await client.PostAsJsonAsync("/api/nodes/slave-files/files/mkdir", new
        {
            path = tempDir,
            name = "beta"
        });
        Assert.Equal(HttpStatusCode.OK, mkdirResponse.StatusCode);
        Assert.True(Directory.Exists(Path.Combine(tempDir, "beta")));

        var downloadResponse = await client.GetAsync($"/api/nodes/slave-files/files/download?path={Uri.EscapeDataString(writePath)}");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("new-slave-content", await downloadResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Slave_Node_Should_Request_Master_Create_And_Operate_Instance_Through_ClusterHub()
    {
        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-reverse-token",
            ["NODE_ID"] = "master-reverse",
            ["NODE_NAME"] = "Master Reverse"
        });
        using var client = app.CreateClient();

        await using var slaveHub = BuildClusterHubConnection(client);
        await slaveHub.StartAsync();
        await slaveHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-reverse-token",
            nodeId = "slave-origin",
            nodeName = "Slave Origin",
            instanceCount = 0
        });

        var createResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-token",
            sourceNodeId = "slave-origin",
            targetNodeId = "master-reverse",
            type = "instance.create",
            payload = new
            {
                command = "bash",
                args = new[] { "-i" },
                cols = 80,
                rows = 25,
                cwd = "/home/yueyuan"
            }
        });
        Assert.True(createResult.Ok);
        var instanceId = createResult.Payload.GetProperty("instance_id").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(instanceId));
        Assert.Equal("slave-origin", createResult.SourceNodeId);
        Assert.Equal("master-reverse", createResult.TargetNodeId);

        var inputResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-token",
            sourceNodeId = "slave-origin",
            targetNodeId = "master-reverse",
            type = "instance.input",
            payload = new { instance_id = instanceId, data = "echo reverse-path\r" }
        });
        Assert.True(inputResult.Ok);

        var resizeResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-token",
            sourceNodeId = "slave-origin",
            targetNodeId = "master-reverse",
            type = "instance.resize",
            payload = new { instance_id = instanceId, cols = 100, rows = 30 }
        });
        Assert.True(resizeResult.Ok);

        var syncResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-token",
            sourceNodeId = "slave-origin",
            targetNodeId = "master-reverse",
            type = "instance.sync",
            payload = new { instance_id = instanceId, type = "screen" }
        });
        Assert.True(syncResult.Ok);
        Assert.Equal("term.snapshot", syncResult.Payload.GetProperty("type").GetString());
        Assert.Equal("master-reverse", syncResult.Payload.GetProperty("node_id").GetString());
        Assert.Equal("Master Reverse", syncResult.Payload.GetProperty("node_name").GetString());

        var terminateResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-token",
            sourceNodeId = "slave-origin",
            targetNodeId = "master-reverse",
            type = "instance.terminate",
            payload = new { instance_id = instanceId }
        });
        Assert.True(terminateResult.Ok);

        await Task.Delay(250);
        var instancesPayload = JsonDocument.Parse(await client.GetStringAsync("/api/instances")).RootElement;
        Assert.DoesNotContain(instancesPayload.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == instanceId);
    }

    [Fact]
    public async Task Slave_Node_Should_Request_Master_Run_And_Manage_Processes_Through_ClusterHub()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tg-cluster-proc-master-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-reverse-process-token",
            ["NODE_ID"] = "master-proc-reverse",
            ["NODE_NAME"] = "Master Proc Reverse",
            ["FILES_BASE_PATH"] = tempDir
        });
        using var client = app.CreateClient();

        await using var slaveHub = BuildClusterHubConnection(client);
        await slaveHub.StartAsync();
        await slaveHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-reverse-process-token",
            nodeId = "slave-proc-origin",
            nodeName = "Slave Proc Origin",
            instanceCount = 0
        });

        var runResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-process-token",
            sourceNodeId = "slave-proc-origin",
            targetNodeId = "master-proc-reverse",
            type = "process.run",
            payload = new
            {
                file = "sh",
                args = new[] { "-c", "printf 'from-master-run'" },
                cwd = tempDir
            }
        });
        Assert.True(runResult.Ok);
        Assert.Equal("from-master-run", runResult.Payload.GetProperty("standard_output").GetString());
        Assert.True(runResult.Payload.GetProperty("is_success").GetBoolean());

        var startResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-process-token",
            sourceNodeId = "slave-proc-origin",
            targetNodeId = "master-proc-reverse",
            type = "process.start",
            payload = new
            {
                file = "sh",
                args = new[] { "-c", "echo master-managed-start && sleep 0.2 && echo master-managed-done" },
                cwd = tempDir
            }
        });
        Assert.True(startResult.Ok);
        var processId = startResult.Payload.GetProperty("process_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(processId));

        var getResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-process-token",
            sourceNodeId = "slave-proc-origin",
            targetNodeId = "master-proc-reverse",
            type = "process.get",
            payload = new { process_id = processId }
        });
        Assert.True(getResult.Ok);
        Assert.Equal(processId, getResult.Payload.GetProperty("process_id").GetString());

        var waitResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-process-token",
            sourceNodeId = "slave-proc-origin",
            targetNodeId = "master-proc-reverse",
            type = "process.wait",
            payload = new { process_id = processId, timeout_ms = 5000 }
        });
        Assert.True(waitResult.Ok);
        Assert.True(waitResult.Payload.GetProperty("completed").GetBoolean());
        Assert.Equal("completed", waitResult.Payload.GetProperty("status").GetString());

        var outputResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-process-token",
            sourceNodeId = "slave-proc-origin",
            targetNodeId = "master-proc-reverse",
            type = "process.output",
            payload = new { process_id = processId }
        });
        Assert.True(outputResult.Ok);
        Assert.NotEmpty(outputResult.Payload.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task TerminalHub_Should_Route_For_SlaveRequested_MasterInstance()
    {
        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-reverse-terminal-token",
            ["NODE_ID"] = "master-join",
            ["NODE_NAME"] = "Master Join"
        });
        using var client = app.CreateClient();

        await using var slaveHub = BuildClusterHubConnection(client);
        await slaveHub.StartAsync();
        await slaveHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-reverse-terminal-token",
            nodeId = "slave-join",
            nodeName = "Slave Join",
            instanceCount = 0
        });

        var createResult = await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-terminal-token",
            sourceNodeId = "slave-join",
            targetNodeId = "master-join",
            type = "instance.create",
            payload = new
            {
                command = "bash",
                args = new[] { "-i" },
                cols = 80,
                rows = 25,
                cwd = "/home/yueyuan"
            }
        });
        Assert.True(createResult.Ok);
        var instanceId = createResult.Payload.GetProperty("instance_id").GetString()!;

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
        var snapshot = await WaitForMessageAsync(messages, gate, msg => GetType(msg) == "term.snapshot", TimeSpan.FromSeconds(8));
        Assert.Equal("master-join", snapshot.GetProperty("node_id").GetString());
        Assert.Equal("Master Join", snapshot.GetProperty("node_name").GetString());

        await terminalHub.InvokeAsync("SendInput", new { instanceId, data = "echo reverse-terminal\r" });
        var raw = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.raw" && JsonSerializer.Serialize(msg).Contains("reverse-terminal", StringComparison.Ordinal),
            TimeSpan.FromSeconds(8));
        Assert.Equal("master-join", raw.GetProperty("node_id").GetString());

        await terminalHub.InvokeAsync("RequestResize", new { instanceId, cols = 110, rows = 35, reqId = "reverse-resize" });
        var ack = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.resize.ack" && GetString(msg, "req_id") == "reverse-resize",
            TimeSpan.FromSeconds(8));
        Assert.Equal("master-join", ack.GetProperty("node_id").GetString());

        await slaveHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-terminal-token",
            sourceNodeId = "slave-join",
            targetNodeId = "master-join",
            type = "instance.terminate",
            payload = new { instance_id = instanceId }
        });

        HubException? syncException = null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await terminalHub.InvokeAsync("RequestSync", new { instanceId, type = "screen" });
            }
            catch (HubException ex)
            {
                syncException = ex;
                break;
            }

            await Task.Delay(100);
        }

        Assert.NotNull(syncException);
        Assert.Contains("instance not found", syncException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TerminalHub_Should_Publish_Remote_Snapshot_After_Remote_Resize_Ack()
    {
        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-remote-resize-token",
            ["GATEWAY_ROLE"] = "master",
            ["NODE_ID"] = "master-resize",
            ["NODE_NAME"] = "Master Resize"
        });
        using var client = app.CreateClient();

        await using var slaveHub = BuildClusterHubConnection(client);
        const string instanceId = "slave-resize-1";

        slaveHub.On<ClusterCommandEnvelope>("ClusterCommand", async command =>
        {
            switch (command.Type)
            {
                case "instance.create":
                    await slaveHub.InvokeAsync("SubmitCommandResult", new
                    {
                        commandId = command.CommandId,
                        nodeId = command.NodeId,
                        ok = true,
                        payload = new
                        {
                            instance_id = instanceId,
                            summary = new
                            {
                                id = instanceId,
                                command = "bash",
                                cwd = "/tmp",
                                cols = 80,
                                rows = 24,
                                created_at = DateTimeOffset.UtcNow.ToString("O"),
                                status = "running",
                                clients = 0,
                                node_id = "slave-resize",
                                node_name = "Slave Resize",
                                node_role = "slave",
                                node_online = true
                            }
                        }
                    });
                    break;
                case "instance.sync":
                    await slaveHub.InvokeAsync("SubmitCommandResult", new
                    {
                        commandId = command.CommandId,
                        nodeId = command.NodeId,
                        ok = true,
                        payload = BuildRemoteSnapshot(instanceId, "slave-resize", "Slave Resize", 80, 24, 1)
                    });
                    break;
                case "instance.resize":
                    await slaveHub.InvokeAsync("SubmitCommandResult", new
                    {
                        commandId = command.CommandId,
                        nodeId = command.NodeId,
                        ok = true,
                        payload = new
                        {
                            ok = true,
                            snapshot = BuildRemoteSnapshot(instanceId, "slave-resize", "Slave Resize", 110, 35, 2)
                        }
                    });
                    break;
            }
        });

        await slaveHub.StartAsync();
        await slaveHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-remote-resize-token",
            nodeId = "slave-resize",
            nodeName = "Slave Resize",
            instanceCount = 0
        });

        var createResponse = await client.PostAsJsonAsync("/api/nodes/slave-resize/instances", new
        {
            command = "bash",
            args = new[] { "-i" },
            cols = 80,
            rows = 24,
            cwd = "/tmp"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

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

        await terminalHub.InvokeAsync("RequestResize", new { instanceId, cols = 110, rows = 35, reqId = "remote-resize" });
        _ = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.resize.ack" && GetString(msg, "req_id") == "remote-resize",
            TimeSpan.FromSeconds(8));
        var snapshot = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.snapshot"
                && msg.TryGetProperty("size", out var size)
                && size.TryGetProperty("cols", out var cols)
                && cols.GetInt32() == 110
                && size.TryGetProperty("rows", out var rows)
                && rows.GetInt32() == 35,
            TimeSpan.FromSeconds(8));

        Assert.Equal("slave-resize", snapshot.GetProperty("node_id").GetString());
        Assert.Equal("Slave Resize", snapshot.GetProperty("node_name").GetString());
    }

    [Fact]
    public async Task Reverse_Cluster_Command_Should_Reject_Unregistered_Or_Mismatched_Slave()
    {
        await using var app = new GatewayFactory(new Dictionary<string, string?>
        {
            ["CLUSTER_TOKEN"] = "cluster-reverse-auth-token",
            ["NODE_ID"] = "master-auth",
            ["NODE_NAME"] = "Master Auth"
        });
        using var client = app.CreateClient();

        await using var unknownHub = BuildClusterHubConnection(client);
        await unknownHub.StartAsync();
        var unregistered = await Assert.ThrowsAsync<HubException>(() => unknownHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-auth-token",
            sourceNodeId = "slave-unknown",
            targetNodeId = "master-auth",
            type = "instance.create",
            payload = new { command = "bash", args = new[] { "-i" }, cwd = "/home/yueyuan" }
        }));
        Assert.Contains("source node mismatch", unregistered.Message, StringComparison.Ordinal);

        await using var registeredHub = BuildClusterHubConnection(client);
        await registeredHub.StartAsync();
        await registeredHub.InvokeAsync("RegisterNode", new
        {
            token = "cluster-reverse-auth-token",
            nodeId = "slave-auth",
            nodeName = "Slave Auth",
            instanceCount = 0
        });

        var mismatched = await Assert.ThrowsAsync<HubException>(() => registeredHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "cluster-reverse-auth-token",
            sourceNodeId = "slave-other",
            targetNodeId = "master-auth",
            type = "instance.create",
            payload = new { command = "bash", args = new[] { "-i" }, cwd = "/home/yueyuan" }
        }));
        Assert.Contains("source node mismatch", mismatched.Message, StringComparison.Ordinal);

        var badToken = await Assert.ThrowsAsync<HubException>(() => registeredHub.InvokeAsync<ClusterCommandResult>("RequestCommand", new
        {
            token = "bad-token",
            sourceNodeId = "slave-auth",
            targetNodeId = "master-auth",
            type = "instance.create",
            payload = new { command = "bash", args = new[] { "-i" }, cwd = "/home/yueyuan" }
        }));
        Assert.Contains("unauthorized cluster token", badToken.Message, StringComparison.Ordinal);
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
            type = "term.raw",
            instance_id = instanceId,
            node_id = "slave-evt",
            node_name = "Slave EVT",
            seq = 1,
            data = "from-slave-seq1"
        };
        await clusterHub.InvokeAsync("PublishTerminalEvent", new
        {
            token = "cluster-event-token",
            eventId = "evt-1",
            nodeId = "slave-evt",
            instanceId,
            seq = 1,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            type = "term.raw",
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
            type = "term.raw",
            payload = new
            {
                v = 1,
                type = "term.raw",
                instance_id = instanceId,
                node_id = "slave-evt",
                node_name = "Slave EVT",
                seq = 3,
                data = "from-slave-seq3"
            }
        });

        _ = await WaitForMessageAsync(messages, gate, msg => GetType(msg) == "term.sync.required" && GetString(msg, "reason") == "seq_gap", TimeSpan.FromSeconds(8));

        var countSeq1 = 0;
        lock (gate)
        {
            countSeq1 = messages.Count(msg => JsonSerializer.Serialize(msg).Contains("from-slave-seq1", StringComparison.Ordinal));
        }

        Assert.Equal(1, countSeq1);
    }

    [Fact]
    [Trait("Category", "oracle")]
    public async Task RequestSyncSnapshot_Should_Converge_With_Oracle_State()
    {
        await using var app = new GatewayFactory();
        using var client = app.CreateClient();

        var createRes = await client.PostAsJsonAsync("/api/instances", new
        {
            command = "/bin/cat",
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

        const string input = "oracle-gateway-sync\n";
        await hub.InvokeAsync("SendInput", new { instanceId, data = input });
        var liveRaw = await WaitForMessageAsync(messages, gate,
            msg => GetType(msg) == "term.raw" && JsonSerializer.Serialize(msg).Contains("oracle-gateway-sync", StringComparison.Ordinal),
            TimeSpan.FromSeconds(8));

        await hub.InvokeAsync("RequestSync", new { instanceId, type = "screen" });
        await Task.Delay(250);

        JsonElement snapshot;
        lock (gate)
        {
            snapshot = messages
                .Where(msg => GetType(msg) == "term.snapshot")
                .OrderByDescending(msg => msg.GetProperty("ts").GetInt64())
                .FirstOrDefault();
        }

        Assert.True(snapshot.ValueKind == JsonValueKind.Object, "missing term.snapshot");
        Assert.True(snapshot.GetProperty("ts").GetInt64() >= liveRaw.GetProperty("ts").GetInt64(), "snapshot did not refresh after sync");

        using var oracle = new XTermOracleAdapter(80, 25);
        oracle.Feed(input);
        var expected = TerminalFrameNormalizer.FromOracle(oracle.Export());
        var actual = TerminalFrameNormalizer.FromSnapshot(snapshot);
        Assert.Equal(expected.Cols, actual.Cols);
        Assert.Equal(expected.Rows, actual.Rows);
        Assert.True(expected.VisibleLines.Count > 0);
        Assert.True(actual.VisibleLines.Count >= expected.VisibleLines.Count);
        for (var i = 0; i < expected.VisibleLines.Count; i++)
        {
            var actualLine = actual.VisibleLines[actual.VisibleLines.Count - expected.VisibleLines.Count + i];
            Assert.Equal(expected.VisibleLines[i], actualLine);
        }
    }


    private static async Task SubmitProcessCommandResultAsync(HubConnection slaveHub, ProcessApiService slaveProcesses, ClusterCommandEnvelope command)
    {
        try
        {
            object? payload = command.Type switch
            {
                "process.run" => await slaveProcesses.RunAsync(
                    command.Payload.Deserialize<RunProcessRequest>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RunProcessRequest(),
                    CancellationToken.None),
                "process.start" => await slaveProcesses.StartManagedAsync(
                    command.Payload.Deserialize<RunProcessRequest>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RunProcessRequest(),
                    CancellationToken.None),
                "process.list" => new { items = slaveProcesses.ListManaged() },
                "process.get" => slaveProcesses.GetManaged(command.Payload.GetProperty("process_id").GetString() ?? string.Empty),
                "process.output" => new { items = slaveProcesses.GetOutput(command.Payload.GetProperty("process_id").GetString() ?? string.Empty) },
                "process.wait" => await slaveProcesses.WaitManagedAsync(
                    command.Payload.GetProperty("process_id").GetString() ?? string.Empty,
                    command.Payload.TryGetProperty("timeout_ms", out var timeout) && timeout.ValueKind == JsonValueKind.Number ? timeout.GetInt32() : null),
                "process.stop" => await slaveProcesses.StopManagedAsync(
                    command.Payload.GetProperty("process_id").GetString() ?? string.Empty,
                    command.Payload.TryGetProperty("force", out var force) && force.ValueKind == JsonValueKind.True),
                "process.remove" => slaveProcesses.RemoveManaged(command.Payload.GetProperty("process_id").GetString() ?? string.Empty),
                _ => throw new InvalidOperationException($"unsupported command: {command.Type}")
            };
            var normalizedPayload = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            await slaveHub.InvokeAsync("SubmitCommandResult", new
            {
                commandId = command.CommandId,
                nodeId = command.NodeId,
                ok = true,
                payload = normalizedPayload
            });
        }
        catch (Exception ex)
        {
            await slaveHub.InvokeAsync("SubmitCommandResult", new
            {
                commandId = command.CommandId,
                nodeId = command.NodeId,
                ok = false,
                error = ex.Message,
                payload = new { }
            });
        }
    }

    private static async Task SubmitFileCommandResultAsync(HubConnection slaveHub, FileApiService slaveFiles, string basePath, ClusterCommandEnvelope command)
    {
        try
        {
            object? payload = command.Type switch
            {
                "files.list" => slaveFiles.List(
                    basePath,
                    TryGetPropertyInsensitive(command.Payload, "path", out var listPath) ? listPath.GetString() : null,
                    TryGetPropertyInsensitive(command.Payload, "show_hidden", out var showHidden) && (
                        showHidden.ValueKind == JsonValueKind.True
                        || (showHidden.ValueKind == JsonValueKind.Number && showHidden.GetInt32() != 0)
                        || (showHidden.ValueKind == JsonValueKind.String && bool.TryParse(showHidden.GetString(), out var showHiddenBool) && showHiddenBool))),
                "files.read" => await slaveFiles.ReadAsync(
                    basePath,
                    TryGetPropertyInsensitive(command.Payload, "path", out var readPath) ? readPath.GetString() : null,
                    TryGetPropertyInsensitive(command.Payload, "max_lines", out var maxLines) && maxLines.ValueKind == JsonValueKind.Number ? maxLines.GetInt32() : 500,
                    TryGetPropertyInsensitive(command.Payload, "mode", out var mode) ? mode.GetString() : null,
                    CancellationToken.None,
                    TryGetPropertyInsensitive(command.Payload, "chunk_bytes", out var chunkBytes) && chunkBytes.ValueKind == JsonValueKind.Number ? chunkBytes.GetInt32() : null,
                    TryGetPropertyInsensitive(command.Payload, "line_offset", out var lineOffset) && lineOffset.ValueKind == JsonValueKind.Number ? lineOffset.GetInt32() : null,
                    TryGetPropertyInsensitive(command.Payload, "direction", out var direction) ? direction.GetString() : null),
                "files.write" => await slaveFiles.WriteAsync(
                    basePath,
                    TryGetPropertyInsensitive(command.Payload, "path", out var writePath) ? writePath.GetString() : null,
                    TryGetPropertyInsensitive(command.Payload, "content", out var content) ? content.GetString() : string.Empty,
                    CancellationToken.None),
                "files.mkdir" => slaveFiles.CreateDirectory(
                    basePath,
                    TryGetPropertyInsensitive(command.Payload, "path", out var mkdirPath) ? mkdirPath.GetString() : null,
                    TryGetPropertyInsensitive(command.Payload, "name", out var mkdirName) ? mkdirName.GetString() : null),
                "files.download" => await BuildDownloadPayloadAsync(
                    slaveFiles,
                    basePath,
                    TryGetPropertyInsensitive(command.Payload, "path", out var downloadPath) ? downloadPath.GetString() : null,
                    CancellationToken.None),
                _ => throw new InvalidOperationException($"unsupported command: {command.Type}")
            };
            var normalizedPayload = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            await slaveHub.InvokeAsync("SubmitCommandResult", new
            {
                commandId = command.CommandId,
                nodeId = command.NodeId,
                ok = true,
                payload = normalizedPayload
            });
        }
        catch (Exception ex)
        {
            await slaveHub.InvokeAsync("SubmitCommandResult", new
            {
                commandId = command.CommandId,
                nodeId = command.NodeId,
                ok = false,
                error = ex.Message,
                payload = new { }
            });
        }
    }

    private static async Task<object> BuildDownloadPayloadAsync(FileApiService slaveFiles, string basePath, string? path, CancellationToken cancellationToken)
    {
        var download = slaveFiles.OpenDownloadStream(basePath, path);
        await using var stream = download.Stream;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return new
        {
            name = download.Name,
            content_type = download.ContentType,
            enable_range_processing = download.EnableRangeProcessing,
            content_base64 = Convert.ToBase64String(buffer.ToArray())
        };
    }

    private static bool TryGetPropertyInsensitive(JsonElement payload, string name, out JsonElement value)
    {
        if (payload.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static HubConnection BuildHubConnection(HttpClient client)
    {
        var baseAddress = client.BaseAddress ?? throw new InvalidOperationException("missing base address");
        var target = new Uri(baseAddress, "/hubs/terminal-v2");
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
        if (!msg.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        if (!string.Equals(name, "type", StringComparison.Ordinal))
        {
            return text;
        }

        return text switch
        {
            "term.v2.snapshot" => "term.snapshot",
            "term.v2.raw" => "term.raw",
            "term.v2.resize.ack" => "term.resize.ack",
            "term.v2.sync.complete" => "term.sync.complete",
            "term.v2.sync.required" => "term.sync.required",
            "term.v2.owner.changed" => "term.owner.changed",
            _ => text
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

        lock (gate)
        {
            var summary = string.Join(", ", messages.Select(msg =>
            {
                var type = GetType(msg) ?? "<unknown>";
                var replay = msg.TryGetProperty("replay", out var replayValue) ? replayValue.ToString() : "-";
                var reqId = GetString(msg, "req_id") ?? "-";
                return $"{type}(replay={replay},req={reqId})";
            }));
            throw new TimeoutException($"timed out waiting signalr frame; received: [{summary}]");
        }
    }

    private static object BuildRemoteSnapshot(string instanceId, string nodeId, string nodeName, int cols, int rows, long renderEpoch)
    {
        return new
        {
            v = 1,
            type = "term.snapshot",
            instance_id = instanceId,
            node_id = nodeId,
            node_name = nodeName,
            seq = renderEpoch,
            base_seq = renderEpoch,
            render_epoch = renderEpoch,
            instance_epoch = 1,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            size = new { cols, rows },
            cursor = new { x = 0, y = 0, visible = true },
            rows = new object[]
            {
                new { y = 0, segs = new object[] { new object[] { $"size={cols}x{rows}", 0 } } }
            }
        };
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
