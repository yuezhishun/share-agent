using PtyAgent.Api.Domain;
using PtyAgent.Api.Infrastructure;
using TaskStatus = PtyAgent.Api.Domain.TaskStatus;

namespace PtyAgent.Api.Tests;

public sealed class SqliteStoreTests
{
    [Fact]
    public async Task CanInsertAndQueryTaskAndEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), "pty-agent-tests", Guid.NewGuid().ToString("N"));
        var options = new SqliteOptions
        {
            DbPath = Path.Combine(root, "db.sqlite"),
            LogsPath = Path.Combine(root, "logs"),
            WorkdirsPath = Path.Combine(root, "workdirs")
        };

        var store = new SqliteStore(options);
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem(Guid.NewGuid(), "title", "intent", null, 1, TaskStatus.Queued, now, now, false, "codex", null, null, null);
        await store.InsertTaskAsync(task);

        await store.InsertEventAsync(new ProgressEvent(Guid.NewGuid(), task.TaskId, null, "task_queued", "info", "ok", DateTimeOffset.UtcNow));

        var loaded = await store.GetTaskAsync(task.TaskId);
        var timeline = await store.ListEventsByTaskAsync(task.TaskId);

        Assert.NotNull(loaded);
        Assert.Equal(task.TaskId, loaded!.TaskId);
        Assert.Single(timeline);
        Assert.Equal("task_queued", timeline[0].EventType);
    }
}
