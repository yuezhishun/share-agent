namespace TerminalGateway.Api.Services;

public sealed class FsBrowserService
{
    public object ListDirectories(string? inputPath, IReadOnlyList<string> allowedRoots)
    {
        var requestedPath = (inputPath ?? string.Empty).Trim();
        if (requestedPath.Length == 0 || !Path.IsPathRooted(requestedPath))
        {
            throw new InvalidOperationException("path must be an absolute path");
        }

        var normalizedRoots = allowedRoots
            .Where(x => !string.IsNullOrWhiteSpace(x) && Path.IsPathRooted(x))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalizedRoots.Count == 0)
        {
            throw new InvalidOperationException("fs browser allowedRoots is empty");
        }

        var full = Path.GetFullPath(requestedPath);
        if (!Directory.Exists(full))
        {
            throw new InvalidOperationException("path does not exist");
        }

        if (!IsUnderAllowedRoots(full, normalizedRoots))
        {
            throw new InvalidOperationException("path is outside allowed roots");
        }

        var rows = new List<object>();
        foreach (var childDir in Directory.GetDirectories(full))
        {
            if (!IsUnderAllowedRoots(childDir, normalizedRoots))
            {
                continue;
            }

            rows.Add(new
            {
                name = Path.GetFileName(childDir),
                path = childDir,
                hasChildren = HasDirectoryChild(childDir, normalizedRoots)
            });
        }

        return new
        {
            path = full,
            items = rows.OrderBy(x => (string)x.GetType().GetProperty("name")!.GetValue(x)!).ToList()
        };
    }

    private static bool HasDirectoryChild(string path, List<string> roots)
    {
        try
        {
            foreach (var child in Directory.GetDirectories(path))
            {
                if (IsUnderAllowedRoots(child, roots))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsUnderAllowedRoots(string path, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (path.Equals(root, StringComparison.Ordinal))
            {
                return true;
            }

            if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
