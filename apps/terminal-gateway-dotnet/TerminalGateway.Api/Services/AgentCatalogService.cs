using System.Diagnostics;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class AgentCatalogService
{
    private static readonly IReadOnlyDictionary<string, AgentBackendDescriptor> Backends =
        new Dictionary<string, AgentBackendDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude"] = Create("claude", "Claude Code", "claude", ["--experimental-acp"], authRequired: true),
            ["gemini"] = Create("gemini", "Google CLI", "gemini", ["--experimental-acp"], authRequired: true),
            ["qwen"] = Create("qwen", "Qwen Code", "npx", ["@qwen-code/qwen-code", "--acp"], authRequired: true),
            ["iflow"] = Create("iflow", "iFlow CLI", "iflow", ["--experimental-acp"], authRequired: true),
            ["codex"] = Create("codex", "Codex", "npx", ["@zed-industries/codex-acp@0.9.5"], authRequired: true),
            ["codebuddy"] = Create("codebuddy", "CodeBuddy", "npx", ["@tencent-ai/codebuddy-code", "--acp"], authRequired: true),
            ["goose"] = Create("goose", "Goose", "goose", ["acp"]),
            ["auggie"] = Create("auggie", "Augment Code", "auggie", ["--acp"]),
            ["kimi"] = Create("kimi", "Kimi CLI", "kimi", ["acp"]),
            ["opencode"] = Create("opencode", "OpenCode", "opencode", ["acp"]),
            ["droid"] = Create("droid", "Factory Droid", "droid", ["exec", "--output-format", "acp"]),
            ["copilot"] = Create("copilot", "GitHub Copilot", "copilot", ["--acp", "--stdio"]),
            ["qoder"] = Create("qoder", "Qoder CLI", "qodercli", ["--acp"]),
            ["vibe"] = Create("vibe", "Mistral Vibe", "vibe-acp", []),
            ["cursor"] = Create("cursor", "Cursor Agent", "agent", ["acp"], authRequired: true),
            ["custom"] = new AgentBackendDescriptor
            {
                Backend = "custom",
                Name = "Custom Agent",
                Enabled = true,
                SupportsStreaming = true,
                RequiresCustomTransport = false
            },
            ["openclaw-gateway"] = new AgentBackendDescriptor
            {
                Backend = "openclaw-gateway",
                Name = "OpenClaw",
                CliCommand = "openclaw",
                Enabled = true,
                SupportsStreaming = true,
                RequiresCustomTransport = true
            },
            ["remote"] = new AgentBackendDescriptor
            {
                Backend = "remote",
                Name = "Remote Agent",
                Enabled = true,
                SupportsStreaming = true,
                RequiresCustomTransport = true
            }
        };

    public IReadOnlyList<AgentBackendDescriptor> List() => Backends.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();

    public bool TryGet(string backend, out AgentBackendDescriptor descriptor) => Backends.TryGetValue((backend ?? string.Empty).Trim(), out descriptor!);

    public AgentHealthResult CheckHealth(string backend, string? cliPathOverride = null)
    {
        if (!TryGet(backend, out var descriptor))
        {
            return new AgentHealthResult
            {
                Backend = backend,
                Available = false,
                Message = "unsupported backend"
            };
        }

        if (descriptor.RequiresCustomTransport)
        {
            return new AgentHealthResult
            {
                Backend = descriptor.Backend,
                Available = false,
                Message = "backend requires a custom transport adapter and is not launched as a local ACP stdio process"
            };
        }

        var command = (cliPathOverride ?? descriptor.CliCommand ?? string.Empty).Trim();
        if (command.Length == 0)
        {
            return new AgentHealthResult
            {
                Backend = descriptor.Backend,
                Available = false,
                Message = "cli command is not configured"
            };
        }

        var resolved = ResolveExecutable(command);
        return new AgentHealthResult
        {
            Backend = descriptor.Backend,
            Available = resolved is not null,
            Message = resolved is null ? "command not found on PATH" : "ok",
            ResolvedCommand = resolved ?? command
        };
    }

    public (string fileName, IReadOnlyList<string> args) ResolveLaunch(string backend, string? cliPathOverride, IReadOnlyList<string>? extraArgs)
    {
        if (!TryGet(backend, out var descriptor))
        {
            throw new InvalidOperationException($"unsupported backend: {backend}");
        }

        if (descriptor.RequiresCustomTransport)
        {
            throw new NotSupportedException($"backend {backend} requires a custom transport adapter");
        }

        if (string.Equals(descriptor.Backend, "custom", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(cliPathOverride))
            {
                throw new InvalidOperationException("custom backend requires cliPath");
            }

            var parsed = ParseCommandLine(cliPathOverride);
            var args = parsed.args.Concat(extraArgs ?? []).ToList();
            return (parsed.fileName, args);
        }

        var fileName = cliPathOverride;
        var argsList = new List<string>();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = descriptor.CliCommand;
            argsList.AddRange(descriptor.AcpArgs);
        }
        else
        {
            var parsed = ParseCommandLine(fileName);
            fileName = parsed.fileName;
            argsList.AddRange(parsed.args);
            if (parsed.args.Count == 0)
            {
                argsList.AddRange(descriptor.AcpArgs);
            }
        }

        if (extraArgs is { Count: > 0 })
        {
            argsList.AddRange(extraArgs);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException($"backend {backend} does not define a launch command");
        }

        return (fileName, argsList);
    }

    private static AgentBackendDescriptor Create(
        string backend,
        string name,
        string cliCommand,
        IReadOnlyList<string> acpArgs,
        bool authRequired = false)
    {
        return new AgentBackendDescriptor
        {
            Backend = backend,
            Name = name,
            CliCommand = cliCommand,
            AcpArgs = acpArgs,
            Enabled = true,
            SupportsStreaming = true,
            AuthRequired = authRequired,
            RequiresCustomTransport = false
        };
    }

    private static string? ResolveExecutable(string command)
    {
        if (Path.IsPathRooted(command) && File.Exists(command))
        {
            return command;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(entry, command);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (OperatingSystem.IsWindows())
            {
                foreach (var ext in new[] { ".exe", ".cmd", ".bat" })
                {
                    var windowsCandidate = candidate + ext;
                    if (File.Exists(windowsCandidate))
                    {
                        return windowsCandidate;
                    }
                }
            }
        }

        return null;
    }

    private static (string fileName, List<string> args) ParseCommandLine(string raw)
    {
        var parts = new List<string>();
        var current = new List<char>();
        var inQuotes = false;
        foreach (var ch in raw.Trim())
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Count > 0)
                {
                    parts.Add(new string([.. current]));
                    current.Clear();
                }
                continue;
            }

            current.Add(ch);
        }

        if (current.Count > 0)
        {
            parts.Add(new string([.. current]));
        }

        if (parts.Count == 0)
        {
            throw new InvalidOperationException("command is empty");
        }

        return (parts[0], parts.Skip(1).ToList());
    }
}
