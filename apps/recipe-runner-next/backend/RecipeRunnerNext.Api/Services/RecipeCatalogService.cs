using System.Text.Json;
using RecipeRunnerNext.Api.Models;

namespace RecipeRunnerNext.Api.Services;

public sealed class RecipeCatalogService
{
    private readonly string _rootPath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();

    public RecipeCatalogService(IHostEnvironment environment)
    {
        _rootPath = Path.Combine(environment.ContentRootPath, "App_Data", "recipes");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<IReadOnlyList<RecipeDefinition>> ListAsync(string nodeId, CancellationToken cancellationToken)
    {
        var normalizedNodeId = NormalizeNodeId(nodeId);
        var items = await ReadNodeFileAsync(normalizedNodeId, cancellationToken);
        return items.OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RecipeDefinition> CreateAsync(string nodeId, CreateRecipeRequest request, CancellationToken cancellationToken)
    {
        var normalizedNodeId = NormalizeNodeId(nodeId);
        var now = DateTimeOffset.UtcNow;
        var item = new RecipeDefinition
        {
            RecipeId = $"recipe-{Guid.NewGuid():N}",
            NodeId = normalizedNodeId,
            Name = NormalizeRequired(request.Name, "name"),
            Group = NormalizeOptional(request.Group, "general"),
            Cwd = NormalizeOptional(request.Cwd, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            Command = NormalizeRequired(request.Command, "command"),
            Args = NormalizeArgs(request.Args),
            Env = NormalizeEnv(request.Env),
            DefaultRunner = NormalizeRunner(request.DefaultRunner),
            CreatedAt = now,
            UpdatedAt = now
        };

        lock (_gate)
        {
            var items = ReadNodeFile(normalizedNodeId);
            items.Add(item);
            WriteNodeFile(normalizedNodeId, items);
        }

        await Task.CompletedTask;
        return item;
    }

    public async Task<RecipeDefinition?> UpdateAsync(string nodeId, string recipeId, UpdateRecipeRequest request, CancellationToken cancellationToken)
    {
        var normalizedNodeId = NormalizeNodeId(nodeId);
        var normalizedRecipeId = NormalizeRequired(recipeId, "recipeId");
        RecipeDefinition? updated = null;
        var found = false;

        lock (_gate)
        {
            var items = ReadNodeFile(normalizedNodeId);
            var current = items.FirstOrDefault(x => string.Equals(x.RecipeId, normalizedRecipeId, StringComparison.Ordinal));
            if (current is null)
            {
                goto done;
            }
            found = true;

            updated = new RecipeDefinition
            {
                RecipeId = current.RecipeId,
                NodeId = current.NodeId,
                Name = request.Name is null ? current.Name : NormalizeRequired(request.Name, "name"),
                Group = request.Group is null ? current.Group : NormalizeOptional(request.Group, "general"),
                Cwd = request.Cwd is null ? current.Cwd : NormalizeOptional(request.Cwd, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                Command = request.Command is null ? current.Command : NormalizeRequired(request.Command, "command"),
                Args = request.Args is null ? current.Args : NormalizeArgs(request.Args),
                Env = request.Env is null ? current.Env : NormalizeEnv(request.Env),
                DefaultRunner = request.DefaultRunner is null ? current.DefaultRunner : NormalizeRunner(request.DefaultRunner),
                CreatedAt = current.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var index = items.FindIndex(x => string.Equals(x.RecipeId, normalizedRecipeId, StringComparison.Ordinal));
            items[index] = updated;
            WriteNodeFile(normalizedNodeId, items);
        }

done:
        if (!found)
        {
            await Task.CompletedTask;
            return null;
        }

        await Task.CompletedTask;
        return updated;
    }

    public async Task<bool> DeleteAsync(string nodeId, string recipeId, CancellationToken cancellationToken)
    {
        var normalizedNodeId = NormalizeNodeId(nodeId);
        var normalizedRecipeId = NormalizeRequired(recipeId, "recipeId");
        var deleted = false;

        lock (_gate)
        {
            var items = ReadNodeFile(normalizedNodeId);
            deleted = items.RemoveAll(x => string.Equals(x.RecipeId, normalizedRecipeId, StringComparison.Ordinal)) > 0;
            if (deleted)
            {
                WriteNodeFile(normalizedNodeId, items);
            }
        }

        await Task.CompletedTask;
        return deleted;
    }

    public async Task<RecipeDefinition?> GetAsync(string nodeId, string recipeId, CancellationToken cancellationToken)
    {
        var normalizedNodeId = NormalizeNodeId(nodeId);
        var normalizedRecipeId = NormalizeRequired(recipeId, "recipeId");
        var items = await ReadNodeFileAsync(normalizedNodeId, cancellationToken);
        return items.FirstOrDefault(x => string.Equals(x.RecipeId, normalizedRecipeId, StringComparison.Ordinal));
    }

    private async Task<List<RecipeDefinition>> ReadNodeFileAsync(string nodeId, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        lock (_gate)
        {
            return ReadNodeFile(nodeId);
        }
    }

    private List<RecipeDefinition> ReadNodeFile(string nodeId)
    {
        var path = BuildNodePath(nodeId);
        if (!File.Exists(path))
        {
            return [];
        }

        var text = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<RecipeDefinition>>(text, _jsonOptions) ?? [];
    }

    private void WriteNodeFile(string nodeId, List<RecipeDefinition> items)
    {
        var path = BuildNodePath(nodeId);
        var text = JsonSerializer.Serialize(items, _jsonOptions);
        File.WriteAllText(path, text);
    }

    private string BuildNodePath(string nodeId) => Path.Combine(_rootPath, $"{nodeId}.json");

    private static string NormalizeNodeId(string nodeId) => NormalizeRequired(nodeId, "nodeId");

    private static string NormalizeRequired(string value, string fieldName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException($"{fieldName} is required");
        }
        return normalized;
    }

    private static string NormalizeOptional(string value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static IReadOnlyList<string> NormalizeArgs(List<string>? args) => (args ?? []).Select(x => x ?? string.Empty).ToList();

    private static IReadOnlyDictionary<string, string> NormalizeEnv(Dictionary<string, string>? env)
    {
        return (env ?? []).ToDictionary(
            x => (x.Key ?? string.Empty).Trim(),
            x => x.Value ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static string NormalizeRunner(string runner)
    {
        var normalized = NormalizeOptional(runner, "managed_job");
        return normalized switch
        {
            "managed_job" => normalized,
            "interactive_terminal" => normalized,
            _ => throw new InvalidOperationException("default_runner must be managed_job or interactive_terminal")
        };
    }
}
