using System.ComponentModel.DataAnnotations;

namespace PtyAgent.Api.Contracts;

public sealed record CreateTaskRequest(
    [property: Required] string Title,
    [property: Required] string Intent,
    string? Constraints = null,
    int Priority = 3,
    bool? IsComplex = null,
    string CliType = "codex",
    string? Command = null,
    Guid? FollowUpTaskId = null,
    Guid? SourceInputId = null
);

public sealed record DecisionRequest(
    [property: Required] string Decision,
    string? Notes
);

public sealed record SendSessionInputRequest(
    [property: Required] string Input
);

public sealed record ProgressSummaryResponse(
    int TotalTasks,
    int RunningTasks,
    int DoneTasks,
    int FailedTasks,
    IReadOnlyList<object> RecentEvents
);
