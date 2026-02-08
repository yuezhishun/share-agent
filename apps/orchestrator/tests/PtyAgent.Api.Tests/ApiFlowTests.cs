using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PtyAgent.Api.Tests;

public sealed class ApiFlowTests
{
    [Fact]
    public async Task ComplexTask_ShouldGoThroughPlanAndExecute()
    {
        var root = Path.Combine(Path.GetTempPath(), "pty-agent-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        await using var factory = new TestAppFactory(root);
        using var client = factory.CreateClient();

        var createPayload = new
        {
            title = "Build XXX system",
            intent = "Please design architecture and implement core backend modules for a software system.",
            isComplex = true,
            cliType = "codex"
        };

        var createRes = await client.PostAsync("/api/tasks", new StringContent(JsonSerializer.Serialize(createPayload), Encoding.UTF8, "application/json"));
        createRes.EnsureSuccessStatusCode();

        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var taskId = createDoc.RootElement.GetProperty("task").GetProperty("taskId").GetGuid();

        var report = await PollReportUntilDoneAsync(client, TimeSpan.FromSeconds(15));
        Assert.True(report.doneTasks >= 1);

        var timelineJson = await PollTimelineUntilContainsAsync(
            client,
            taskId,
            TimeSpan.FromSeconds(10),
            "plan_started",
            "handoff_created",
            "task_done",
            "maf_adapter",
            "maf_workflow_event");

        Assert.Contains("plan_started", timelineJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("handoff_created", timelineJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("task_done", timelineJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maf_adapter", timelineJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maf_workflow_event", timelineJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HitlTask_ShouldBlockThenResumeAfterDecision()
    {
        var root = Path.Combine(Path.GetTempPath(), "pty-agent-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        await using var factory = new TestAppFactory(root);
        using var client = factory.CreateClient();

        var createPayload = new
        {
            title = "HITL task",
            intent = "This task needs boss decision before execution.",
            isComplex = true,
            cliType = "codex"
        };

        var createRes = await client.PostAsync("/api/tasks", new StringContent(JsonSerializer.Serialize(createPayload), Encoding.UTF8, "application/json"));
        createRes.EnsureSuccessStatusCode();

        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var taskId = createDoc.RootElement.GetProperty("task").GetProperty("taskId").GetGuid();

        var blockedTimeline = await PollTimelineUntilContainsAsync(client, taskId, TimeSpan.FromSeconds(10), "hitl_waiting");
        Assert.Contains("hitl_waiting", blockedTimeline, StringComparison.OrdinalIgnoreCase);

        var decisionPayload = new { decision = "approve", notes = "continue with revised plan" };
        var decisionRes = await client.PostAsync($"/api/tasks/{taskId}/decision", new StringContent(JsonSerializer.Serialize(decisionPayload), Encoding.UTF8, "application/json"));
        decisionRes.EnsureSuccessStatusCode();

        var completedTimeline = await PollTimelineUntilContainsAsync(client, taskId, TimeSpan.FromSeconds(15), "hitl_resumed", "maf_replan_event", "task_done");
        Assert.Contains("hitl_resumed", completedTimeline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maf_replan_event", completedTimeline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("task_done", completedTimeline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NodePtyUnavailable_ShouldFallbackToProcessAndFinish()
    {
        var root = Path.Combine(Path.GetTempPath(), "pty-agent-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        await using var factory = new TestAppFactory(root, backend: "nodepty");
        using var client = factory.CreateClient();

        var createPayload = new
        {
            title = "nodepty fallback",
            intent = "Simple task for backend fallback verification.",
            isComplex = false,
            cliType = "codex"
        };

        var createRes = await client.PostAsync("/api/tasks", new StringContent(JsonSerializer.Serialize(createPayload), Encoding.UTF8, "application/json"));
        createRes.EnsureSuccessStatusCode();

        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var taskId = createDoc.RootElement.GetProperty("task").GetProperty("taskId").GetGuid();

        var timeline = await PollTimelineUntilContainsAsync(client, taskId, TimeSpan.FromSeconds(15), "pty_fallback", "task_done");
        Assert.Contains("pty_fallback", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("task_done", timeline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskListEndpoint_ShouldReturnCreatedTask()
    {
        var root = Path.Combine(Path.GetTempPath(), "pty-agent-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        await using var factory = new TestAppFactory(root);
        using var client = factory.CreateClient();

        var createPayload = new
        {
            title = "list endpoint task",
            intent = "Simple task for list api.",
            isComplex = false,
            cliType = "codex"
        };

        var createRes = await client.PostAsync("/api/tasks", new StringContent(JsonSerializer.Serialize(createPayload), Encoding.UTF8, "application/json"));
        createRes.EnsureSuccessStatusCode();

        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var taskId = createDoc.RootElement.GetProperty("task").GetProperty("taskId").GetGuid();

        var listRes = await client.GetAsync("/api/tasks?limit=10");
        listRes.EnsureSuccessStatusCode();
        var listJson = await listRes.Content.ReadAsStringAsync();
        Assert.Contains(taskId.ToString(), listJson, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int doneTasks, string raw)> PollReportUntilDoneAsync(HttpClient client, TimeSpan timeout)
    {
        var start = DateTimeOffset.UtcNow;
        string latest = string.Empty;
        while (DateTimeOffset.UtcNow - start < timeout)
        {
            var reportRes = await client.GetAsync("/api/reports/progress");
            reportRes.EnsureSuccessStatusCode();
            latest = await reportRes.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(latest);
            var done = doc.RootElement.GetProperty("doneTasks").GetInt32();
            if (done >= 1)
            {
                return (done, latest);
            }

            await Task.Delay(300);
        }

        return (0, latest);
    }

    private static async Task<string> PollTimelineUntilContainsAsync(HttpClient client, Guid taskId, TimeSpan timeout, params string[] markers)
    {
        var start = DateTimeOffset.UtcNow;
        string latest = string.Empty;

        while (DateTimeOffset.UtcNow - start < timeout)
        {
            var timelineRes = await client.GetAsync($"/api/tasks/{taskId}/timeline");
            timelineRes.EnsureSuccessStatusCode();
            latest = await timelineRes.Content.ReadAsStringAsync();

            if (markers.All(x => latest.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                return latest;
            }

            await Task.Delay(250);
        }

        return latest;
    }

    private sealed class TestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _root;
        private readonly string _backend;

        public TestAppFactory(string root, string backend = "process")
        {
            _root = root;
            _backend = backend;
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["Sqlite:DbPath"] = Path.Combine(_root, "test.db"),
                    ["Sqlite:LogsPath"] = Path.Combine(_root, "logs"),
                    ["Sqlite:WorkdirsPath"] = Path.Combine(_root, "workdirs"),
                    ["Runtime:TerminalBackend"] = _backend,
                    ["Orchestration:EngineProvider"] = "maf"
                };
                config.AddInMemoryCollection(values);
            });
        }
    }
}
