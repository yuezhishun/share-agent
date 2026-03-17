using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ProcessRunner;

/// <summary>
/// 可管道化的进程命令，支持链式管道操作
/// </summary>
public class PipeableProcessCommand
{
    /// <summary>
    /// 当前命令
    /// </summary>
    public ProcessCommand Command { get; }

    /// <summary>
    /// 前一个管道命令
    /// </summary>
    public PipeableProcessCommand? Previous { get; }

    /// <summary>
    /// 创建可管道化命令
    /// </summary>
    /// <param name="command">当前命令</param>
    /// <param name="previous">前一个管道命令</param>
    public PipeableProcessCommand(ProcessCommand command, PipeableProcessCommand? previous = null)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Previous = previous;
    }

    /// <summary>
    /// 重载 | 操作符，连接两个命令
    /// </summary>
    public static PipeableProcessCommand operator |(PipeableProcessCommand left, ProcessCommand right)
        => new(right, left);

    /// <summary>
    /// 收集管道链中的所有命令
    /// </summary>
    public List<ProcessCommand> CollectCommands()
    {
        var commands = new List<ProcessCommand>();
        var current = this;
        while (current != null)
        {
            commands.Insert(0, current.Command);
            current = current.Previous;
        }
        return commands;
    }

    /// <summary>
    /// 执行管道链
    /// </summary>
    public async Task<ProcessResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(Encoding.Default, cancellationToken);
    }

    /// <summary>
    /// 执行管道链
    /// </summary>
    public async Task<ProcessResult> ExecuteAsync(Encoding encoding, CancellationToken cancellationToken = default)
    {
        var commands = CollectCommands();
        var executor = new MultiPipedProcessExecutor(commands, encoding);
        return await executor.ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// 以流式方式监听管道执行事件
    /// </summary>
    public IAsyncEnumerable<ProcessCommandEvent> ListenAsync(CancellationToken cancellationToken = default)
    {
        return ListenAsync(Encoding.Default, cancellationToken);
    }

    /// <summary>
    /// 以流式方式监听管道执行事件
    /// </summary>
    public IAsyncEnumerable<ProcessCommandEvent> ListenAsync(Encoding encoding, CancellationToken cancellationToken = default)
    {
        var commands = CollectCommands();
        var executor = new MultiPipedProcessExecutor(commands, encoding);
        return executor.ListenAsync(cancellationToken);
    }

    /// <summary>
    /// 将管道连接到另一个命令
    /// </summary>
    public PipeableProcessCommand PipeTo(ProcessCommand target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        return new PipeableProcessCommand(target, this);
    }
}

/// <summary>
/// 多层管道执行器
/// </summary>
internal class MultiPipedProcessExecutor
{
    private readonly List<ProcessCommand> _commands;
    private readonly Encoding _encoding;

