using System.Text.Json;
using PtyAgent.Api.Domain;
using PtyAgent.Api.Infrastructure;
using PtyAgent.Api.Runtime;
using PtyAgent.Api.Services;
using TaskStatus = PtyAgent.Api.Domain.TaskStatus;

namespace PtyAgent.Api.Orchestration;

public sealed class DefaultOrchestrationEngine : IOrchestrationEngine
{
    private readonly SqliteStore _store;
    private readonly CliSessionManager _sessions;
    private readonly RuntimeEventPublisher _publisher;

    public DefaultOrchestrationEngine(SqliteStore store, CliSessionManager sessions, RuntimeEventPublisher publisher)
    {
        _store = store;
        _sessions = sessions;
        _publisher = publisher;
    }

    public async Task RunTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _store.GetTaskAsync(taskId);
        if (task is null)
        {
            return;
        }

        await _store.UpdateTaskStatusAsync(taskId, TaskStatus.IntakeStructured);
        await _publisher.PublishAsync(taskId, null, "intake_structured", "info", "Task structured by secretary API.");

        var isComplex = task.IsComplex || task.Intent.Length > 160 || task.Intent.Contains("系统", StringComparison.OrdinalIgnoreCase) || task.Intent.Contains("architecture", StringComparison.OrdinalIgnoreCase);
        await _store.UpdateTaskStatusAsync(taskId, TaskStatus.Classified);
        await _publisher.PublishAsync(taskId, null, "classified", "info", isComplex ? "complex" : "simple");

        if (isComplex)
        {
            await ExecuteComplexTaskAsync(task, cancellationToken);
        }
        else
        {
            await ExecuteSimpleTaskAsync(task, cancellationToken);
        }
    }

    private async Task ExecuteSimpleTaskAsync(TaskItem task, CancellationToken cancellationToken)
    {
        await _store.UpdateTaskStatusAsync(task.TaskId, TaskStatus.Executing);
        var command = task.Command ?? $"echo '[EXEC] {Escape(task.Title)}'; echo '{Escape(task.Intent)}'; sleep 1; echo '[DONE] simple task finished'";
        var session = await _sessions.StartAsync(task.TaskId, task.CliType ?? "codex", "execute", command, cancellationToken);
        await _store.UpdateTaskStatusAsync(task.TaskId, TaskStatus.Executing, executorSessionId: session.SessionId);

        _ = WaitAndFinalizeAsync(task.TaskId, session.SessionId, simple: true, cancellationToken);
    }

    private async Task ExecuteComplexTaskAsync(TaskItem task, CancellationToken cancellationToken)
    {
        await _store.UpdateTaskStatusAsync(task.TaskId, TaskStatus.Planning);
        await _publisher.PublishAsync(task.TaskId, null, "plan_started", "info", "planner worker started");

        var planCommand = $"echo '[PLAN] analyzing task'; echo 'title={Escape(task.Title)}'; echo 'intent={Escape(task.Intent)}'; sleep 1; echo 'milestone-1: define scope'; echo 'milestone-2: implement core'; echo 'milestone-3: test/report'";
        var plannerSession = await _sessions.StartAsync(task.TaskId, task.CliType ?? "codex", "plan", planCommand, cancellationToken);
        await _store.UpdateTaskStatusAsync(task.TaskId, TaskStatus.Planning, plannerSessionId: plannerSession.SessionId);

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        var plan = new PlanArtifact(
            Guid.NewGuid(),
            task.TaskId,
            plannerSession.SessionId,
            JsonSerializer.Serialize(new[] { "define scope", "implement core", "test/report" }),
            JsonSerializer.Serialize(new[] { "input: task intent", "output: implementation summary" }),
            "milestones completed and summary generated",
            "risk: command/tool failure",
            DateTimeOffset.UtcNow);

        await _store.InsertPlanAsync(plan);
        await _store.UpdateTaskStatusAsync(task.TaskId, TaskStatus.PlanReviewed, plannerSessionId: plannerSession.SessionId);
        await _publisher.PublishAsync(task.TaskId, plannerSession.SessionId, "plan_completed", "info", $"plan_id={plan.PlanId}");

        var executeCommand = task.Command ?? $"echo '[EXEC] start from plan {plan.PlanId}'; sleep 1; echo '[EXEC] implementing {Escape(task.Title)}'; sleep 1; echo '[DONE] complex task finished'";
        var executorSession = await _sessions.StartAsync(task.TaskId, task.CliType ?? "codex", "execute", executeCommand, cancellationToken);

        var handoff = new ExecutionHandoff(
            Guid.NewGuid(),
            task.TaskId,
            plan.PlanId,
            executorSession.SessionId,
            "plan artifact exists; io contracts checked",
            $"local://plans/{plan.PlanId}",
            DateTimeOffset.UtcNow);

        await _store.InsertHandoffAsync(handoff);
        await _store.UpdateTaskStatusAsync(task.TaskId, TaskStatus.Executing, plannerSessionId: plannerSession.SessionId, executorSessionId: executorSession.SessionId);
        await _publisher.PublishAsync(task.TaskId, executorSession.SessionId, "handoff_created", "info", $"handoff_id={handoff.HandoffId}");

        _ = WaitAndFinalizeAsync(task.TaskId, executorSession.SessionId, simple: false, cancellationToken);
    }

    private async Task WaitAndFinalizeAsync(Guid taskId, Guid sessionId, bool simple, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var session = await _store.GetSessionAsync(sessionId);
            if (session is null)
            {
                break;
            }

            if (session.Status is SessionStatus.Exited)
            {
                await _store.UpdateTaskStatusAsync(taskId, TaskStatus.Done);
                await _publisher.PublishAsync(taskId, sessionId, "task_done", "info", simple ? "simple workflow done" : "complex workflow done");
                await _store.InsertKnowledgeItemAsync(new KnowledgeItem(Guid.NewGuid(), taskId, "summary", simple ? "simple task completed" : "complex task completed", null, "auto,summary", DateTimeOffset.UtcNow));
                break;
            }

            if (session.Status is SessionStatus.Failed or SessionStatus.Terminated)
            {
                await _store.UpdateTaskStatusAsync(taskId, TaskStatus.Failed);
                await _store.InsertEvaluationAsync(new EvaluationRecord(Guid.NewGuid(), taskId, "session_exit_nonzero", 0.9, "mark_failed", DateTimeOffset.UtcNow));
                await _publisher.PublishAsync(taskId, sessionId, "task_failed", "error", "session failed");
                break;
            }

            await Task.Delay(500, cancellationToken);
        }
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
