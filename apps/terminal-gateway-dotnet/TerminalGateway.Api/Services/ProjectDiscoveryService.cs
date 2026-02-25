using System.Text.RegularExpressions;

namespace TerminalGateway.Api.Services;

public sealed class ProjectDiscoveryService
{
    private static readonly Regex CodexProjectRegex = new("^\\s*\\[projects\\.\"([^\"]+)\"\\]\\s*$", RegexOptions.Compiled);

    public object Discover(string? codexConfigPath, string? claudeConfigPath)
    {
        var items = new List<ProjectItem>();
        if (!string.IsNullOrWhiteSpace(codexConfigPath))
        {
            items.AddRange(ReadCodexProjects(codexConfigPath.Trim()));
        }

        var deduped = items
            .GroupBy(x => x.Path, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ToList();

        return new
        {
            items = deduped,
            meta = new
            {
                codexConfigPath = string.IsNullOrWhiteSpace(codexConfigPath) ? null : codexConfigPath,
                claudeConfigPath = string.IsNullOrWhiteSpace(claudeConfigPath) ? null : claudeConfigPath
            }
        };
    }

    private static List<ProjectItem> ReadCodexProjects(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        List<ProjectItem> items = [];
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch
        {
            return [];
        }

        foreach (var line in content.Split('\n'))
        {
            var match = CodexProjectRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var projectPath = match.Groups[1].Value.Trim();
            if (projectPath.Length == 0 || !Path.IsPathRooted(projectPath))
            {
                continue;
            }

            items.Add(new ProjectItem
            {
                Path = projectPath,
                Label = Path.GetFileName(projectPath),
                Source = "codex"
            });
        }

        return items;
    }

    private sealed class ProjectItem
    {
        public string Path { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }
}
