using System.Diagnostics;
using ProcessRunner;

namespace TerminalGateway.Api.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task ProcessCommand_ExecuteAsync_UsesWorkingDirectory_AndEnvironment()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"process-runner-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var result = await new ProcessCommand("sh")
            .AddArguments("-c", "printf '%s|%s' \"$PWD\" \"$TEST_PROCESS_RUNNER\"")
            .SetWorkingDirectory(tempDir)
            .SetEnvironmentVariable("TEST_PROCESS_RUNNER", "active")
            .ExecuteAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"{tempDir}|active", result.StandardOutput);
        Assert.Equal(tempDir, result.Context.WorkingDirectory);
    }

    [Fact]
    public async Task ProcessCommand_EventHandlers_ReceiveLifecycle_Output_AndError()
    {
        var outputs = new List<string>();
        var errors = new List<string>();
        var startedPid = 0;
        var exitCode = int.MinValue;

        var result = await new ProcessCommand("sh")
            .AddArguments("-c", "printf 'alpha'; printf 'beta' 1>&2")
            .OnStarted(pid => startedPid = pid)
            .OnOutput(text => outputs.Add(text))
            .OnError(text => errors.Add(text))
            .OnExited(code => exitCode = code)
            .ExecuteAsync();

        Assert.True(startedPid > 0);
        Assert.Equal(0, exitCode);
        Assert.Equal("alpha", string.Concat(outputs));
        Assert.Equal("beta", string.Concat(errors));
        Assert.Equal("alpha", result.Context.CollectedOutput);
        Assert.Equal("beta", result.Context.CollectedError);
    }

    [Fact]
    public async Task ProcessManager_StartProcessesAsync_RespectsConcurrency_AndCollectsResults()
    {
        using var manager = new ProcessManager(maxConcurrency: 1);
        var completed = new List<string>();

        manager.ProcessCompleted += (_, args) => completed.Add(args.ProcessId);

        var first = manager.RegisterProcess(
            new ProcessCommand("sh")
                .AddArguments("-c", "sleep 0.2; printf first")
                .SetTimeout(TimeSpan.FromSeconds(5)));
        var second = manager.RegisterProcess(
            new ProcessCommand("sh")
                .AddArguments("-c", "sleep 0.2; printf second")
                .SetTimeout(TimeSpan.FromSeconds(5)));

        var stopwatch = Stopwatch.StartNew();
        await manager.StartProcessesAsync(new[] { first, second });
        var results = await manager.WaitAllProcessesAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(350), $"expected serialized execution, actual: {stopwatch.Elapsed}");
        Assert.Equal(ProcessManager.ProcessStatus.Completed, manager.GetProcessStatus(first));
        Assert.Equal(ProcessManager.ProcessStatus.Completed, manager.GetProcessStatus(second));
        Assert.Equal(2, completed.Count);
        Assert.Equal("first", results[first]!.StandardOutput);
        Assert.Equal("second", results[second]!.StandardOutput);
        Assert.Contains(manager.GetAllOutput(), record => record.ProcessId == first && record.Content.Contains("first", StringComparison.Ordinal));
        Assert.Contains(manager.GetAllOutput(), record => record.ProcessId == second && record.Content.Contains("second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PipeableProcessCommand_ExecuteAsync_PipesOutput()
    {
        var result = await new ProcessCommand("sh")
            .AddArguments("-c", "printf 'alpha\\nbeta\\n'")
            .PipeTo(new ProcessCommand("wc").AddArguments("-l"))
            .ExecuteAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task ProcessCommand_TryUpdateTimeout_ExtendsRunningCommand()
    {
        var timeoutUpdated = false;
        var command = new ProcessCommand("sh")
            .AddArguments("-c", "printf ready; sleep 0.3; printf done")
            .SetTimeout(TimeSpan.FromMilliseconds(120));

        command.OnOutput(text =>
        {
            if (text.Contains("ready", StringComparison.Ordinal))
            {
                timeoutUpdated = command.TryUpdateTimeout(TimeSpan.FromMilliseconds(600));
            }
        });

        var result = await command.ExecuteAsync();

        Assert.True(timeoutUpdated);
        Assert.Equal("readydone", result.StandardOutput);
    }

    [Fact]
    public async Task ProcessCommand_Timeout_ThrowsTimeoutException_AndMarksContext()
    {
        string? timeoutMessage = null;
        var command = new ProcessCommand("sh")
            .AddArguments("-c", "sleep 0.3")
            .SetTimeout(TimeSpan.FromMilliseconds(100))
            .OnTimeout(message => timeoutMessage = message);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => command.ExecuteAsync());

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(timeoutMessage);
        Assert.True(command.Context.IsTimedOut);
        Assert.True(command.Context.IsExited);
        Assert.Equal(-1, command.Context.ExitCode);
    }
}
