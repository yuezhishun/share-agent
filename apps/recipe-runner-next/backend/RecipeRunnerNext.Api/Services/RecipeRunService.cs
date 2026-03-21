using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using RecipeRunnerNext.Api.Models;

namespace RecipeRunnerNext.Api.Services;

public sealed class RecipeRunService
{
    private readonly RecipeCatalogService _recipes;
    private readonly ConcurrentDictionary<string, RecipeRun> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ActiveRunState> _activeRuns = new(StringComparer.Ordinal);

    public RecipeRunService(RecipeCatalogService recipes)
    {
        _recipes = recipes;
    }

    public IReadOnlyList<RecipeRun> List(string? targetNodeId, string? status)
    {
        var filtered = _runs.Values.AsEnumerable();
        var normalizedTargetNodeId = (targetNodeId ?? string.Empty).Trim();
        var normalizedStatus = (status ?? string.Empty).Trim();

        if (normalizedTargetNodeId.Length > 0)
        {
            filtered = filtered.Where(x => string.Equals(x.TargetNodeId, normalizedTargetNodeId, StringComparison.Ordinal));
        }

        if (normalizedStatus.Length > 0)
        {
            filtered = filtered.Where(x => string.Equals(x.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.OrderByDescending(x => x.StartedAt).ToList();
    }

    public RecipeRun? Get(string runId)
    {
        var normalizedRunId = (runId ?? string.Empty).Trim();
        return normalizedRunId.Length == 0 || !_runs.TryGetValue(normalizedRunId, out var item) ? null : item;
    }

    public async Task<RecipeRun> CreateAsync(CreateRunRequest request, CancellationToken cancellationToken)
    {
        var targetNodeId = NormalizeRequired(request.TargetNodeId, "target_node_id");
        var sourceNodeId = NormalizeRequired(request.SourceNodeId, "source_node_id");
        var recipeId = NormalizeRequired(request.RecipeId, "recipe_id");
        var recipe = await _recipes.GetAsync(targetNodeId, recipeId, cancellationToken);
        if (recipe is null)
        {
            throw new InvalidOperationException("recipe not found on target node");
        }

        var runnerType = ResolveRunnerType(request.Overrides?.DefaultRunner, recipe.DefaultRunner);
        var runId = $"run-{Guid.NewGuid():N}";
        var startedAt = DateTimeOffset.UtcNow;
        var initial = new RecipeRun
        {
            RunId = runId,
            RecipeId = recipe.RecipeId,
            TargetNodeId = targetNodeId,
            SourceNodeId = sourceNodeId,
            TriggerSource = "api",
            Status = "running",
            StartedAt = startedAt,
            FinishedAt = null,
            ExitCode = null,
            RunnerType = runnerType,
            RuntimeRef = runnerType == "interactive_terminal"
                ? new RuntimeRef { Kind = "terminal", Id = $"terminal-{runId}", NodeId = targetNodeId }
                : new RuntimeRef { Kind = "process", Id = $"process-{runId}", NodeId = targetNodeId },
            StdoutSummary = string.Empty,
            StderrSummary = string.Empty,
            Artifacts = [],
            Error = string.Empty,
            Stdout = string.Empty,
            Stderr = string.Empty
        };

        _runs[runId] = initial;

        if (runnerType == "interactive_terminal")
        {
            var completed = initial with
            {
                Status = "succeeded",
                FinishedAt = DateTimeOffset.UtcNow,
                ExitCode = 0,
                StdoutSummary = "terminal runtime placeholder created"
            };
            _runs[runId] = completed;
            return completed;
        }

        var active = await StartProcessAsync(runId, recipe, request.Overrides, cancellationToken);
        _activeRuns[runId] = active;
        _ = WaitForCompletionAsync(runId, active);
        return initial;
    }

    public async Task<RecipeRun?> CancelAsync(string runId, CancellationToken cancellationToken)
    {
        var normalizedRunId = NormalizeRequired(runId, "runId");
        if (!_runs.TryGetValue(normalizedRunId, out var existing))
        {
            return null;
        }

        if (_activeRuns.TryRemove(normalizedRunId, out var active))
        {
            try
            {
                active.Cancellation.Cancel();
                if (!active.Process.HasExited)
                {
                    active.Process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
            finally
            {
                active.Process.Dispose();
                active.Cancellation.Dispose();
            }

            var cancelled = existing with
            {
                Status = "cancelled",
                FinishedAt = DateTimeOffset.UtcNow,
                Error = "cancelled by request",
                Stdout = active.Stdout.ToString(),
                Stderr = active.Stderr.ToString(),
                StdoutSummary = Summarize(active.Stdout.ToString()),
                StderrSummary = Summarize(active.Stderr.ToString())
            };
            _runs[normalizedRunId] = cancelled;
            await Task.CompletedTask;
            return cancelled;
        }

        return existing;
    }

    private async Task<ActiveRunState> StartProcessAsync(string runId, RecipeDefinition recipe, RunOverrides? overrides, CancellationToken cancellationToken)
    {
        var command = NormalizeOverride(overrides?.Command, recipe.Command);
        var args = overrides?.Args?.Select(x => x ?? string.Empty).ToList() ?? recipe.Args.ToList();
        var cwd = NormalizeOverride(overrides?.Cwd, recipe.Cwd);
        var env = new Dictionary<string, string>(recipe.Env, StringComparer.Ordinal);
        if (overrides?.Env is not null)
        {
            foreach (var entry in overrides.Env)
            {
                env[entry.Key ?? string.Empty] = entry.Value ?? string.Empty;
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var entry in env)
        {
            startInfo.Environment[entry.Key] = entry.Value;
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        var state = new ActiveRunState(process, new CancellationTokenSource());
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                state.Stdout.AppendLine(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                state.Stderr.AppendLine(eventArgs.Data);
            }
        };

        if (!process.Start())
        {
            process.Dispose();
            state.Cancellation.Dispose();
            throw new InvalidOperationException("failed to start process");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await Task.CompletedTask;
        return state;
    }

    private async Task WaitForCompletionAsync(string runId, ActiveRunState active)
    {
        try
        {
            await active.Process.WaitForExitAsync(active.Cancellation.Token);
            var stdout = active.Stdout.ToString();
            var stderr = active.Stderr.ToString();
            var exitCode = active.Process.ExitCode;
            var completed = _runs[runId] with
            {
                Status = exitCode == 0 ? "succeeded" : "failed",
                FinishedAt = DateTimeOffset.UtcNow,
                ExitCode = exitCode,
                Stdout = stdout,
                Stderr = stderr,
                StdoutSummary = Summarize(stdout),
                StderrSummary = Summarize(stderr),
                Error = exitCode == 0 ? string.Empty : $"process exited with code {exitCode}"
            };
            _runs[runId] = completed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            var failed = _runs[runId] with
            {
                Status = "failed",
                FinishedAt = DateTimeOffset.UtcNow,
                Error = error.Message,
                Stdout = active.Stdout.ToString(),
                Stderr = active.Stderr.ToString(),
                StdoutSummary = Summarize(active.Stdout.ToString()),
                StderrSummary = Summarize(active.Stderr.ToString())
            };
            _runs[runId] = failed;
        }
        finally
        {
            _activeRuns.TryRemove(runId, out _);
            active.Process.Dispose();
            active.Cancellation.Dispose();
        }
    }

    private static string ResolveRunnerType(string? overrideRunner, string recipeRunner)
    {
        var normalized = (overrideRunner ?? recipeRunner).Trim();
        return normalized switch
        {
            "managed_job" => normalized,
            "interactive_terminal" => normalized,
            _ => throw new InvalidOperationException("runner_type must be managed_job or interactive_terminal")
        };
    }

    private static string NormalizeRequired(string value, string fieldName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException($"{fieldName} is required");
        }

        return normalized;
    }

    private static string NormalizeOverride(string? candidate, string fallback)
    {
        var normalized = (candidate ?? string.Empty).Trim();
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static string Summarize(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length <= 240)
        {
            return normalized;
        }

        return normalized[..240];
    }

    private sealed class ActiveRunState
    {
        public ActiveRunState(Process process, CancellationTokenSource cancellation)
        {
            Process = process;
            Cancellation = cancellation;
        }

        public Process Process { get; }
        public CancellationTokenSource Cancellation { get; }
        public StringBuilder Stdout { get; } = new();
        public StringBuilder Stderr { get; } = new();
    }
}
