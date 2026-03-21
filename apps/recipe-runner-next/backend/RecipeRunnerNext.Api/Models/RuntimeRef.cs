namespace RecipeRunnerNext.Api.Models;

public sealed class RuntimeRef
{
    public required string Kind { get; init; }
    public required string Id { get; init; }
    public required string NodeId { get; init; }
}

