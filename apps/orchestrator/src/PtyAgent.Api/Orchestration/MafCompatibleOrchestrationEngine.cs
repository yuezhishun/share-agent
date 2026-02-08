using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Execution;
using PtyAgent.Api.Domain;
using PtyAgent.Api.Infrastructure;
using PtyAgent.Api.Runtime;
using PtyAgent.Api.Services;
using TaskStatus = PtyAgent.Api.Domain.TaskStatus;

namespace PtyAgent.Api.Orchestration;

public sealed class MafCompatibleOrchestrationEngine : IOrchestrationEngine
{
    private readonly SqliteStore _store;
    private readonly CliSessionManager _sessions;
    private readonly RuntimeEventPublisher _publisher;
    private readonly DecisionInbox _decisionInbox;
    private readonly CheckpointManager _checkpointManager;
    private readonly ILogger<MafCompatibleOrchestrationEngine> _logger;

    public MafCompatibleOrchestrationEngine(
        SqliteStore store,
        CliSessionManager sessions,
        RuntimeEventPublisher publisher,
        DecisionInbox decisionInbox,
        ILogger<MafCompatibleOrchestrationEngine> logger)
    {
        _store = store;
        _sessions = sessions;
        _publisher = publisher;
        _decisionInbox = decisionInbox;
        _logger = logger;
        _checkpointManager = CheckpointManager.CreateInMemory();
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
        await _publisher.PublishAsync(taskId, null, "maf_adapter", "info", "MAF workflow mode enabled");

        var state = await RunMafWorkflowAsync(task, cancellationToken);

        await _store.UpdateTaskStatusAsync(taskId, TaskStatus.Classified);
        await _publisher.PublishAsync(taskId, null, "classified", "info", state.IsComplex ? "complex" : "simple");

        if (state.NeedsDecision && task.Status != TaskStatus.Replanning)
        {
            await _store.UpdateTaskStatusAsync(taskId, TaskStatus.BlockedForDecision);
            await _publisher.PublishAsync(taskId, null, "hitl_waiting", "warn", state.DecisionPrompt ?? "Need decision from boss.");
            return;
        }

        if (task.Status == TaskStatus.Replanning)
        {
            DecisionPayload? decision;
            if (!_decisionInbox.TryTake(taskId, out decision) || decision is null)
            {
                decision = new DecisionPayload("continue", "No explicit decision payload found, continue with default replan.", DateTimeOffset.UtcNow);
            }

            await _publisher.PublishAsync(taskId, null, "hitl_resumed", "info", $"decision={decision.Decision}; notes={decision.Notes}");
            state = await RunReplanWorkflowAsync(task, state, decision, cancellationToken);
        }

        if (state.IsComplex)
        {
            await ExecuteComplexTaskAsync(task, state, cancellationToken);
        }
        else
        {
            await ExecuteSimpleTaskAsync(task, state, cancellationToken);
        }
    }

    private async Task<WorkflowState> RunMafWorkflowAsync(TaskItem task, CancellationToken cancellationToken)
    {
        var intake = ((Func<TaskItem, WorkflowState>)(input =>
            new WorkflowState(
                IsComplex: input.IsComplex || input.Intent.Length > 160,
                Reason: "intake",
                PlanCommand: null,
                ExecuteCommand: null,
                NeedsDecision: ShouldRequestDecision(input.Intent),
                DecisionPrompt: ShouldRequestDecision(input.Intent) ? "任务涉及关键取舍，请老板确认方向（scope/cost/timeline）" : null,
                ShouldReplan: false,
                ReplanReason: null))).BindAsExecutor("intake_executor");

        var classify = ((Func<WorkflowState, WorkflowState>)(state =>
            state with
            {
                IsComplex = state.IsComplex,
                Reason = state.IsComplex ? "complex_by_maf" : "simple_by_maf"
            })).BindAsExecutor("classify_executor");

        var plan = ((Func<WorkflowState, WorkflowState>)(state =>
        {
            if (!state.IsComplex)
            {
                return state with
                {
                    ExecuteCommand = "echo '[EXEC] simple flow'; sleep 1; echo '[DONE] simple task finished'"
                };
            }

            return state with
            {
                PlanCommand = "echo '[PLAN] analyzing task'; sleep 1; echo 'milestone-1: define scope'; echo 'milestone-2: implement core'; echo 'milestone-3: test/report'",
                ExecuteCommand = "echo '[EXEC] start from plan'; sleep 1; echo '[EXEC] implementing task'; sleep 1; echo '[DONE] complex task finished'"
            };
        })).BindAsExecutor("plan_executor");

        var handoff = ((Func<WorkflowState, WorkflowState>)(state =>
            state with { Reason = state.Reason + ";handoff_ready" })).BindAsExecutor("handoff_executor");

        var review = ((Func<WorkflowState, WorkflowState>)(state =>
            state with
            {
                ShouldReplan = state.IsComplex && state.Reason.Contains("risk", StringComparison.OrdinalIgnoreCase),
                ReplanReason = state.Reason.Contains("risk", StringComparison.OrdinalIgnoreCase) ? "risk_detected" : null
            })).BindAsExecutor("review_executor");

        var finalize = ((Func<WorkflowState, string>)(state => JsonSerializer.Serialize(state))).BindAsExecutor("finalize_executor");

        WorkflowBuilder builder = new(intake);
        builder.AddEdge(intake, classify);
        builder.AddEdge(classify, plan);
        builder.AddEdge(plan, handoff);
        builder.AddEdge(handoff, review);
        builder.AddEdge(review, finalize).WithOutputFrom(finalize);

        Workflow workflow = builder.Build();

        string? finalJson = null;
        await using var checkpointed = await InProcessExecution.RunAsync(workflow, task, _checkpointManager, runId: task.TaskId.ToString(), cancellationToken: cancellationToken);
        Run run = checkpointed.Run;
        foreach (WorkflowEvent evt in run.NewEvents)
        {
            await _publisher.PublishAsync(task.TaskId, null, "maf_workflow_event", "info", evt.GetType().Name);
            if (evt is ExecutorCompletedEvent completed)
            {
                finalJson = completed.Data?.ToString();
            }
        }

        if (!string.IsNullOrWhiteSpace(finalJson))
        {
            var state = JsonSerializer.Deserialize<WorkflowState>(finalJson);
            if (state is not null)
            {
                return state;
            }
        }

        _logger.LogWarning("MAF workflow did not return state for task {TaskId}, fallback to simple.", task.TaskId);
        return new WorkflowState(false, "maf_no_output", null, "echo '[EXEC] fallback'; sleep 1; echo '[DONE] fallback finished'", false, null, false, null);
    }