    public MultiPipedProcessExecutor(List<ProcessCommand> commands, Encoding encoding)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        if (_commands.Count == 0) throw new ArgumentException("At least one command is required", nameof(commands));
        _encoding = encoding ?? Encoding.Default;
    }

    /// <summary>
    /// 执行管道链
    /// </summary>
    public async Task<ProcessResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 使用第一个命令的超时时间
        var firstCommand = _commands[0];
        if (firstCommand.Timeout != Timeout.InfiniteTimeSpan)
            cts.CancelAfter(firstCommand.Timeout);

        // 创建所有进程
        var processes = new List<Process>();
        foreach (var cmd in _commands)
        {
            processes.Add(CreateProcess(cmd));
        }

        try
        {
            // 启动所有进程
            for (int i = 0; i < processes.Count; i++)
            {
                processes[i].Start();
                _commands[i].Context.MarkStarted(processes[i].Id);
            }

            // 处理第一个命令的输入
            var firstProcess = processes[0];
            if (firstCommand.StandardInputPipe != null)
            {
                await WritePipeSourceAsync(firstProcess.StandardInput.BaseStream, firstCommand.StandardInputPipe, cts.Token);
            }
            else if (firstCommand.StandardInputStream != null)
            {
                await WriteInputStreamAsync(firstProcess.StandardInput, firstCommand.StandardInputStream, cts.Token);
            }
            else if (firstCommand.StandardInput != null)
            {
                await WriteInputAsync(firstProcess.StandardInput, firstCommand.StandardInput, cts.Token);
            }

            // 创建管道连接任务
            var pipeTasks = new List<Task>();
            for (int i = 0; i < processes.Count - 1; i++)
            {
                pipeTasks.Add(PipeOutputToTargetAsync(
                    processes[i].StandardOutput,
                    processes[i + 1].StandardInput,
                    cts.Token));
            }

            // 读取最后一个进程的输出
            Task<string> outputTask;
            var lastCommand = _commands[^1];

            if (lastCommand.StandardOutputTarget != null)
            {
                outputTask = ReadOutputToTargetAsync(processes[^1].StandardOutput.BaseStream, lastCommand.StandardOutputTarget, cts.Token);
            }
            else
            {
                outputTask = ReadOutputAsync(processes[^1].StandardOutput, cts.Token);
            }

            // 读取错误输出（支持 PipeTarget）
            var errorTasks = new List<Task<string>>();
            for (int i = 0; i < processes.Count; i++)
            {
                if (_commands[i].StandardErrorTarget != null)
                {
                    errorTasks.Add(ReadOutputToTargetAsync(processes[i].StandardError.BaseStream, _commands[i].StandardErrorTarget!, cts.Token));
                }
                else
                {
                    errorTasks.Add(ReadErrorAsync(processes[i].StandardError, _commands[i], cts.Token));
                }
            }

            // 等待所有管道完成
            await Task.WhenAll(pipeTasks);

            // 等待所有进程退出
            foreach (var process in processes)
            {
                await process.WaitForExitAsync(cts.Token);
            }

            // 收集结果
            var lastProcess = processes[^1];
            var output = await outputTask;
            var errors = await Task.WhenAll(errorTasks);

            // 构建组合错误输出
            var combinedError = string.Join("\n", errors);

            // 更新所有命令的上下文
            for (int i = 0; i < processes.Count; i++)
            {
                _commands[i].Context.MarkExited(processes[i].ExitCode);
            }

            // 生成结果（使用最后一个进程的结果）
            var result = new ProcessResult
            {
                ProcessId = lastProcess.Id,
                ExitCode = lastProcess.ExitCode,
                StandardOutput = output,
                StandardError = combinedError,
                CompletionTime = DateTime.UtcNow,
                Context = _commands[^1].Context.CreateSnapshot()
            };

            // 验证退出码
            ValidateExitCode(result);

            return result;
        }
        catch (OperationCanceledException)
        {
            foreach (var process in processes)
            {
                try { process.Kill(); } catch { }
            }
            throw;
        }
    }

    /// <summary>
    /// 以流式方式监听管道执行事件
    /// </summary>
    public async IAsyncEnumerable<ProcessCommandEvent> ListenAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<ProcessCommandEvent>();
        var writer = channel.Writer;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 使用第一个命令的超时时间
        var firstCommand = _commands[0];
        if (firstCommand.Timeout != Timeout.InfiniteTimeSpan)
            cts.CancelAfter(firstCommand.Timeout);

        // 创建所有进程
        var processes = new List<Process>();
        foreach (var cmd in _commands)
        {
            processes.Add(CreateProcess(cmd));
        }

        // 启动执行
        _ = Task.Run(async () =>
        {
            try
            {
                // 启动所有进程
                for (int i = 0; i < processes.Count; i++)
                {
                    processes[i].Start();
                    _commands[i].Context.MarkStarted(processes[i].Id);
                    await writer.WriteAsync(new ProcessStartedEvent
                    {
                        ProcessId = processes[i].Id,
                        Context = _commands[i].Context.CreateSnapshot()
                    }, cts.Token);
                }

                // 处理第一个命令的输入
                var firstProcess = processes[0];
                if (firstCommand.StandardInputPipe != null)
                {
                    await WritePipeSourceAsync(firstProcess.StandardInput.BaseStream, firstCommand.StandardInputPipe, cts.Token);
                }
                else if (firstCommand.StandardInputStream != null)
                {
                    await WriteInputStreamAsync(firstProcess.StandardInput, firstCommand.StandardInputStream, cts.Token);
                }
                else if (firstCommand.StandardInput != null)
                {
                    await WriteInputAsync(firstProcess.StandardInput, firstCommand.StandardInput, cts.Token);
                }

                // 创建管道连接任务
                var pipeTasks = new List<Task>();
                for (int i = 0; i < processes.Count - 1; i++)
                {
                    pipeTasks.Add(PipeOutputToTargetAsync(
                        processes[i].StandardOutput,
                        processes[i + 1].StandardInput,
                        cts.Token));
                }

                // 读取错误输出（非流式，后台收集）
                var errorTasks = new List<Task>();
                for (int i = 0; i < processes.Count; i++)
                {
                    errorTasks.Add(ReadErrorStreamAsync(processes[i].StandardError, _commands[i], writer, cts.Token));
                }

                // 读取最后一个进程的输出（流式）
                var outputTask = ReadOutputStreamAsync(processes[^1].StandardOutput, writer, cts.Token);

                // 等待所有管道完成
                await Task.WhenAll(pipeTasks);

                // 等待所有进程退出
                foreach (var process in processes)
                {
                    await process.WaitForExitAsync(cts.Token);
                }

                // 等待读取完成
                await Task.WhenAll(errorTasks);
                await outputTask;

                // 发送退出事件
                for (int i = 0; i < processes.Count; i++)
                {
                    _commands[i].Context.MarkExited(processes[i].ExitCode);
                    await writer.WriteAsync(new ProcessExitedEvent
                    {
                        ExitCode = processes[i].ExitCode,
                        Context = _commands[i].Context.CreateSnapshot()
                    }, cts.Token);
                }

                writer.Complete();
            }
            catch (OperationCanceledException ex)
            {
                if (!cancellationToken.IsCancellationRequested && firstCommand.Timeout != Timeout.InfiniteTimeSpan)
                {
                    // 超时
                    foreach (var process in processes)
                    {
                        try { process.Kill(); } catch { }
                    }
                    _commands[^1].Context.MarkTimedOut();
                    var timeoutMessage = $"Command timed out after {firstCommand.Timeout.TotalSeconds} seconds";
                    await writer.WriteAsync(new ProcessTimeoutEvent
                    {
                        Message = timeoutMessage,
                        Context = _commands[^1].Context.CreateSnapshot()
                    }, CancellationToken.None);
                }
                writer.Complete(ex);
            }
            catch (Exception ex)
            {
                foreach (var process in processes)
                {
                    try { process.Kill(); } catch { }
                }
                writer.Complete(ex);
            }
        }, cts.Token);

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// 创建 Process 实例
    /// </summary>
    private Process CreateProcess(ProcessCommand command)
    {
        var executablePath = command.ResolvedTarget ?? command.Target;

        var redirectInput = command.StandardInput != null ||
                           command.StandardInputStream != null ||
                           command.StandardInputPipe != null ||
                           command != _commands[0]; // 非第一个命令需要输入重定向

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = command.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = redirectInput,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        // 设置启动参数
        for (int i = 0; i < command.Arguments.Count; i++)
        {
            process.StartInfo.ArgumentList.Add(command.Arguments[i]);
        }

        // 设置环境变量
        foreach (var kvp in command.EnvironmentVariables)
        {
            process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
        }

        return process;
    }

    /// <summary>
    /// 将源输出转发到目标输入
    /// </summary>
    private async Task PipeOutputToTargetAsync(StreamReader source, StreamWriter target, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            int charsRead = await source.ReadAsync(buffer, 0, buffer.Length);
            if (charsRead == 0) break;

            await target.WriteAsync(buffer, 0, charsRead);
            await target.FlushAsync();
        }

        target.Close();
    }

    /// <summary>
    /// 读取标准输出
    /// </summary>
    private async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (charsRead == 0) break;

            var text = new string(buffer, 0, charsRead);
            output.Append(text);
        }

        return output.ToString();
    }

    /// <summary>
    /// 读取标准错误
    /// </summary>
    private async Task<string> ReadErrorAsync(StreamReader reader, ProcessCommand command, CancellationToken cancellationToken)
    {
        var error = new StringBuilder();
        var buffer = new char[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (charsRead == 0) break;

            var text = new string(buffer, 0, charsRead);
            error.Append(text);
            command.Context.AppendError(text);
        }

        return error.ToString();
    }

    /// <summary>
    /// 将输出读取到 PipeTarget
    /// </summary>
    private async Task<string> ReadOutputToTargetAsync(Stream source, PipeTarget target, CancellationToken cancellationToken)
    {
        try
        {
            await target.CopyFromAsync(source, cancellationToken);
            return string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Read output to target error: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 读取标准输出（流式）
    /// </summary>
    private async Task ReadOutputStreamAsync(StreamReader reader, ChannelWriter<ProcessCommandEvent> writer, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (charsRead == 0) break;

            var text = new string(buffer, 0, charsRead);
            _commands[^1].Context.AppendOutput(text);
            await writer.WriteAsync(new StandardOutputEvent
            {
                Text = text,
                Context = _commands[^1].Context.CreateSnapshot()
            }, cancellationToken);
        }
    }

    /// <summary>
    /// 读取标准错误（流式）
    /// </summary>
    private async Task ReadErrorStreamAsync(StreamReader reader, ProcessCommand command, ChannelWriter<ProcessCommandEvent> writer, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (charsRead == 0) break;

            var text = new string(buffer, 0, charsRead);
            command.Context.AppendError(text);
            await writer.WriteAsync(new StandardErrorEvent
            {
                Text = text,
                Context = command.Context.CreateSnapshot()
            }, cancellationToken);
        }
    }

    /// <summary>
    /// 写入标准输入流
    /// </summary>
    private async Task WriteInputStreamAsync(StreamWriter writer, Stream inputStream, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(inputStream, leaveOpen: true);
            var buffer = new char[4096];

            while (!cancellationToken.IsCancellationRequested)
            {
                int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (charsRead == 0) break;

                await writer.WriteAsync(buffer, 0, charsRead);
            }

            await writer.FlushAsync();
            writer.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Write input stream error: {ex.Message}");
        }
    }

    /// <summary>
    /// 写入标准输入
    /// </summary>
    private async Task WriteInputAsync(StreamWriter writer, string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(input))
            return;

        try
        {
            await writer.WriteAsync(input);
            await writer.FlushAsync();
            writer.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Write input error: {ex.Message}");
        }
    }

    /// <summary>
    /// 写入管道源到标准输入
    /// </summary>
    private async Task WritePipeSourceAsync(Stream stream, PipeSource source, CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(stream, cancellationToken);
            stream.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Write pipe source error: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证退出码
    /// </summary>
    private void ValidateExitCode(ProcessResult result)
    {
        var lastCommand = _commands[^1];
        if (lastCommand.Validation == CommandResultValidation.None)
            return;

        if (lastCommand.Validation == CommandResultValidation.ZeroExitCode && result.ExitCode != 0)
        {
            throw new ProcessExecutionException(
                $"Piped command execution failed with exit code {result.ExitCode}. " +
                $"Target: {lastCommand.GetFullCommandLine()}",
                result);
        }
    }
}
