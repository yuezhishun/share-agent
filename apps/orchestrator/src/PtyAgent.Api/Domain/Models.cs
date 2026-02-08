namespace PtyAgent.Api.Domain;

public enum TaskStatus
{
    Queued,
    IntakeStructured,
    Classified,
    Planning,
    PlanReviewed,
    HandoffReady,
    Executing,
    BlockedForDecision,
    Replanning,
    Done,
    Failed,
    Canceled
}

public enum LinkType
{
    New,
    FollowUp
}

public enum SessionStatus
{
    Starting,
    Running,
    Exited,
    Failed,
    Terminated
}

public sealed record TaskItem(
    Guid TaskId,
    string Title,
    string Intent,
    string? Constraints,
    int Priority,
    TaskStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsComplex,
    string? CliType,
    string? Command,
    Guid? PlannerSessionId,
    Guid? ExecutorSessionId
);

public sealed record TaskLink(
    Guid SourceInputId,
    Guid TaskId,
    LinkType LinkType,
    double Confidence,
    string Reason,
    DateTimeOffset CreatedAt
);

public sealed record PlanArtifact(
    Guid PlanId,
    Guid TaskId,
    Guid PlannerSessionId,
    string MilestonesJson,
    string IoContractsJson,
    string AcceptanceCriteria,
    string Risks,
    DateTimeOffset CreatedAt
);

public sealed record ExecutionHandoff(
    Guid HandoffId,
    Guid TaskId,
    Guid FromPlanId,
    Guid ExecutorSessionId,
    string HandoffChecklist,
    string ContextBundleRef,
    DateTimeOffset CreatedAt
);

public sealed record ExecutionSession(
    Guid SessionId,
    Guid TaskId,
    string CliType,
    string Workdir,
    string? EnvProfile,
    SessionStatus Status,
    int? Pid,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Mode
);

public sealed record ProgressEvent(
    Guid EventId,
    Guid TaskId,
    Guid? SessionId,
    string EventType,
    string Severity,
    string Payload,
    DateTimeOffset Timestamp
);

public sealed record EvaluationRecord(
    Guid RecordId,
    Guid TaskId,
    string RuleId,
    double DriftScore,
    string ActionTaken,
    DateTimeOffset CreatedAt
);

public sealed record KnowledgeItem(
    Guid ItemId,
    Guid TaskId,
    string Kind,
    string Content,
    string? EmbeddingRef,
    string Tags,
    DateTimeOffset CreatedAt
);
