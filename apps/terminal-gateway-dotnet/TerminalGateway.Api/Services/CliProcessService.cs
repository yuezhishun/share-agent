using ProcessRunner;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class CliProcessService : IDisposable
{
    private readonly ProcessManager _manager;
    private readonly CliTemplateService _templates;
    private readonly TerminalEnvService _terminalEnvs;
    private readonly string _filesBasePath;

    public CliProcessService(GatewayOptions options, CliTemplateService templates, TerminalEnvService terminalEnvs)
    {
        _templates = templates;
        _terminalEnvs = terminalEnvs;
        _filesBasePath = Path.GetFullPath(options.FilesBasePath);
        _manager = new ProcessManager(Math.Max(1, options.ProcessManagerMaxConcurrency));
    }

    public async Task<object> StartManagedAsync(StartCliProcessRequest request, CancellationToken cancellationToken)
    {
        var template = _templates.GetRequired(request.TemplateId ?? string.Empty);
        var currentNodeOs = NodeOsHelper.Current;
        if (template.SupportedOs.Count > 0 && !template.SupportedOs.Contains(currentNodeOs, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"template does not support current node os: {currentNodeOs}");
        }
        var cwd = ResolveWithinBase(request.CwdOverride, template.DefaultCwd);
        var command = BuildCommand(template, request, cwd);
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["template_id"] = template.TemplateId,
            ["template_name"] = template.Name,
            ["cli_type"] = template.CliType,
            ["label"] = string.IsNullOrWhiteSpace(request.Label) ? template.Name : request.Label.Trim(),
            ["cwd"] = cwd,
            ["executable"] = template.Executable
        };
        var processId = _manager.RegisterProcess(command, metadata: metadata);
        await _manager.StartProcessAsync(processId, cancellationToken);
        return new
        {
            processId,
            templateId = template.TemplateId,
            templateName = template.Name,
            status = _manager.GetProcessStatus(processId).ToString().ToLowerInvariant()
        };
    }

    public IReadOnlyList<object> ListManaged()
    {
        return _manager.GetAllProcesses().Select(SerializeProcessInfo).ToList();
    }

    public object GetManaged(string processId)
    {
        return SerializeProcessInfo(_manager.GetProcessInfo(processId));
    }

    public IReadOnlyList<object> GetOutput(string processId)
    {
        return _manager.GetProcessOutput(processId)
            .Select(x => new
            {
                timestamp = x.Timestamp,
                processId = x.ProcessId,
                outputType = x.OutputType.ToString().ToLowerInvariant(),
                content = x.Content
            })
            .Cast<object>()
            .ToList();
    }

    public async Task<object> WaitManagedAsync(string processId, int? timeoutMs)
    {
        var timeout = timeoutMs is > 0 ? TimeSpan.FromMilliseconds(timeoutMs.Value) : (TimeSpan?)null;
        var result = await _manager.WaitProcessAsync(processId, timeout);
        var info = _manager.GetProcessInfo(processId);
        return new
        {
            processId,
            status = info.Status.ToString().ToLowerInvariant(),
            completed = result is not null,
            result = result is null ? null : SerializeResult(result)
        };
    }

    public async Task<object> StopManagedAsync(string processId, bool force)
    {
        await _manager.StopProcessAsync(processId, force);
        var info = _manager.GetProcessInfo(processId);
        return new
        {
            ok = true,
            processId,
            status = info.Status.ToString().ToLowerInvariant()
        };
    }

    public object RemoveManaged(string processId)
    {
        _manager.RemoveProcess(processId);
        return new
        {
            ok = true,
            processId
        };
    }

    private ProcessCommand BuildCommand(CliTemplateRecord template, StartCliProcessRequest request, string cwd)
    {
        var command = new ProcessCommand(template.Executable)
            .AddArguments([.. template.BaseArgs, .. NormalizeStrings(request.ExtraArgs ?? [])])
            .SetWorkingDirectory(cwd);

        foreach (var kv in _terminalEnvs.ResolveEnvironment(template.EnvGroupNames, template.EnvEntryIds, NodeOsHelper.Current))
        {
            command.SetEnvironmentVariable(kv.Key, kv.Value);
        }
        foreach (var kv in template.DefaultEnv)
        {
            command.SetEnvironmentVariable(kv.Key, kv.Value);
        }
        foreach (var kv in request.EnvOverrides ?? [])
        {
            var key = (kv.Key ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                continue;
            }
            command.SetEnvironmentVariable(key, kv.Value ?? string.Empty);
        }

        if (request.TimeoutMs is > 0)
        {
            command.SetTimeout(TimeSpan.FromMilliseconds(request.TimeoutMs.Value));
        }

        return command;
    }

    private string ResolveWithinBase(string? overridePath, string fallbackPath)
    {
        var candidate = string.IsNullOrWhiteSpace(overridePath)
            ? (string.IsNullOrWhiteSpace(fallbackPath) ? _filesBasePath : Path.GetFullPath(fallbackPath.Trim()))
            : Path.GetFullPath(overridePath.Trim());

        if (candidate.Equals(_filesBasePath, StringComparison.Ordinal))
        {
            return candidate;
        }

        var prefix = _filesBasePath.EndsWith(Path.DirectorySeparatorChar)
            ? _filesBasePath
            : _filesBasePath + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("cwd is outside allowed base");
        }

        return candidate;
    }

    private static List<string> NormalizeStrings(IEnumerable<string> items)
    {
        return items.Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).ToList();
    }

    private static object SerializeResult(ProcessResult result)
    {
        return new
        {
            processId = result.ProcessId,
            exitCode = result.ExitCode,
            standardOutput = result.StandardOutput,
            standardError = result.StandardError,
            completionTime = result.CompletionTime,
            durationMs = (long)result.Duration.TotalMilliseconds,
            isSuccess = result.IsSuccess,
            isTimedOut = result.IsTimedOut,
            command = result.Context.FullCommandLine,
            workingDirectory = result.Context.WorkingDirectory
        };
    }

    private static object SerializeProcessInfo(ProcessInfo info)
    {
        var metadata = new Dictionary<string, object>(info.Metadata, StringComparer.Ordinal);
        metadata.TryGetValue("template_id", out var templateId);
        metadata.TryGetValue("template_name", out var templateName);
        metadata.TryGetValue("cli_type", out var cliType);
        metadata.TryGetValue("label", out var label);

        return new
        {
            processId = info.Id,
            status = info.Status.ToString().ToLowerInvariant(),
            startTime = info.StartTime,
            endTime = info.EndTime,
            durationMs = (long)info.Duration.TotalMilliseconds,
            command = info.Command,
            templateId = templateId?.ToString(),
            templateName = templateName?.ToString(),
            cliType = cliType?.ToString(),
            label = label?.ToString(),
            outputCount = info.OutputCount,
            metadata,
            result = info.Result is null ? null : SerializeResult(info.Result)
        };
    }

    public void Dispose()
    {
        _manager.Dispose();
    }
}
