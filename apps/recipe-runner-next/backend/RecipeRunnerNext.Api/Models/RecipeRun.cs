namespace RecipeRunnerNext.Api.Models;

public sealed record class RecipeRun
{
    public required string RunId { get; init; }
    public required string RecipeId { get; init; }
    public required string TargetNodeId { get; init; }
    public required string SourceNodeId { get; init; }
    public required string TriggerSource { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public int? ExitCode { get; init; }
    public required string RunnerType { get; init; }
    public RuntimeRef? RuntimeRef { get; init; }
    public required string StdoutSummary { get; init; }
    public required string StderrSummary { get; init; }
    public required IReadOnlyList<RunArtifact> Artifacts { get; init; }
    public string Error { get; init; } = string.Empty;
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
}
