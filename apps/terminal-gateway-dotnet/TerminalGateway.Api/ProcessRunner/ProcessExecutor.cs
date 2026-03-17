using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessRunner
{
    /// <summary>
    /// 基于 System.Diagnostics.Process 的命令执行器
    /// 用于 headless 模式的子进程执行
    /// </summary>
    internal class ProcessExecutor
    {
        private readonly ProcessCommand _command;
        private readonly List<Action<string>> _outputListeners = new();
        private readonly List<Action<string>> _errorListeners = new();
        private readonly List<Action<int>> _startedListeners = new();
        private readonly List<Action<int>> _exitedListeners = new();
        private readonly List<Action<string>> _timeoutListeners = new();
        private readonly Dictionary<Type, List<Delegate>> _eventListeners = new();

        public ProcessExecutor(ProcessCommand command)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
        }

        #region 监听器注册方法

        /// <summary>
        /// 注册标准输出监听器
        /// </summary>
        public ProcessExecutor OnOutput(Action<string> handler)
        {
            if (handler != null)
                _outputListeners.Add(handler);
            return this;
        }

        /// <summary>
        /// 注册标准错误监听器
        /// </summary>
        public ProcessExecutor OnError(Action<string> handler)
        {
            if (handler != null)
                _errorListeners.Add(handler);
            return this;
        }

        /// <summary>
        /// 注册进程启动监听器
        /// </summary>
        public ProcessExecutor OnStarted(Action<int> handler)
        {
            if (handler != null)
                _startedListeners.Add(handler);
            return this;
        }

        /// <summary>
        /// 注册进程退出监听器
        /// </summary>
        public ProcessExecutor OnExited(Action<int> handler)
        {
            if (handler != null)
                _exitedListeners.Add(handler);
            return this;
        }

        /// <summary>
        /// 注册超时监听器
        /// </summary>
        public ProcessExecutor OnTimeout(Action<string> handler)
        {
            if (handler != null)
                _timeoutListeners.Add(handler);
            return this;
        }

        /// <summary>
        /// 注册通用事件监听器
        /// </summary>
        public ProcessExecutor OnEvent<T>(Action<T> handler) where T : ProcessCommandEvent
        {
            if (handler != null)
            {
                if (!_eventListeners.ContainsKey(typeof(T)))
                    _eventListeners[typeof(T)] = new List<Delegate>();
                _eventListeners[typeof(T)].Add(handler);
            }
            return this;
        }

        /// <summary>
        /// 注册异步标准输出监听器
        /// </summary>
        public ProcessExecutor OnOutputAsync(Func<string, Task> handler)
        {
            if (handler != null)
                _outputListeners.Add(async text => await handler(text));
            return this;
        }

        /// <summary>
        /// 注册异步标准错误监听器
        /// </summary>
        public ProcessExecutor OnErrorAsync(Func<string, Task> handler)
        {
            if (handler != null)
                _errorListeners.Add(async text => await handler(text));
            return this;
        }

        /// <summary>
        /// 注册异步通用事件监听器
        /// </summary>
        public ProcessExecutor OnEventAsync<T>(Func<T, Task> handler) where T : ProcessCommandEvent
        {
            if (handler != null)
            {
                if (!_eventListeners.ContainsKey(typeof(T)))
                    _eventListeners[typeof(T)] = new List<Delegate>();
                _eventListeners[typeof(T)].Add(handler);
            }
            return this;
        }

        #endregion

        #region 监听器触发方法

        /// <summary>
        /// 触发所有输出监听器
        /// </summary>
        private void TriggerOutputListeners(string text)
        {
            foreach (var listener in _outputListeners)
            {
                try
                {
                    listener(text);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Output listener error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 触发所有错误监听器
        /// </summary>
        private void TriggerErrorListeners(string text)
        {
            foreach (var listener in _errorListeners)
            {
                try
                {
                    listener(text);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error listener error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 触发所有启动监听器
        /// </summary>
        private void TriggerStartedListeners(int processId)
        {
            foreach (var listener in _startedListeners)
            {
                try
                {
                    listener(processId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Started listener error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 触发所有退出监听器
        /// </summary>
        private void TriggerExitedListeners(int exitCode)
        {
            foreach (var listener in _exitedListeners)
            {
                try
                {
                    listener(exitCode);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Exited listener error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 触发所有超时监听器
        /// </summary>
        private void TriggerTimeoutListeners(string message)
        {
            foreach (var listener in _timeoutListeners)
            {
                try
                {
                    listener(message);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Timeout listener error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 触发通用事件监听器
        /// </summary>
        private void TriggerEventListeners<T>(T evt) where T : ProcessCommandEvent
        {
            if (_eventListeners.TryGetValue(typeof(T), out var listeners))
            {
                foreach (var listener in listeners)
                {
                    try
                    {
                        if (listener is Action<T> action)
                            action(evt);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Event listener error: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 异步触发通用事件监听器
        /// </summary>
        private async Task TriggerEventListenersAsync<T>(T evt) where T : ProcessCommandEvent
        {
            if (_eventListeners.TryGetValue(typeof(T), out var listeners))
            {
                foreach (var listener in listeners)
                {
                    try
                    {
                        if (listener is Action<T> action)
                            action(evt);
                        else if (listener is Func<T, Task> asyncAction)
                            await asyncAction(evt);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Event listener error: {ex.Message}");
                    }
                }
            }
        }

        #endregion

        #region 执行方法

        /// <summary>
        /// 执行命令并返回结果
        /// </summary>
        public async Task<ProcessResult> ExecuteAsync(Encoding encoding, CancellationToken cancellationToken = default)
        {
            // 创建进程
            var process = CreateProcess();
            
            try
            {
                // 启动进程
                process.Start();

                // 如果有标准输入数据或流，则异步写入
                if (_command.StandardInputPipe != null)
                {
                    await WritePipeSourceAsync(process.StandardInput.BaseStream, _command.StandardInputPipe, cancellationToken);
                }
                else if (_command.StandardInputStream != null)
                {
                    await WriteInputStreamAsync(process.StandardInput, _command.StandardInputStream, cancellationToken);
                }
                else if (_command.StandardInput != null)
                {
                    await WriteInputAsync(process.StandardInput, _command.StandardInput, cancellationToken);
                }

                // 更新上下文
                _command.Context.MarkStarted(process.Id);

                // 触发启动监听器
                TriggerStartedListeners(process.Id);
                TriggerEventListeners(new ProcessStartedEvent
                {
                    ProcessId = process.Id,
                    Context = _command.Context.CreateSnapshot()
                });

                // 启动输出读取任务
                Task<string> outputTask;
                Task<string> errorTask;

                if (_command.StandardOutputTarget != null)
                {
                    // 使用 PipeTarget 处理输出
                    outputTask = ReadOutputToTargetAsync(process.StandardOutput.BaseStream, _command.StandardOutputTarget, cancellationToken);
                }
                else
                {
                    // 默认行为：缓冲输出
                    outputTask = ReadOutputAsync(process.StandardOutput, encoding, cancellationToken);
                }

                if (_command.StandardErrorTarget != null)
                {
                    // 使用 PipeTarget 处理错误
                    errorTask = ReadOutputToTargetAsync(process.StandardError.BaseStream, _command.StandardErrorTarget, cancellationToken);
                }
                else
                {
                    // 默认行为：缓冲错误
                    errorTask = ReadErrorAsync(process.StandardError, encoding, cancellationToken);
                }

                var processExitTask = process.WaitForExitAsync(cancellationToken);
                var timeoutTask = WaitForTimeoutAsync(cancellationToken);
                var completedTask = await Task.WhenAny(processExitTask, timeoutTask);

                if (completedTask == timeoutTask && await timeoutTask)
                {
                    KillProcess(process);
                    await process.WaitForExitAsync(CancellationToken.None);
                    _command.Context.MarkTimedOut();

                    var timeoutMessage = $"Command timed out after {_command.Timeout.TotalSeconds} seconds";
                    TriggerTimeoutListeners(timeoutMessage);
                    TriggerEventListeners(new ProcessTimeoutEvent
                    {
                        Message = timeoutMessage,
                        Context = _command.Context.CreateSnapshot()
                    });

                    throw new TimeoutException(timeoutMessage);
                }

                await processExitTask;

                // 等待输出读取完成
                var output = await outputTask;
                var error = await errorTask;

                // 更新上下文
                _command.Context.MarkExited(process.ExitCode);

                // 触发退出监听器
                TriggerExitedListeners(process.ExitCode);
                TriggerEventListeners(new ProcessExitedEvent
                {
                    ExitCode = process.ExitCode,
                    Context = _command.Context.CreateSnapshot()
                });

                // 生成结果
                var result = new ProcessResult
                {
                    ProcessId = process.Id,
                    ExitCode = process.ExitCode,
                    StandardOutput = output,
                    StandardError = error,
                    CompletionTime = DateTime.UtcNow,
                    Context = _command.Context.CreateSnapshot()
                };

                // 验证退出码
                ValidateExitCode(result);

                return result;
            }
            catch (OperationCanceledException)
            {
                KillProcess(process);
                throw;
            }
        }

    
        #endregion

        #region 辅助方法

        /// <summary>
        /// 创建 Process 实例
        /// </summary>
        private Process CreateProcess()
        {
            // 优先使用解析后的路径，如果未找到则使用原始路径
            var executablePath = _command.ResolvedTarget ?? _command.Target;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = _command.WorkingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = _command.StandardInput != null || _command.StandardInputStream != null || _command.StandardInputPipe != null, // 如果有输入数据、流或管道则启用输入重定向
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            // 设置启动参数
            for (int i = 0; i<_command.Arguments.Count; i++)
            {
                process.StartInfo.ArgumentList.Add(_command.Arguments[i]);
            }
            // 设置环境变量
            foreach (var kvp in _command.EnvironmentVariables)
            {
                process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }

            return process;
        }

        /// <summary>
        /// 读取标准输出
        /// </summary>
        private async Task<string> ReadOutputAsync(StreamReader reader, Encoding encoding, CancellationToken cancellationToken)
        {
            var output = new StringBuilder();
            var buffer = new char[4096];

            while (!cancellationToken.IsCancellationRequested)
            {
                int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (charsRead == 0) break;

                var text = new string(buffer, 0, charsRead);
                output.Append(text);

                // 触发输出监听器
                TriggerOutputListeners(text);

                // 更新上下文
                _command.Context.AppendOutput(text);
            }

            return output.ToString();
        }

        /// <summary>
        /// 读取标准错误
        /// </summary>
        private async Task<string> ReadErrorAsync(StreamReader reader, Encoding encoding, CancellationToken cancellationToken)
        {
            var error = new StringBuilder();
            var buffer = new char[4096];

            while (!cancellationToken.IsCancellationRequested)
            {
                int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (charsRead == 0) break;

                var text = new string(buffer, 0, charsRead);
                error.Append(text);

                // 触发错误监听器
                TriggerErrorListeners(text);

                // 更新上下文
                _command.Context.AppendError(text);
            }

            return error.ToString();
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
        /// 将输出读取到 PipeTarget
        /// </summary>
        private async Task<string> ReadOutputToTargetAsync(Stream source, PipeTarget target, CancellationToken cancellationToken)
        {
            try
            {
                await target.CopyFromAsync(source, cancellationToken);
                return string.Empty; // PipeTarget 模式下不返回缓冲内容
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Read output to target error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 验证退出码
        /// </summary>
        private void ValidateExitCode(ProcessResult result)
        {
            if (_command.Validation == CommandResultValidation.None)
                return;

            if (_command.Validation == CommandResultValidation.ZeroExitCode && result.ExitCode != 0)
            {
                throw new ProcessExecutionException(
                    $"Command execution failed with exit code {result.ExitCode}. " +
                    $"Command: {_command.GetFullCommandLine()}",
                    result);
            }
        }

        private async Task<bool> WaitForTimeoutAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_command.Context.TryGetTimeoutDeadline(out var deadlineUtc))
                {
                    return false;
                }

                var remaining = deadlineUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return true;
                }

                var delay = remaining < TimeSpan.FromMilliseconds(50)
                    ? remaining
                    : TimeSpan.FromMilliseconds(50);

                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            return false;
        }

        private static void KillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 忽略终止时的错误
            }
        }

        #endregion
    }
}
