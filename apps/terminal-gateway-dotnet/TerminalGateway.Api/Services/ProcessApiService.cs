using ProcessRunner;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class ProcessApiService : IDisposable
{
    private readonly ProcessManager _manager;
    private readonly string _filesBasePath;

    public ProcessApiService(GatewayOptions options)
    {
        _filesBasePath = Path.GetFullPath(options.FilesBasePath);
        _manager = new ProcessManager(Math.Max(1, options.ProcessManagerMaxConcurrency));
    }

    public async Task<object> RunAsync(RunProcessRequest request, CancellationToken cancellationToken)
    {
        var command = BuildCommandChain(request);
        var result = await command.ExecuteAsync(cancellationToken);
        return SerializeResult(result);
    }

    public async Task<object> StartManagedAsync(RunProcessRequest request, CancellationToken cancellationToken)
    {
        var command = BuildCommandChain(request);
        var processId = _manager.RegisterProcess(command, metadata: request.Metadata);
        await _manager.StartProcessAsync(processId, cancellationToken);
        return new
        {
            processId,
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

    private ProcessCommand BuildCommand(ProcessCommandSpec spec, RunProcessRequest request)
    {
        var file = (spec.File ?? string.Empty).Trim();
        if (file.Length == 0)
        {
            throw new InvalidOperationException("file is required");
        }

        var command = new ProcessCommand(file)
            .AddArguments(spec.Args ?? []);

        var cwd = ResolveWithinBase(request.Cwd);
        command.SetWorkingDirectory(cwd);

        foreach (var kv in request.Env ?? [])
        {
            command.SetEnvironmentVariable(kv.Key, kv.Value);
        }

        if (!string.IsNullOrEmpty(request.Stdin))
        {
            command.WithStandardInput(request.Stdin);
        }

        if (request.TimeoutMs is > 0)
        {
            command.SetTimeout(TimeSpan.FromMilliseconds(request.TimeoutMs.Value));
        }

        if (request.AllowNonZeroExitCode == true)
        {
            command.WithValidation(CommandResultValidation.None);
        }

        return command;
    }

    private ProcessCommand BuildCommandChain(RunProcessRequest request)
    {
        var specs = new List<ProcessCommandSpec>();
        if (!string.IsNullOrWhiteSpace(request.File))
        {
            specs.Add(new ProcessCommandSpec
            {
                File = request.File,
                Args = request.Args
            });
        }

        if (request.Pipeline is { Count: > 0 })
        {
            specs.AddRange(request.Pipeline);
        }

        if (specs.Count == 0)
        {
            throw new InvalidOperationException("at least one command is required");
        }

        var first = BuildCommand(specs[0], request);
        if (specs.Count == 1)
        {
            return first;
        }

        var chain = new PipeableProcessCommand(first);
        for (var i = 1; i < specs.Count; i++)
        {
            chain = chain.PipeTo(BuildCommand(specs[i], request));
        }

        return new PipedProcessCommand(chain);
    }

    private string ResolveWithinBase(string? inputPath)
    {
        var candidate = string.IsNullOrWhiteSpace(inputPath) ? _filesBasePath : Path.GetFullPath(inputPath.Trim());
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
        return new
        {
            processId = info.Id,
            status = info.Status.ToString().ToLowerInvariant(),
            startTime = info.StartTime,
            endTime = info.EndTime,
            durationMs = (long)info.Duration.TotalMilliseconds,
            command = info.Command,
            outputCount = info.OutputCount,
            metadata = info.Metadata,
            result = info.Result is null ? null : SerializeResult(info.Result)
        };
    }

    public void Dispose()
    {
        _manager.Dispose();
    }
}
