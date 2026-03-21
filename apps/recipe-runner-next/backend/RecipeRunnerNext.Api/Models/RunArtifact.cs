namespace RecipeRunnerNext.Api.Models;

public sealed class RunArtifact
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Path { get; init; }
}

