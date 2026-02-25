using System.Text.Json;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class SettingsService
{
    private readonly string _storeFile;
    private readonly object _sync = new();
    private List<QuickCommandItem> _globalQuickCommands = [];
    private List<string> _fsAllowedRoots;

    public SettingsService(string storeFile, IEnumerable<string> defaultFsAllowedRoots)
    {
        _storeFile = (storeFile ?? string.Empty).Trim();
        _fsAllowedRoots = NormalizeRoots(defaultFsAllowedRoots);
        Load();
    }

    public IReadOnlyList<QuickCommandItem> GetGlobalQuickCommands()
    {
        lock (_sync)
        {
            return _globalQuickCommands.Select(x => new QuickCommandItem
            {
                Id = x.Id,
                Label = x.Label,
                Content = x.Content,
                SendMode = x.SendMode,
                Enabled = x.Enabled,
                Order = x.Order
            }).ToList();
        }
    }

    public IReadOnlyList<QuickCommandItem> SetGlobalQuickCommands(IEnumerable<QuickCommandItem> items)
    {
        lock (_sync)
        {
            _globalQuickCommands = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Content))
                .Select((x, i) => new QuickCommandItem
                {
                    Id = string.IsNullOrWhiteSpace(x.Id) ? Guid.NewGuid().ToString("N") : x.Id,
                    Label = string.IsNullOrWhiteSpace(x.Label) ? x.Content.Trim() : x.Label,
                    Content = x.Content.Trim(),
                    SendMode = x.SendMode is "auto" or "enter" or "raw" ? x.SendMode : "auto",
                    Enabled = x.Enabled,
                    Order = i
                })
                .ToList();
            Persist();
            return GetGlobalQuickCommands();
        }
    }

    public IReadOnlyList<string> GetFsAllowedRoots()
    {
        lock (_sync)
        {
            return [.. _fsAllowedRoots];
        }
    }

    public IReadOnlyList<string> SetFsAllowedRoots(IEnumerable<string> items)
    {
        lock (_sync)
        {
            var normalized = NormalizeRoots(items);
            if (normalized.Count == 0)
            {
                throw new InvalidOperationException("fs allowed roots must not be empty");
            }

            _fsAllowedRoots = normalized;
            Persist();
            return GetFsAllowedRoots();
        }
    }

    private void Load()
    {
        if (string.IsNullOrWhiteSpace(_storeFile) || !File.Exists(_storeFile))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(_storeFile));
            if (parsed is null)
            {
                return;
            }

            _globalQuickCommands = parsed.GlobalQuickCommands ?? [];
            var roots = NormalizeRoots(parsed.FsAllowedRoots ?? []);
            if (roots.Count > 0)
            {
                _fsAllowedRoots = roots;
            }
        }
        catch
        {
        }
    }

    private void Persist()
    {
        if (string.IsNullOrWhiteSpace(_storeFile))
        {
            return;
        }

        var data = new SettingsStore
        {
            GlobalQuickCommands = _globalQuickCommands,
            FsAllowedRoots = _fsAllowedRoots
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storeFile) ?? ".");
            File.WriteAllText(_storeFile, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static List<string> NormalizeRoots(IEnumerable<string> roots)
    {
        return roots
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0 && Path.IsPathRooted(x))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private sealed class SettingsStore
    {
        public List<QuickCommandItem>? GlobalQuickCommands { get; set; }
        public List<string>? FsAllowedRoots { get; set; }
    }
}
