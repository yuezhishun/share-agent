namespace RecipeRunnerNext.Api.Models;

public sealed class CreateRecipeRequest
{
    public string Name { get; init; } = string.Empty;
    public string Group { get; init; } = "general";
    public string Cwd { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public List<string>? Args { get; init; }
    public Dictionary<string, string>? Env { get; init; }
    public string DefaultRunner { get; init; } = "managed_job";
}

