using Microsoft.Extensions.Hosting;
using PtyAgent.Api.Domain;
using PtyAgent.Api.Infrastructure;
using PtyAgent.Api.Services;
using TaskStatus = PtyAgent.Api.Domain.TaskStatus;

namespace PtyAgent.Api.Orchestration;

public sealed class OrchestratorWorker : BackgroundService
{
    private readonly TaskQueue _queue;
    private readonly SqliteStore _store;
    private readonly IOrchestrationEngine _engine;
    private readonly RuntimeEventPublisher _publisher;
    private readonly ILogger<OrchestratorWorker> _logger;

    public OrchestratorWorker(TaskQueue queue, SqliteStore store, IOrchestrationEngine engine, RuntimeEventPublisher publisher, ILogger<OrchestratorWorker> logger)
    {
        _queue = queue;
        _store = store;
        _engine = engine;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid taskId;
            try
            {
                taskId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await _engine.RunTaskAsync(taskId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Orchestrator failed for task {TaskId}", taskId);
                await _store.UpdateTaskStatusAsync(taskId, TaskStatus.Failed);
                await _publisher.PublishAsync(taskId, null, "task_failed", "error", ex.Message);
            }
        }
    }
}
