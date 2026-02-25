using System.Text.Json;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class ProfileService
{
    private readonly string _storeFile;
    private readonly Dictionary<string, ProfileRecord> _profiles = new(StringComparer.Ordinal);

    public ProfileService(string storeFile)
    {
        _storeFile = (storeFile ?? string.Empty).Trim();
        SeedBuiltins();
        LoadCustomProfiles();
    }

    public IReadOnlyList<ProfileRecord> List()
    {
        return _profiles.Values
            .Select(Clone)
            .OrderByDescending(x => x.IsBuiltin)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();
    }

    public ProfileRecord Create(CreateProfileRequest input)
    {
        var normalized = Normalize(input);
        ValidateProfile(normalized, true);
        _profiles.Add(normalized.ProfileId, normalized);
        Persist();
        return Clone(normalized);
    }

    public ProfileRecord Update(string profileId, UpdateProfileRequest updates)
    {
        if (!_profiles.TryGetValue(profileId, out var current))
        {
            throw new InvalidOperationException($"profile not found: {profileId}");
        }

        if (current.IsBuiltin)
        {
            throw new InvalidOperationException("builtin profile is read-only");
        }

        var merged = Normalize(updates, profileId, current);
        ValidateProfile(merged, false);
        _profiles[profileId] = merged;
        Persist();
        return Clone(merged);
    }

    public object Delete(string profileId)
    {
        if (!_profiles.TryGetValue(profileId, out var current))
        {
            throw new InvalidOperationException($"profile not found: {profileId}");
        }

        if (current.IsBuiltin)
        {
            throw new InvalidOperationException("builtin profile cannot be deleted");
        }

        _profiles.Remove(profileId);
        Persist();
        return new { ok = true };
    }

    public ProfileRecord? Get(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        return _profiles.TryGetValue(profileId.Trim(), out var profile) ? Clone(profile) : null;
    }

    private void SeedBuiltins()
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        AddBuiltin("builtin-bash", "bash", "custom", "/bin/bash", "/tmp", "terminal", "#1ea7a4", []);
        AddBuiltin("builtin-codex", "codex", "codex", "codex", "/tmp", "bot", "#3a90e5", []);
        AddBuiltin("builtin-mcp-tools", "mcp-tools", "custom", "/bin/bash", "/workspace/tools/mcp", "tool", "#ff9f1a", ["pwd"]);
        AddBuiltin("builtin-skills-runner", "skills-runner", "custom", "/bin/bash", "/workspace/skills", "book", "#9cdb43", ["pwd"]);

        void AddBuiltin(string id, string name, string cliType, string shell, string cwd, string icon, string color, List<string> startupCommands)
        {
            _profiles[id] = new ProfileRecord
            {
                ProfileId = id,
                Name = name,
                CliType = cliType,
                Shell = shell,
                Cwd = cwd,
                StartupCommands = startupCommands,
                Icon = icon,
                Color = color,
                IsBuiltin = true,
                CreatedAt = now,
                UpdatedAt = now
            };
        }
    }

    private void LoadCustomProfiles()
    {
        if (string.IsNullOrWhiteSpace(_storeFile) || !File.Exists(_storeFile))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<ProfileRecord>>(File.ReadAllText(_storeFile));
            if (parsed is null)
            {
                return;
            }

            foreach (var profile in parsed)
            {
                if (string.IsNullOrWhiteSpace(profile.ProfileId) || _profiles.ContainsKey(profile.ProfileId))
                {
                    continue;
                }

                profile.IsBuiltin = false;
                _profiles[profile.ProfileId] = profile;
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

        var rows = _profiles.Values.Where(x => !x.IsBuiltin).Select(Clone).ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storeFile) ?? ".");
            File.WriteAllText(_storeFile, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static ProfileRecord Normalize(CreateProfileRequest input, string? fixedProfileId = null, ProfileRecord? fallback = null)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var result = new ProfileRecord
        {
            ProfileId = fixedProfileId ?? (input.ProfileId?.Trim() ?? Guid.NewGuid().ToString()),
            Name = (input.Name ?? fallback?.Name ?? string.Empty).Trim(),
            CliType = (input.CliType ?? fallback?.CliType ?? "custom").Trim(),
            Shell = (input.Shell ?? fallback?.Shell ?? string.Empty).Trim(),
            Cwd = (input.Cwd ?? fallback?.Cwd ?? string.Empty).Trim(),
            Args = NormalizeStrings(input.Args ?? fallback?.Args ?? []),
            Env = new Dictionary<string, string>(input.Env ?? fallback?.Env ?? [], StringComparer.Ordinal),
            StartupCommands = NormalizeStrings(input.StartupCommands ?? fallback?.StartupCommands ?? []),
            QuickCommands = NormalizeQuickCommands(input.QuickCommands ?? fallback?.QuickCommands ?? []),
            CliOptions = new Dictionary<string, object>(input.CliOptions ?? fallback?.CliOptions ?? [], StringComparer.Ordinal),
            Icon = (input.Icon ?? fallback?.Icon ?? string.Empty).Trim(),
            Color = (input.Color ?? fallback?.Color ?? string.Empty).Trim(),
            IsBuiltin = fallback?.IsBuiltin ?? false,
            CreatedAt = fallback?.CreatedAt ?? now,
            UpdatedAt = now
        };

        return result;
    }

    private static void ValidateProfile(ProfileRecord profile, bool checkExists)
    {
        if (string.IsNullOrWhiteSpace(profile.ProfileId))
        {
            throw new InvalidOperationException("profileId is required");
        }
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("profile name is required");
        }
        if (string.IsNullOrWhiteSpace(profile.Shell))
        {
            throw new InvalidOperationException("profile shell is required");
        }
        if (string.IsNullOrWhiteSpace(profile.Cwd))
        {
            throw new InvalidOperationException("profile cwd is required");
        }
    }

    private static List<string> NormalizeStrings(IEnumerable<string> items)
    {
        return items.Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).ToList();
    }

    private static List<QuickCommandItem> NormalizeQuickCommands(IEnumerable<QuickCommandItem> items)
    {
        var output = items
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
        return output;
    }

    private static ProfileRecord Clone(ProfileRecord profile)
    {
        return new ProfileRecord
        {
            ProfileId = profile.ProfileId,
            Name = profile.Name,
            CliType = profile.CliType,
            Shell = profile.Shell,
            Cwd = profile.Cwd,
            Args = [.. profile.Args],
            Env = new Dictionary<string, string>(profile.Env, StringComparer.Ordinal),
            StartupCommands = [.. profile.StartupCommands],
            QuickCommands = profile.QuickCommands.Select(x => new QuickCommandItem
            {
                Id = x.Id,
                Label = x.Label,
                Content = x.Content,
                SendMode = x.SendMode,
                Enabled = x.Enabled,
                Order = x.Order
            }).ToList(),
            CliOptions = new Dictionary<string, object>(profile.CliOptions, StringComparer.Ordinal),
            Icon = profile.Icon,
            Color = profile.Color,
            IsBuiltin = profile.IsBuiltin,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt
        };
    }
}
