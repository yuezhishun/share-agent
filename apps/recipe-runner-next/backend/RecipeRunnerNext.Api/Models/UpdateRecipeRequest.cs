namespace RecipeRunnerNext.Api.Models;

public sealed class UpdateRecipeRequest
{
    public string? Name { get; init; }
    public string? Group { get; init; }
    public string? Cwd { get; init; }
    public string? Command { get; init; }
    public List<string>? Args { get; init; }
    public Dictionary<string, string>? Env { get; init; }
    public string? DefaultRunner { get; init; }
}

