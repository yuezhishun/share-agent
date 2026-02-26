namespace TerminalGateway.Api.Services;

public sealed class ProjectApiService
{
    public object ListProjects(string basePath)
    {
        var root = Path.GetFullPath(basePath);
        if (!Directory.Exists(root))
        {
            return new { @base = root, items = Array.Empty<object>() };
        }

        var items = Directory.GetDirectories(root)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name!.StartsWith(".", StringComparison.Ordinal))
            .Select(name => new { name, path = Path.Combine(root, name!) })
            .OrderBy(x => x.name, StringComparer.Ordinal)
            .ToList();

        return new { @base = root, items };
    }
}