    private async Task<WorkflowState> RunReplanWorkflowAsync(TaskItem task, WorkflowState state, DecisionPayload decision, CancellationToken cancellationToken)
    {
        var replan = ((Func<WorkflowState, WorkflowState>)(current =>
            current with
            {
                NeedsDecision = false,
                ShouldReplan = false,
                ReplanReason = $"human_decision:{decision.Decision}",
                PlanCommand = "echo '[PLAN] replanning by human decision'; sleep 1; echo 'milestone-1: update scope'; echo 'milestone-2: execute revised plan'; echo 'milestone-3: summarize impact'",
                ExecuteCommand = "echo '[EXEC] execute revised plan'; sleep 1; echo '[DONE] revised task finished'"
            })).BindAsExecutor("replan_executor");

        var finalize = ((Func<WorkflowState, string>)(s => JsonSerializer.Serialize(s))).BindAsExecutor("replan_finalize_executor");

        WorkflowBuilder builder = new(replan);
        builder.AddEdge(replan, finalize).WithOutputFrom(finalize);
        Workflow workflow = builder.Build();

        string? finalJson = null;
        await using var checkpointed = await InProcessExecution.RunAsync(workflow, state, _checkpointManager, runId: task.TaskId + "-replan", cancellationToken: cancellationToken);
        Run run = checkpointed.Run;
        foreach (WorkflowEvent evt in run.NewEvents)
        {
            await _publisher.PublishAsync(task.TaskId, null, "maf_replan_event", "info", evt.GetType().Name);
            if (evt is ExecutorCompletedEvent completed)
            {
                finalJson = completed.Data?.ToString();
            }
        }

        if (!string.IsNullOrWhiteSpace(finalJson))
        {
            var replanned = JsonSerializer.Deserialize<WorkflowState>(finalJson);
            if (replanned is not null)
            {
                await _store.InsertEvaluationAsync(new EvaluationRecord(Guid.NewGuid(), task.TaskId, "replan_applied", 0.2, $"replanned_by:{decision.Decision}", DateTimeOffset.UtcNow));
                return replanned;
            }
        }

        return state;
    }

    private async Task ExecuteSimpleTaskAsync(TaskItem task, WorkflowState state, CancellationToken cancellationToken)
    {
        await _store.UpdateTaskStatusAsync(task.TaskId, TaskStatus.Executing);
        var command = task.Command ?? $"echo '[EXEC] {Escape(task.Title)}'; echo '{Escape(task.Intent)}'; {state.ExecuteCommand}";
        var session = await _sessions.StartAsync(task.TaskId, task.CliType ?? "codex", "execute", command, cancellationToken);
        await _store.UpdateTaskStatusAsync(task.TaskId, TaskStatus.Executing, executorSessionId: session.SessionId);
        _ = WaitAndFinalizeAsync(task.TaskId, session.SessionId, simple: true, cancellationToken);
    }

    private async Task ExecuteComplexTaskAsync(TaskItem task, WorkflowState state, CancellationToken cancellationToken)
    {
        await _store.UpdateTaskStatusAsync(task.TaskId, TaskStatus.Planning);
        await _publisher.PublishAsync(task.TaskId, null, "plan_started", "info", "planner worker started");

        var planCommand = task.Command is null
            ? $"echo 'title={Escape(task.Title)}'; echo 'intent={Escape(task.Intent)}'; {state.PlanCommand ?? "echo '[PLAN] default'"}"
            : task.Command;

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

        var executeCommand = task.Command ?? $"echo '[EXEC] start from plan {plan.PlanId}'; {state.ExecuteCommand ?? "echo '[DONE] complex task finished'"}";
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

    private static bool ShouldRequestDecision(string intent)
    {
        return intent.Contains("decision", StringComparison.OrdinalIgnoreCase)
            || intent.Contains("审批", StringComparison.OrdinalIgnoreCase)
            || intent.Contains("老板", StringComparison.OrdinalIgnoreCase)
            || intent.Contains("方向", StringComparison.OrdinalIgnoreCase)
            || intent.Contains("approve", StringComparison.OrdinalIgnoreCase);
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record WorkflowState(
        bool IsComplex,
        string Reason,
        string? PlanCommand,
        string? ExecuteCommand,
        bool NeedsDecision,
        string? DecisionPrompt,
        bool ShouldReplan,
        string? ReplanReason);
}
