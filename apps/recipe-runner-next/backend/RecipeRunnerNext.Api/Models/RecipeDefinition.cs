namespace RecipeRunnerNext.Api.Models;

public sealed class RecipeDefinition
{
    public required string RecipeId { get; init; }
    public required string NodeId { get; init; }
    public required string Name { get; init; }
    public required string Group { get; init; }
    public required string Cwd { get; init; }
    public required string Command { get; init; }
    public required IReadOnlyList<string> Args { get; init; }
    public required IReadOnlyDictionary<string, string> Env { get; init; }
    public required string DefaultRunner { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

