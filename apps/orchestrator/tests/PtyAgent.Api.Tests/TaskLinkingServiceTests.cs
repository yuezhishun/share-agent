using PtyAgent.Api.Contracts;
using PtyAgent.Api.Domain;
using PtyAgent.Api.Infrastructure;
using PtyAgent.Api.Services;
using TaskStatus = PtyAgent.Api.Domain.TaskStatus;

namespace PtyAgent.Api.Tests;

public sealed class TaskLinkingServiceTests
{
    [Fact]
    public async Task UsesExplicitFollowUpWhenProvided()
    {
        var (store, _) = CreateStore();
        var existingTask = new TaskItem(Guid.NewGuid(), "A", "Build apartment report", null, 1, TaskStatus.Done, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, "codex", null, null, null);
        await store.InsertTaskAsync(existingTask);

        var service = new TaskLinkingService(store);
        var request = new CreateTaskRequest("follow up", "please continue", FollowUpTaskId: existingTask.TaskId);

        var link = await service.BuildLinkAsync(request, Guid.NewGuid());

        Assert.Equal(LinkType.FollowUp, link.LinkType);
        Assert.Equal(existingTask.TaskId, link.TaskId);
        Assert.Equal(1.0, link.Confidence);
    }

    [Fact]
    public async Task UsesSemanticFallbackWhenSimilarTaskExists()
    {
        var (store, _) = CreateStore();
        var existingTask = new TaskItem(Guid.NewGuid(), "Student apartment survey", "research nearby student apartment pricing and facilities", null, 1, TaskStatus.Done, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, true, "codex", null, null, null);
        await store.InsertTaskAsync(existingTask);

        var service = new TaskLinkingService(store);
        var request = new CreateTaskRequest("apartment update", "please update student apartment pricing info", null, 3, true, "codex", null, null, Guid.NewGuid());

        var link = await service.BuildLinkAsync(request, Guid.NewGuid());

        Assert.Equal(LinkType.FollowUp, link.LinkType);
        Assert.Equal(existingTask.TaskId, link.TaskId);
        Assert.True(link.Confidence >= 0.2);
    }

    [Fact]
    public async Task MarksNewWhenNoSimilarTask()
    {
        var (store, _) = CreateStore();
        var service = new TaskLinkingService(store);
        var newTaskId = Guid.NewGuid();

        var request = new CreateTaskRequest("build payment system", "implement payment integration", null, 3, true, "codex", null, null, Guid.NewGuid());
        var link = await service.BuildLinkAsync(request, newTaskId);

        Assert.Equal(LinkType.New, link.LinkType);
        Assert.Equal(newTaskId, link.TaskId);
        Assert.Equal(1.0, link.Confidence);
    }

    private static (SqliteStore store, string root) CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "pty-agent-tests", Guid.NewGuid().ToString("N"));
        var options = new SqliteOptions
        {
            DbPath = Path.Combine(root, "db.sqlite"),
            LogsPath = Path.Combine(root, "logs"),
            WorkdirsPath = Path.Combine(root, "workdirs")
        };

        var store = new SqliteStore(options);
        store.InitializeAsync().GetAwaiter().GetResult();
        return (store, root);
    }
}
