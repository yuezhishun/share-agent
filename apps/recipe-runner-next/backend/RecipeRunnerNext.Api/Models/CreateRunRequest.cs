namespace RecipeRunnerNext.Api.Models;

public sealed class CreateRunRequest
{
    public string RecipeId { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public RunOverrides? Overrides { get; init; }
}

