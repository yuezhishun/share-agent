using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ProcessRunner
{

    /// <summary>
    /// 简化的进程命令包装器，提供类似CliWrap的链式调用API
    /// </summary>
    public class ProcessCommand
    {
        private readonly string _target;
        private readonly string? _resolvedTarget;
        private readonly List<string> _arguments = new();
        private string _workingDirectory = Environment.CurrentDirectory;
        private readonly Dictionary<string, string> _environmentVariables = new();
        private TimeSpan _timeout = System.Threading.Timeout.InfiniteTimeSpan;
        private bool _verbatimCommandLine = true;
        private ProcessExecutor? _processExecutor;
        private string? _standardInput;
        private Stream? _standardInputStream;
        private PipeSource? _standardInputPipe;
        private CommandResultValidation _validation = CommandResultValidation.ZeroExitCode;
        private PipeTarget? _standardOutputTarget;
        private PipeTarget? _standardErrorTarget;

        /// <summary>
        /// 命令执行上下文
        /// </summary>
        public ProcessCommandContext Context { get; }

        /// <summary>
        /// 目标程序路径（原始输入）
        /// </summary>
        public string Target => _target;

        /// <summary>
        /// 解析后的目标程序完整路径
        /// </summary>
        public string? ResolvedTarget => _resolvedTarget;

        /// <summary>
        /// 命令行参数列表（只读）
        /// </summary>
        public IReadOnlyList<string> Arguments => _arguments.AsReadOnly();

        /// <summary>
        /// 工作目录
        /// </summary>
        public string WorkingDirectory => _workingDirectory;

        /// <summary>
        /// 环境变量字典
        /// </summary>
        public IReadOnlyDictionary<string, string> EnvironmentVariables => _environmentVariables.AsReadOnly();

  
        /// <summary>
        /// 超时时间
        /// </summary>
        public TimeSpan Timeout => _timeout;

        /// <summary>
        /// 是否使用逐字命令行
        /// </summary>
        public bool VerbatimCommandLine => _verbatimCommandLine;

        /// <summary>
        /// 标准输入内容（用于重定向输入）
        /// </summary>
        public string? StandardInput => _standardInput;

        /// <summary>
        /// 标准输入流（用于重定向输入）
        /// </summary>
        public Stream? StandardInputStream => _standardInputStream;

        /// <summary>
        /// 标准输入管道源（用于重定向输入）
        /// </summary>
        public PipeSource? StandardInputPipe => _standardInputPipe;

        /// <summary>
        /// 结果验证选项
        /// </summary>
        public CommandResultValidation Validation => _validation;

        /// <summary>
        /// 标准输出目标
        /// </summary>
        public PipeTarget? StandardOutputTarget => _standardOutputTarget;

        /// <summary>
        /// 标准错误目标
        /// </summary>
        public PipeTarget? StandardErrorTarget => _standardErrorTarget;

        /// <summary>
        /// 创建一个新的进程命令
        /// </summary>
        /// <param name="target">要执行的目标程序</param>
        public ProcessCommand(string target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            // 基础设置
            _target = target;

            // 尝试解析可执行文件路径
            _resolvedTarget = ResolveExecutablePath(target);

            // 创建上下文
            Context = new ProcessCommandContext(this);
        }

        /// <summary>
        /// 添加命令行参数
        /// </summary>
        public ProcessCommand AddArguments(IEnumerable<string> arguments)
        {
            if (arguments != null)
            {
                _arguments.AddRange(arguments);
            }
            return this;
        }

        /// <summary>
        /// 添加命令行参数
        /// </summary>
        public ProcessCommand AddArguments(params string[] arguments)
        {
            if (arguments != null)
            {
                _arguments.AddRange(arguments);
            }
            return this;
        }

        /// <summary>
        /// 设置工作目录
        /// </summary>
        public ProcessCommand SetWorkingDirectory(string workingDirectory)
        {
            if (workingDirectory == null)
                throw new ArgumentNullException(nameof(workingDirectory));

            if (string.IsNullOrWhiteSpace(workingDirectory))
                throw new ArgumentException("工作目录不能为空或仅包含空白字符", nameof(workingDirectory));

            // 解析并验证完整路径（防止路径遍历攻击）
            var fullPath = Path.GetFullPath(workingDirectory);

            // 验证路径是否存在且是目录
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException($"工作目录不存在: {fullPath}");

            // 验证路径不是系统关键目录（可选安全增强）
            ValidateWorkingDirectorySafety(fullPath);

            _workingDirectory = fullPath;
            return this;
        }

        /// <summary>
        /// 验证工作目录安全性
        /// </summary>
        private static void ValidateWorkingDirectorySafety(string fullPath)
        {
            // 获取规范化路径
            var normalizedPath = NormalizePath(fullPath);

            // Windows 系统特定检查
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // 检查是否是系统根目录
                var systemRoot = Path.GetPathRoot(normalizedPath);
                if (string.Equals(normalizedPath.TrimEnd('\\', '/'), systemRoot?.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "工作目录不能是系统根目录，这可能导致安全风险");
                }

                // 检查是否是 Windows 系统目录
                var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var system32Dir = Environment.GetFolderPath(Environment.SpecialFolder.System);

                if (IsSubPath(normalizedPath, windowsDir) ||
                    IsSubPath(normalizedPath, system32Dir))
                {
                    throw new ArgumentException(
                        "工作目录不能是 Windows 系统目录或其子目录");
                }
            }
            else
            {
                // Unix/Linux/macOS 系统特定检查
                // 检查是否是根目录
                if (normalizedPath == "/")
                {
                    throw new ArgumentException(
                        "工作目录不能是系统根目录，这可能导致安全风险");
                }

                // 检查是否是系统关键目录
                var sensitivePaths = new[] { "/bin", "/sbin", "/usr/bin", "/usr/sbin", "/etc", "/lib", "/lib64" };
                foreach (var sensitivePath in sensitivePaths)
                {
                    if (IsSubPath(normalizedPath, sensitivePath) ||
                        string.Equals(normalizedPath, sensitivePath, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            $"工作目录不能是系统关键目录: {sensitivePath}");
                    }
                }
            }
        }

        /// <summary>
        /// 规范化路径（统一使用目录分隔符）
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // 统一使用当前平台的目录分隔符
            var normalized = path.Replace('/', Path.DirectorySeparatorChar)
                                .Replace('\\', Path.DirectorySeparatorChar);

            // 移除末尾的目录分隔符（除非就是根目录）
            if (normalized.Length > 1 &&
                normalized[^1] == Path.DirectorySeparatorChar &&
                !normalized.StartsWith("\\\\", StringComparison.Ordinal)) // 保留 UNC 路径
            {
                normalized = normalized[..^1];
            }

            return normalized;
        }

        /// <summary>
        /// 检查 childPath 是否是 parentPath 的子目录
        /// </summary>
        private static bool IsSubPath(string childPath, string parentPath)
        {
            if (string.IsNullOrEmpty(childPath) || string.IsNullOrEmpty(parentPath))
                return false;

            var normalizedChild = NormalizePath(childPath).TrimEnd(Path.DirectorySeparatorChar);
            var normalizedParent = NormalizePath(parentPath).TrimEnd(Path.DirectorySeparatorChar);

            // Windows 不区分大小写
            var comparison = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // 确保 parentPath 末尾有分隔符，防止部分匹配
            if (!normalizedParent.EndsWith(Path.DirectorySeparatorChar.ToString(), comparison))
            {
                normalizedParent += Path.DirectorySeparatorChar;
            }

            return normalizedChild.StartsWith(normalizedParent, comparison) ||
                   string.Equals(normalizedChild, normalizedParent.TrimEnd(Path.DirectorySeparatorChar), comparison);
        }

        /// <summary>
        /// 设置环境变量
        /// </summary>
        public ProcessCommand SetEnvironmentVariables(IEnumerable<(string key, string value)> variables)
        {
            if (variables != null)
            {
                foreach (var (key, value) in variables)
                {
                    _environmentVariables[key] = value;
                }
            }
            return this;
        }

        /// <summary>
        /// 设置环境变量
        /// </summary>
        public ProcessCommand SetEnvironmentVariable(string key, string value)
        {
            _environmentVariables[key] = value ?? throw new ArgumentNullException(nameof(value));
            return this;
        }

    
        /// <summary>
        /// 设置超时时间
        /// </summary>
        public ProcessCommand SetTimeout(TimeSpan timeout)
        {
            _timeout = timeout;
            return this;
        }

        /// <summary>
        /// 设置是否使用逐字命令行
        /// </summary>
        public ProcessCommand SetUseVerbatimCommandLine(bool verbatim = true)
        {
            _verbatimCommandLine = verbatim;
            return this;
        }

        /// <summary>
        /// 设置标准输入内容（用于向子进程发送数据）
        /// </summary>
        /// <param name="input">要发送的输入内容</param>
        public ProcessCommand WithStandardInput(string input)
        {
            _standardInput = input ?? throw new ArgumentNullException(nameof(input));
            _standardInputStream = null;
            _standardInputPipe = null;
            return this;
        }

        /// <summary>
        /// 设置标准输入流（用于向子进程发送数据）
        /// </summary>
        /// <param name="inputStream">输入流</param>
        public ProcessCommand WithStandardInput(Stream inputStream)
        {
            _standardInputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
            _standardInput = null;
            _standardInputPipe = null;
            return this;
        }

        /// <summary>
        /// 设置标准输入管道源（用于向子进程发送数据）
        /// </summary>
        /// <param name="source">输入源</param>
        public ProcessCommand WithStandardInputPipe(PipeSource source)
        {
            _standardInputPipe = source ?? throw new ArgumentNullException(nameof(source));
            _standardInput = null;
            _standardInputStream = null;
            return this;
        }

        /// <summary>
        /// 设置结果验证选项
        /// </summary>
        /// <param name="validation">验证选项</param>
        public ProcessCommand WithValidation(CommandResultValidation validation)
        {
            _validation = validation;
            return this;
        }

        /// <summary>
        /// 设置标准输出管道目标
        /// </summary>
        /// <param name="target">输出目标</param>
        public ProcessCommand WithStandardOutputPipe(PipeTarget target)
        {
            _standardOutputTarget = target ?? throw new ArgumentNullException(nameof(target));
            return this;
        }

        /// <summary>
        /// 设置标准错误管道目标
        /// </summary>
        /// <param name="target">错误目标</param>
        public ProcessCommand WithStandardErrorPipe(PipeTarget target)
        {
            _standardErrorTarget = target ?? throw new ArgumentNullException(nameof(target));
            return this;
        }

        #region 监听器注册方法

        /// <summary>
        /// 注册标准输出监听器
        /// </summary>
        public ProcessCommand OnOutput(Action<string> handler)
        {
            EnsureProcessExecutor();
            _processExecutor!.OnOutput(handler);
            return this;
        }

        /// <summary>
        /// 注册标准错误监听器
        /// </summary>
        public ProcessCommand OnError(Action<string> handler)
        {
            EnsureProcessExecutor();
            _processExecutor!.OnError(handler);
            return this;
        }

        /// <summary>
        /// 注册进程启动监听器
        /// </summary>
        public ProcessCommand OnStarted(Action<int> handler)
        {
            EnsureProcessExecutor();
            _processExecutor!.OnStarted(handler);
            return this;
        }

        /// <summary>
        /// 注册进程退出监听器
        /// </summary>
        public ProcessCommand OnExited(Action<int> handler)
        {
            EnsureProcessExecutor();
            _processExecutor!.OnExited(handler);
            return this;
        }

        /// <summary>
        /// 注册超时监听器
        /// </summary>
        public ProcessCommand OnTimeout(Action<string> handler)
        {
            EnsureProcessExecutor();
            _processExecutor!.OnTimeout(handler);
            return this;
        }

        /// <summary>
        /// 注册通用事件监听器
        /// </summary>
        public ProcessCommand OnEvent<T>(Action<T> handler) where T : ProcessCommandEvent
        {
            EnsureProcessExecutor();
            _processExecutor!.OnEvent(handler);
            return this;
        }

        /// <summary>
        /// 注册异步标准输出监听器
        /// </summary>
        public ProcessCommand OnOutputAsync(Func<string, Task> handler)
        {
            EnsureProcessExecutor();
            _processExecutor!.OnOutputAsync(handler);
            return this;
        }

        /// <summary>
        /// 注册异步标准错误监听器
        /// </summary>
        public ProcessCommand OnErrorAsync(Func<string, Task> handler)
        {
            EnsureProcessExecutor();
            _processExecutor!.OnErrorAsync(handler);
            return this;
        }

        /// <summary>
        /// 注册异步通用事件监听器
        /// </summary>
        public ProcessCommand OnEventAsync<T>(Func<T, Task> handler) where T : ProcessCommandEvent
        {
            EnsureProcessExecutor();
            _processExecutor!.OnEventAsync(handler);
            return this;
        }

        /// <summary>
        /// 确保 ProcessExecutor 已创建
        /// </summary>
        private void EnsureProcessExecutor()
        {
            _processExecutor ??= new ProcessExecutor(this);
        }

        #endregion

        
        /// <summary>
        /// 执行命令并等待完成，返回缓冲的输出
        /// </summary>
        public Task<ProcessResult> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// 执行命令并等待完成，返回缓冲的输出
        /// </summary>
        public virtual Task<ProcessResult> ExecuteAsync(Encoding encoding, CancellationToken cancellationToken = default)
        {
            // 保留已注册的监听器，避免托管执行场景下事件丢失。
            _processExecutor ??= new ProcessExecutor(this);
            return _processExecutor.ExecuteAsync(encoding, cancellationToken);
        }

        /// <summary>
        /// 以流式方式监听命令执行事件
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步事件流</returns>
        public IAsyncEnumerable<ProcessCommandEvent> ListenAsync(CancellationToken cancellationToken = default)
        {
            return ListenAsync(Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// 以流式方式监听命令执行事件
        /// </summary>
        /// <param name="encoding">编码</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步事件流</returns>
        public IAsyncEnumerable<ProcessCommandEvent> ListenAsync(Encoding encoding, CancellationToken cancellationToken = default)
        {
            var streamExecutor = new StreamedProcessExecutor(this, encoding);
            return streamExecutor.ListenAsync(cancellationToken);
        }

        /// <summary>
        /// 将此命令的输出管道到另一个命令
        /// </summary>
        /// <param name="target">目标命令</param>
        /// <returns>新的组合命令</returns>
        public PipeableProcessCommand PipeTo(ProcessCommand target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return new PipeableProcessCommand(this, null).PipeTo(target);
        }

        /// <summary>
        /// 将此命令的输出管道到另一个命令
        /// </summary>
        /// <param name="target">目标命令的可执行文件路径</param>
        /// <returns>新的组合命令</returns>
        public PipeableProcessCommand PipeTo(string target)
        {
            return PipeTo(new ProcessCommand(target));
        }

        /// <summary>
        /// 重载 | 操作符，创建管道链
        /// </summary>
        public static PipeableProcessCommand operator |(ProcessCommand left, ProcessCommand right)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));
            return new PipeableProcessCommand(left, null).PipeTo(right);
        }

        #region 上下文操作方法

        /// <summary>
        /// 动态更新超时时间
        /// </summary>
        /// <param name="newTimeout">新的超时时间</param>
        /// <returns>是否更新成功</returns>
        public bool TryUpdateTimeout(TimeSpan newTimeout)
        {
            return Context.TryUpdateTimeout(newTimeout);
        }

        
        /// <summary>
        /// 设置自定义上下文数据
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        public void SetCustomData(string key, object value)
        {
            Context.SetCustomData(key, value);
        }

        /// <summary>
        /// 获取自定义上下文数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>值</returns>
        public T GetCustomData<T>(string key, T defaultValue = default)
        {
            return Context.GetCustomData<T>(key, defaultValue);
        }

        /// <summary>
        /// 取消命令执行
        /// </summary>
        public void Cancel()
        {
            Context.Cancel();
        }

        /// <summary>
        /// 获取当前执行状态的描述
        /// </summary>
        public string GetStatusDescription()
        {
            return Context.GetStatusDescription();
        }

        /// <summary>
        /// 创建当前上下文的快照
        /// </summary>
        public ProcessCommandContextSnapshot CreateContextSnapshot()
        {
            return Context.CreateSnapshot();
        }

        /// <summary>
        /// 获取完整的命令行字符串
        /// </summary>
        public string GetFullCommandLine()
        {
            return Arguments.Count > 0 ? $"{Target} {string.Join(" ", Arguments)}" : Target;
        }

        /// <summary>
        /// 获取用于执行的完整命令行字符串（使用解析后的路径）
        /// </summary>
        public string GetExecutableCommandLine()
        {
            var executablePath = _resolvedTarget ?? Target;
            return Arguments.Count > 0 ? $"{executablePath} {string.Join(" ", Arguments)}" : executablePath;
        }

        #endregion

        #region 路径解析方法

        /// <summary>
        /// 解析可执行文件的完整路径
        /// </summary>
        /// <param name="target">目标可执行文件名或路径</param>
        /// <returns>解析后的完整路径，如果未找到则返回null</returns>
        private string? ResolveExecutablePath(string target)
        {
            var pathResolver = new PathResolver();
            return pathResolver.FindExecutableInPath(target, _environmentVariables);
        }

        #endregion
    }

    /// <summary>
    /// 流式进程执行器 - 支持 IAsyncEnumerable 事件流
    /// </summary>
    internal class StreamedProcessExecutor
    {
        private readonly ProcessCommand _command;
        private readonly Encoding _encoding;

        public StreamedProcessExecutor(ProcessCommand command, Encoding encoding)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
            _encoding = encoding ?? Encoding.Default;
        }

        /// <summary>
        /// 以流式方式监听命令执行事件
        /// </summary>
        public async IAsyncEnumerable<ProcessCommandEvent> ListenAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channel = Channel.CreateUnbounded<ProcessCommandEvent>();
            var writer = channel.Writer;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (_command.Timeout != Timeout.InfiniteTimeSpan)
                cts.CancelAfter(_command.Timeout);

            // 创建进程
            var process = CreateProcess();

            // 启动执行
            _ = Task.Run(async () =>
            {
                try
                {
                    // 启动进程
                    process.Start();

                    // 标记启动
                    _command.Context.MarkStarted(process.Id);
                    await writer.WriteAsync(new ProcessStartedEvent
                    {
                        ProcessId = process.Id,
                        Context = _command.Context.CreateSnapshot()
                    }, cts.Token);

                    // 写入标准输入
                    if (_command.StandardInputPipe != null)
                    {
                        await WritePipeSourceAsync(process.StandardInput.BaseStream, _command.StandardInputPipe, cts.Token);
                    }
                    else if (_command.StandardInputStream != null)
                    {
                        await WriteInputStreamAsync(process.StandardInput, _command.StandardInputStream, cts.Token);
                    }
                    else if (_command.StandardInput != null)
                    {
                        await WriteInputAsync(process.StandardInput, _command.StandardInput, cts.Token);
                    }

                    // 读取输出和错误
                    Task outputTask;
                    Task errorTask;

                    if (_command.StandardOutputTarget != null)
                    {
                        outputTask = ReadOutputToTargetAsync(process.StandardOutput.BaseStream, _command.StandardOutputTarget, cts.Token);
                    }
                    else
                    {
                        outputTask = ReadOutputAsync(process.StandardOutput, writer, cts.Token);
                    }

                    if (_command.StandardErrorTarget != null)
                    {
                        errorTask = ReadOutputToTargetAsync(process.StandardError.BaseStream, _command.StandardErrorTarget, cts.Token);
                    }
                    else
                    {
                        errorTask = ReadErrorAsync(process.StandardError, writer, cts.Token);
                    }

                    // 等待进程退出
                    await process.WaitForExitAsync(cts.Token);

                    // 等待输出读取完成
                    await Task.WhenAll(outputTask, errorTask);

                    // 标记退出
                    _command.Context.MarkExited(process.ExitCode);
                    await writer.WriteAsync(new ProcessExitedEvent
                    {
                        ExitCode = process.ExitCode,
                        Context = _command.Context.CreateSnapshot()
                    }, cts.Token);

                    writer.Complete();
                }
                catch (OperationCanceledException ex)
                {
                    if (!cancellationToken.IsCancellationRequested && _command.Timeout != Timeout.InfiniteTimeSpan)
                    {
                        // 超时
                        try { process.Kill(); } catch { }
                        _command.Context.MarkTimedOut();
                        var timeoutMessage = $"Command timed out after {_command.Timeout.TotalSeconds} seconds";
                        await writer.WriteAsync(new ProcessTimeoutEvent
                        {
                            Message = timeoutMessage,
                            Context = _command.Context.CreateSnapshot()
                        }, CancellationToken.None);
                    }
                    writer.Complete(ex);
                }
                catch (Exception ex)
                {
                    try { process.Kill(); } catch { }
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
        private Process CreateProcess()
        {
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
                    RedirectStandardInput = _command.StandardInput != null || _command.StandardInputStream != null || _command.StandardInputPipe != null,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            // 设置启动参数
            for (int i = 0; i < _command.Arguments.Count; i++)
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
        private async Task ReadOutputAsync(StreamReader reader, ChannelWriter<ProcessCommandEvent> writer, CancellationToken cancellationToken)
        {
            var buffer = new char[4096];

            while (!cancellationToken.IsCancellationRequested)
            {
                int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (charsRead == 0) break;

                var text = new string(buffer, 0, charsRead);
                _command.Context.AppendOutput(text);
                await writer.WriteAsync(new StandardOutputEvent
                {
                    Text = text,
                    Context = _command.Context.CreateSnapshot()
                }, cancellationToken);
            }
        }

        /// <summary>
        /// 读取标准错误
        /// </summary>
        private async Task ReadErrorAsync(StreamReader reader, ChannelWriter<ProcessCommandEvent> writer, CancellationToken cancellationToken)
        {
            var buffer = new char[4096];

            while (!cancellationToken.IsCancellationRequested)
            {
                int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (charsRead == 0) break;

                var text = new string(buffer, 0, charsRead);
                _command.Context.AppendError(text);
                await writer.WriteAsync(new StandardErrorEvent
                {
                    Text = text,
                    Context = _command.Context.CreateSnapshot()
                }, cancellationToken);
            }
        }

        /// <summary>
        /// 将输出读取到 PipeTarget（不发送事件）
        /// </summary>
        private async Task ReadOutputToTargetAsync(Stream source, PipeTarget target, CancellationToken cancellationToken)
        {
            try
            {
                await target.CopyFromAsync(source, cancellationToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Read output to target error: {ex.Message}");
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
    }
}
