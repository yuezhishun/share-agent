using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessRunner
{
    /// <summary>
    /// 进程管理器 - 统一管理多个 ProcessCommand 进程
    /// </summary>
    public class ProcessManager : IDisposable
    {
        #region 内部数据结构

        /// <summary>
        /// 托管的进程信息
        /// </summary>
        private class ManagedProcess
        {
            public string Id { get; set; } = string.Empty;
            public ProcessCommand Command { get; set; } = null!;
            public ProcessStatus Status { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public ProcessResult? Result { get; set; }
            public Task<ProcessResult>? ExecutionTask { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; } = new();
            public List<ProcessOutputRecord> OutputHistory { get; set; } = new();
            public Dictionary<string, object> Metadata { get; set; } = new();
        }

        /// <summary>
        /// 输出记录
        /// </summary>
        public class ProcessOutputRecord
        {
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
            public ProcessOutputType OutputType { get; set; }
            public string Content { get; set; } = string.Empty;
            public string ProcessId { get; set; } = string.Empty;
        }

        /// <summary>
        /// 进程状态
        /// </summary>
        public enum ProcessStatus
        {
            /// <summary>未启动</summary>
            NotStarted,
            /// <summary>正在运行</summary>
            Running,
            /// <summary>已完成</summary>
            Completed,
            /// <summary>已失败</summary>
            Failed,
            /// <summary>已取消</summary>
            Cancelled,
            /// <summary>已超时</summary>
            TimedOut
        }

        /// <summary>
        /// 输出类型
        /// </summary>
        public enum ProcessOutputType
        {
            /// <summary>标准输出</summary>
            StandardOutput,
            /// <summary>标准错误</summary>
            StandardError,
            /// <summary>系统消息</summary>
            SystemMessage
        }

        #endregion

        #region 字段和属性

        private readonly ConcurrentDictionary<string, ManagedProcess> _processes = new();
        private readonly ConcurrentQueue<ProcessOutputRecord> _outputQueue = new();
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly object _disposeLock = new object();
        private bool _disposed = false;

        /// <summary>
        /// 最大并发进程数
        /// </summary>
        public int MaxConcurrency { get; }

        /// <summary>
        /// 当前运行的进程数
        /// </summary>
        public int RunningProcessCount => _processes.Values.Count(p => p.Status == ProcessStatus.Running);

        /// <summary>
        /// 总进程数
        /// </summary>
        public int TotalProcessCount => _processes.Count;

        /// <summary>
        /// 是否已释放
        /// </summary>
        public bool IsDisposed => _disposed;

        #endregion

        #region 事件

        /// <summary>
        /// 进程启动事件
        /// </summary>
        public event EventHandler<ProcessStartedEventArgs>? ProcessStarted;

        /// <summary>
        /// 进程输出事件
        /// </summary>
        public event EventHandler<ProcessOutputEventArgs>? ProcessOutput;

        /// <summary>
        /// 进程完成事件
        /// </summary>
        public event EventHandler<ProcessCompletedEventArgs>? ProcessCompleted;

        /// <summary>
        /// 进程失败事件
        /// </summary>
        public event EventHandler<ProcessFailedEventArgs>? ProcessFailed;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建进程管理器
        /// </summary>
        /// <param name="maxConcurrency">最大并发进程数，默认为10</param>
        public ProcessManager(int maxConcurrency = 10)
        {
            if (maxConcurrency <= 0)
                throw new ArgumentException("最大并发数必须大于0", nameof(maxConcurrency));

            MaxConcurrency = maxConcurrency;
            _concurrencyLimiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        #endregion

        #region 进程管理方法

        /// <summary>
        /// 注册子进程模式命令
        /// </summary>
        /// <param name="command">ProcessCommand 实例</param>
        /// <param name="processId">进程ID，为空时自动生成</param>
        /// <param name="metadata">元数据</param>
        /// <returns>进程ID</returns>
        public string RegisterProcess(ProcessCommand command, string? processId = null, Dictionary<string, object>? metadata = null)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var id = processId ?? GenerateProcessId();
            var managedProcess = new ManagedProcess
            {
                Id = id,
                Command = command,
                Status = ProcessStatus.NotStarted,
                StartTime = DateTime.UtcNow,
                Metadata = metadata ?? new Dictionary<string, object>()
            };

            _processes.TryAdd(id, managedProcess);
            return id;
        }

        /// <summary>
        /// 启动指定进程
        /// </summary>
        /// <param name="processId">进程ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>启动任务</returns>
        public async Task StartProcessAsync(string processId, CancellationToken cancellationToken = default)
        {
            if (!_processes.TryGetValue(processId, out var managedProcess))
                throw new ArgumentException($"进程 {processId} 不存在", nameof(processId));

            if (managedProcess.Status != ProcessStatus.NotStarted)
                throw new InvalidOperationException($"进程 {processId} 已启动或已完成");

            // 等待并发控制
            await _concurrencyLimiter.WaitAsync(cancellationToken);

            var signalReleased = new SignalReleasedState { Value = false };

            try
            {
                managedProcess.Status = ProcessStatus.Running;
                await StartHeadlessProcessAsync(managedProcess, cancellationToken, signalReleased);

                // 触发启动事件
                ProcessStarted?.Invoke(this, new ProcessStartedEventArgs
                {
                    ProcessId = processId,
                    StartTime = managedProcess.StartTime,
                    Metadata = managedProcess.Metadata
                });
            }
            catch (Exception ex)
            {
                managedProcess.Status = ProcessStatus.Failed;
                if (!signalReleased.Value)
                {
                    _concurrencyLimiter.Release();
                    signalReleased.Value = true;
                }

                ProcessFailed?.Invoke(this, new ProcessFailedEventArgs
                {
                    ProcessId = processId,
                    Error = ex,
                    StartTime = managedProcess.StartTime,
                    EndTime = DateTime.UtcNow,
                    Metadata = managedProcess.Metadata
                });

                throw;
            }
        }

        /// <summary>
        /// 批量启动进程
        /// </summary>
        /// <param name="processIds">进程ID列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>启动任务</returns>
        public async Task StartProcessesAsync(IEnumerable<string> processIds, CancellationToken cancellationToken = default)
        {
            var tasks = processIds.Select(id => StartProcessAsync(id, cancellationToken));
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 停止指定进程
        /// </summary>
        /// <param name="processId">进程ID</param>
        /// <param name="force">是否强制终止</param>
        /// <returns>停止任务</returns>
        public async Task StopProcessAsync(string processId, bool force = false)
        {
            if (!_processes.TryGetValue(processId, out var managedProcess))
                throw new ArgumentException($"进程 {processId} 不存在", nameof(processId));

            if (managedProcess.Status != ProcessStatus.Running)
                return;

            try
            {
                managedProcess.CancellationTokenSource.Cancel();
                await WaitProcessCompletionAsync(processId, TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                // 记录错误但不抛出
                AddOutputRecord(processId, ProcessOutputType.SystemMessage, $"停止进程时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 等待指定进程完成
        /// </summary>
        /// <param name="processId">进程ID</param>
        /// <param name="timeout">超时时间</param>
        /// <returns>等待任务</returns>
        public async Task<ProcessResult?> WaitProcessAsync(string processId, TimeSpan? timeout = null)
        {
            if (!_processes.TryGetValue(processId, out var managedProcess))
                throw new ArgumentException($"进程 {processId} 不存在", nameof(processId));

            if (managedProcess.ExecutionTask == null)
                return managedProcess.Result;

            using var timeoutCts = timeout.HasValue
                ? new CancellationTokenSource(timeout.Value)
                : new CancellationTokenSource();

            try
            {
                var result = await managedProcess.ExecutionTask.WaitAsync(timeoutCts.Token);
                managedProcess.Result = result;
                return result;
            }
            catch (TimeoutException)
            {
                managedProcess.Status = ProcessStatus.TimedOut;
                return null;
            }
        }

        /// <summary>
        /// 等待所有进程完成
        /// </summary>
        /// <param name="timeout">超时时间</param>
        /// <returns>等待任务</returns>
        public async Task<Dictionary<string, ProcessResult?>> WaitAllProcessesAsync(TimeSpan? timeout = null)
        {
            var results = new Dictionary<string, ProcessResult?>();
            var tasks = _processes.Values
                .Where(p => p.Status == ProcessStatus.Running || p.Status == ProcessStatus.NotStarted)
                .Select(async p =>
                {
                    var result = await WaitProcessAsync(p.Id, timeout);
                    results[p.Id] = result;
                });

            await Task.WhenAll(tasks);

            // 添加已完成进程的结果
            foreach (var kvp in _processes.Where(p => p.Value.Result != null))
            {
                results[kvp.Key] = kvp.Value.Result;
            }

            return results;
        }

        /// <summary>
        /// 获取进程状态
        /// </summary>
        /// <param name="processId">进程ID</param>
        /// <returns>进程状态</returns>
        public ProcessStatus GetProcessStatus(string processId)
        {
            return _processes.TryGetValue(processId, out var managedProcess)
                ? managedProcess.Status
                : throw new ArgumentException($"进程 {processId} 不存在", nameof(processId));
        }

        /// <summary>
        /// 获取进程信息
        /// </summary>
        /// <param name="processId">进程ID</param>
        /// <returns>进程信息</returns>
        public ProcessInfo GetProcessInfo(string processId)
        {
            if (!_processes.TryGetValue(processId, out var managedProcess))
                throw new ArgumentException($"进程 {processId} 不存在", nameof(processId));

            return new ProcessInfo
            {
                Id = managedProcess.Id,
                Status = managedProcess.Status,
                StartTime = managedProcess.StartTime,
                EndTime = managedProcess.EndTime,
                Duration = managedProcess.EndTime.HasValue
                    ? managedProcess.EndTime.Value - managedProcess.StartTime
                    : DateTime.UtcNow - managedProcess.StartTime,
                Command = managedProcess.Command.GetFullCommandLine(),
                Result = managedProcess.Result,
                OutputCount = managedProcess.OutputHistory.Count,
                Metadata = managedProcess.Metadata
            };
        }

        /// <summary>
        /// 获取所有进程信息
        /// </summary>
        /// <returns>所有进程信息列表</returns>
        public IReadOnlyList<ProcessInfo> GetAllProcesses()
        {
            return _processes.Values.Select(p => new ProcessInfo
            {
                Id = p.Id,
                Status = p.Status,
                StartTime = p.StartTime,
                EndTime = p.EndTime,
                Duration = p.EndTime.HasValue
                    ? p.EndTime.Value - p.StartTime
                    : DateTime.UtcNow - p.StartTime,
                Command = p.Command.GetFullCommandLine(),
                Result = p.Result,
                OutputCount = p.OutputHistory.Count,
                Metadata = p.Metadata
            }).ToList();
        }

        #endregion

        #region 输出管理方法

        /// <summary>
        /// 获取进程输出历史
        /// </summary>
        /// <param name="processId">进程ID</param>
        /// <param name="outputType">输出类型过滤</param>
        /// <param name="since">起始时间</param>
        /// <returns>输出记录列表</returns>
        public IReadOnlyList<ProcessOutputRecord> GetProcessOutput(string processId, ProcessOutputType? outputType = null, DateTime? since = null)
        {
            if (!_processes.TryGetValue(processId, out var managedProcess))
                throw new ArgumentException($"进程 {processId} 不存在", nameof(processId));

            var query = managedProcess.OutputHistory.AsEnumerable();

            if (outputType.HasValue)
                query = query.Where(o => o.OutputType == outputType.Value);

            if (since.HasValue)
                query = query.Where(o => o.Timestamp >= since.Value);

            return query.OrderBy(o => o.Timestamp).ToList();
        }

        /// <summary>
        /// 获取所有进程的混合输出流
        /// </summary>
        /// <param name="outputType">输出类型过滤</param>
        /// <param name="since">起始时间</param>
        /// <returns>输出记录列表</returns>
        public IReadOnlyList<ProcessOutputRecord> GetAllOutput(ProcessOutputType? outputType = null, DateTime? since = null)
        {
            var query = _outputQueue.AsEnumerable();

            if (outputType.HasValue)
                query = query.Where(o => o.OutputType == outputType.Value);

            if (since.HasValue)
                query = query.Where(o => o.Timestamp >= since.Value);

            return query.OrderBy(o => o.Timestamp).ToList();
        }

        #endregion

        #region 私有方法

        private string GenerateProcessId()
        {
            return $"proc_{Guid.NewGuid():N}";
        }

        // 辅助类用于跟踪信号释放状态
        private class SignalReleasedState
        {
            public bool Value { get; set; }
        }

        private async Task StartHeadlessProcessAsync(ManagedProcess managedProcess, CancellationToken cancellationToken, SignalReleasedState signalReleasedState)
        {
            var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                managedProcess.CancellationTokenSource.Token);

            // 配置监听器
            managedProcess.Command
                .OnStarted(pid => AddOutputRecord(managedProcess.Id, ProcessOutputType.SystemMessage, $"进程启动 PID: {pid}"))
                .OnOutput(text => AddOutputRecord(managedProcess.Id, ProcessOutputType.StandardOutput, text))
                .OnError(text => AddOutputRecord(managedProcess.Id, ProcessOutputType.StandardError, text))
                .OnExited(code => AddOutputRecord(managedProcess.Id, ProcessOutputType.SystemMessage, $"进程退出 退出码: {code}"));

            // 启动执行任务
            managedProcess.ExecutionTask = Task.Run(async () =>
            {
                try
                {
                    var result = await managedProcess.Command.ExecuteAsync(combinedCts.Token);
                    managedProcess.Result = result;
                    managedProcess.Status = result.ExitCode == 0 ? ProcessStatus.Completed : ProcessStatus.Failed;
                    return result;
                }
                catch (TimeoutException)
                {
                    managedProcess.Status = ProcessStatus.TimedOut;
                    throw;
                }
                catch (OperationCanceledException)
                {
                    managedProcess.Status = ProcessStatus.Cancelled;
                    throw;
                }
                catch (Exception)
                {
                    managedProcess.Status = ProcessStatus.Failed;
                    throw;
                }
            }, combinedCts.Token);

            // 等待完成
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await managedProcess.ExecutionTask;
                    managedProcess.EndTime = DateTime.UtcNow;
                    if (!signalReleasedState.Value)
                    {
                        _concurrencyLimiter.Release();
                        signalReleasedState.Value = true;
                    }

                    if (managedProcess.Status == ProcessStatus.Completed)
                    {
                        ProcessCompleted?.Invoke(this, new ProcessCompletedEventArgs
                        {
                            ProcessId = managedProcess.Id,
                            Result = result,
                            StartTime = managedProcess.StartTime,
                            EndTime = managedProcess.EndTime.Value,
                            Metadata = managedProcess.Metadata
                        });
                    }
                    else
                    {
                        ProcessFailed?.Invoke(this, new ProcessFailedEventArgs
                        {
                            ProcessId = managedProcess.Id,
                            Error = new Exception($"进程失败，状态: {managedProcess.Status}"),
                            StartTime = managedProcess.StartTime,
                            EndTime = managedProcess.EndTime ?? DateTime.UtcNow,
                            Metadata = managedProcess.Metadata
                        });
                    }
                }
                catch (Exception ex)
                {
                    managedProcess.EndTime = DateTime.UtcNow;
                    managedProcess.Status = ProcessStatus.Failed;
                    if (!signalReleasedState.Value)
                    {
                        _concurrencyLimiter.Release();
                        signalReleasedState.Value = true;
                    }

                    ProcessFailed?.Invoke(this, new ProcessFailedEventArgs
                    {
                        ProcessId = managedProcess.Id,
                        Error = ex,
                        StartTime = managedProcess.StartTime,
                        EndTime = managedProcess.EndTime.Value,
                        Metadata = managedProcess.Metadata
                    });
                }
            });
        }

        private void AddOutputRecord(string processId, ProcessOutputType outputType, string content)
        {
            var record = new ProcessOutputRecord
            {
                ProcessId = processId,
                OutputType = outputType,
                Content = content
            };

            // 添加到进程历史
            if (_processes.TryGetValue(processId, out var managedProcess))
            {
                managedProcess.OutputHistory.Add(record);
            }

            // 添加到全局队列
            _outputQueue.Enqueue(record);

            // 触发输出事件
            ProcessOutput?.Invoke(this, new ProcessOutputEventArgs
            {
                ProcessId = processId,
                OutputType = outputType,
                Content = content,
                Timestamp = record.Timestamp
            });
        }

        private async Task WaitProcessCompletionAsync(string processId, TimeSpan timeout)
        {
            if (!_processes.TryGetValue(processId, out var managedProcess))
                return;

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                if (managedProcess.ExecutionTask != null)
                    await managedProcess.ExecutionTask.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 超时
            }
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;

                // 停止所有运行中的进程
                var runningProcesses = _processes.Values.Where(p => p.Status == ProcessStatus.Running).ToList();
                foreach (var process in runningProcesses)
                {
                    try
                    {
                        process.CancellationTokenSource.Cancel();
                    }
                    catch
                    {
                        // 忽略释放时的错误
                    }
                }

                // 释放资源
                _concurrencyLimiter?.Dispose();
                foreach (var process in _processes.Values)
                {
                    process.CancellationTokenSource?.Dispose();
                }

                _processes.Clear();
                _disposed = true;
            }
        }

        #endregion
    }

    #region 事件参数类

    /// <summary>
    /// 进程启动事件参数
    /// </summary>
    public class ProcessStartedEventArgs : EventArgs
    {
        public string ProcessId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// 进程输出事件参数
    /// </summary>
    public class ProcessOutputEventArgs : EventArgs
    {
        public string ProcessId { get; set; } = string.Empty;
        public ProcessManager.ProcessOutputType OutputType { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 进程完成事件参数
    /// </summary>
    public class ProcessCompletedEventArgs : EventArgs
    {
        public string ProcessId { get; set; } = string.Empty;
        public ProcessResult Result { get; set; } = null!;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// 进程失败事件参数
    /// </summary>
    public class ProcessFailedEventArgs : EventArgs
    {
        public string ProcessId { get; set; } = string.Empty;
        public Exception Error { get; set; } = null!;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    #endregion

    #region 数据传输对象

    /// <summary>
    /// 进程信息
    /// </summary>
    public class ProcessInfo
    {
        public string Id { get; set; } = string.Empty;
        public ProcessManager.ProcessStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string Command { get; set; } = string.Empty;
        public ProcessResult? Result { get; set; }
        public int OutputCount { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    #endregion
}
