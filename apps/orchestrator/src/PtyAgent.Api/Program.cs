using PtyAgent.Api.Contracts;
using PtyAgent.Api.Domain;
using PtyAgent.Api.Hubs;
using PtyAgent.Api.Infrastructure;
using PtyAgent.Api.Orchestration;
using PtyAgent.Api.Runtime;
using PtyAgent.Api.Runtime.Terminal;
using PtyAgent.Api.Services;
using TaskStatus = PtyAgent.Api.Domain.TaskStatus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();

var sqliteOptions = new SqliteOptions();
builder.Configuration.GetSection("Sqlite").Bind(sqliteOptions);
builder.Services.AddSingleton(sqliteOptions);

var runtimeOptions = new RuntimeOptions();
builder.Configuration.GetSection("Runtime").Bind(runtimeOptions);
builder.Services.AddSingleton(runtimeOptions);

var orchestrationOptions = new OrchestrationOptions();
builder.Configuration.GetSection("Orchestration").Bind(orchestrationOptions);
builder.Services.AddSingleton(orchestrationOptions);

builder.Services.AddSingleton<SqliteStore>();
builder.Services.AddSingleton<RuntimeEventPublisher>();
builder.Services.AddSingleton<TaskLinkingService>();
builder.Services.AddSingleton<DecisionInbox>();
builder.Services.AddSingleton<TaskQueue>();
builder.Services.AddHttpClient("terminal-gateway", (sp, client) =>
{
    var options = sp.GetRequiredService<RuntimeOptions>();
    client.BaseAddress = new Uri(options.TerminalGatewayBaseUrl);
});
builder.Services.AddSingleton<ITerminalBackend, ProcessTerminalBackend>();
builder.Services.AddSingleton<ITerminalBackend, NodePtyTerminalBackend>();
builder.Services.AddSingleton<CliSessionManager>();
builder.Services.AddSingleton<DefaultOrchestrationEngine>();
builder.Services.AddSingleton<MafCompatibleOrchestrationEngine>();
builder.Services.AddSingleton<IOrchestrationEngine>(sp =>
{
    var options = sp.GetRequiredService<OrchestrationOptions>();
    return string.Equals(options.EngineProvider, "maf", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<MafCompatibleOrchestrationEngine>()
        : sp.GetRequiredService<DefaultOrchestrationEngine>();
});
builder.Services.AddHostedService<OrchestratorWorker>();

var app = builder.Build();

await app.Services.GetRequiredService<SqliteStore>().InitializeAsync();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/tasks", async (CreateTaskRequest request, SqliteStore store, TaskQueue queue, RuntimeEventPublisher publisher, TaskLinkingService linker) =>
{
    var now = DateTimeOffset.UtcNow;
    var taskId = Guid.NewGuid();
    var isComplex = request.IsComplex ?? request.Intent.Length > 160;

    var task = new TaskItem(
        taskId,
        request.Title,
        request.Intent,
        request.Constraints,
        request.Priority,
        TaskStatus.Queued,
        now,
        now,
        isComplex,
        request.CliType,
        request.Command,
        null,
        null);

    var link = await linker.BuildLinkAsync(request, taskId);
    await store.InsertTaskAsync(task);
    await store.InsertTaskLinkAsync(link);
    await publisher.PublishAsync(task.TaskId, null, "task_queued", "info", "Task accepted by secretary.");
    await publisher.PublishAsync(task.TaskId, null, "task_linked", "info", $"link_type={link.LinkType}; linked_task_id={link.TaskId}; confidence={link.Confidence:F2}");
    await queue.EnqueueAsync(task.TaskId);

    return Results.Accepted($"/api/tasks/{task.TaskId}", new
    {
        Task = task,
        Link = link
    });
});

app.MapGet("/api/tasks", async (int? limit, SqliteStore store) =>
{
    var clampedLimit = Math.Clamp(limit ?? 50, 1, 200);
    var tasks = await store.ListRecentTasksAsync(clampedLimit);
    return Results.Ok(tasks);
});

app.MapGet("/api/tasks/{taskId:guid}", async (Guid taskId, SqliteStore store) =>
{
    var task = await store.GetTaskAsync(taskId);
    return task is null ? Results.NotFound() : Results.Ok(task);
});

app.MapGet("/api/tasks/{taskId:guid}/timeline", async (Guid taskId, SqliteStore store) =>
{
    var events = await store.ListEventsByTaskAsync(taskId);
    return Results.Ok(events);
});

app.MapGet("/api/tasks/{taskId:guid}/sessions", async (Guid taskId, SqliteStore store) =>
{
    var sessions = await store.ListSessionsByTaskAsync(taskId);
    return Results.Ok(sessions);
});

app.MapPost("/api/tasks/{taskId:guid}/decision", async (Guid taskId, DecisionRequest request, SqliteStore store, RuntimeEventPublisher publisher, DecisionInbox inbox, TaskQueue queue) =>
{
    var task = await store.GetTaskAsync(taskId);
    if (task is null)
    {
        return Results.NotFound();
    }

    await store.UpdateTaskStatusAsync(taskId, TaskStatus.Replanning);
    await publisher.PublishAsync(taskId, null, "decision_received", "info", $"decision={request.Decision}; notes={request.Notes}");
    await store.InsertEvaluationAsync(new EvaluationRecord(Guid.NewGuid(), taskId, "human_decision", 0.3, "replanning_requested", DateTimeOffset.UtcNow));
    inbox.Submit(taskId, request.Decision, request.Notes);
    await queue.EnqueueAsync(taskId);

    return Results.Accepted($"/api/tasks/{taskId}");
});

app.MapPost("/api/sessions/{sessionId:guid}/input", async (Guid sessionId, SendSessionInputRequest request, CliSessionManager manager) =>
{
    await manager.SendInputAsync(sessionId, request.Input);
    return Results.Accepted();
});

app.MapPost("/api/sessions/{sessionId:guid}/terminate", async (Guid sessionId, CliSessionManager manager) =>
{
    await manager.TerminateAsync(sessionId);
    return Results.Accepted();
});

app.MapGet("/api/reports/progress", async (int? windowMinutes, SqliteStore store) =>
{
    var window = TimeSpan.FromMinutes(windowMinutes.GetValueOrDefault(45));
    var (total, running, done, failed, events) = await store.GetProgressSummaryAsync(window);
    return Results.Ok(new ProgressSummaryResponse(
        total,
        running,
        done,
        failed,
        events.Select(e => new
        {
            e.EventId,
            e.TaskId,
            e.SessionId,
            e.EventType,
            e.Severity,
            e.Payload,
            e.Timestamp
        }).ToList()));
});

app.MapGet("/api/knowledge/search", async (string q, SqliteStore store) =>
{
    var items = await store.SearchKnowledgeAsync(q);
    return Results.Ok(items);
});

app.MapHub<RuntimeHub>("/hubs/runtime");

app.Run();

public partial class Program
{
}
