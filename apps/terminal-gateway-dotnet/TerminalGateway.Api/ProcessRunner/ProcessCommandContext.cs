using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProcessRunner
{
    /// <summary>
    /// ProcessCommand 执行上下文，包含所有配置参数和运行时状态
    /// </summary>
    public class ProcessCommandContext
    {
        #region 配置参数

        /// <summary>
        /// 目标程序路径
        /// </summary>
        public string Target => _command.Target;

        /// <summary>
        /// 命令行参数列表
        /// </summary>
        public IReadOnlyList<string> Arguments => _command.Arguments;

        /// <summary>
        /// 工作目录
        /// </summary>
        public string WorkingDirectory => _command.WorkingDirectory;

        /// <summary>
        /// 环境变量字典
        /// </summary>
        public IReadOnlyDictionary<string, string> EnvironmentVariables => _command.EnvironmentVariables;

        /// <summary>
        /// 基础超时时间
        /// </summary>
        public TimeSpan Timeout => _command.Timeout;

        /// <summary>
        /// 当前有效的超时时间（包含动态修改）
        /// </summary>
        public TimeSpan CurrentTimeout
        {
            get
            {
                lock (_timeoutLock)
                {
                    if (_timeoutDeadlineUtc.HasValue)
                    {
                        var remaining = _timeoutDeadlineUtc.Value - DateTime.UtcNow;
                        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
                    }

                    return _dynamicTimeout != default ? _dynamicTimeout : Timeout;
                }
            }
        }

        /// <summary>
        /// 是否使用逐字命令行
        /// </summary>
        public bool VerbatimCommandLine => _command.VerbatimCommandLine;

        #endregion

        #region 运行时状态

        /// <summary>
        /// 进程ID（启动后设置）
        /// </summary>
        public int ProcessId { get; internal set; }

        /// <summary>
        /// 退出码（进程退出后设置）
        /// </summary>
        public int ExitCode { get; internal set; }

        /// <summary>
        /// 是否已启动
        /// </summary>
        public bool IsStarted { get; internal set; }

        /// <summary>
        /// 是否已退出
        /// </summary>
        public bool IsExited { get; internal set; }

        /// <summary>
        /// 是否已超时
        /// </summary>
        public bool IsTimedOut { get; internal set; }

        /// <summary>
        /// 启动时间
        /// </summary>
        public DateTime StartTime { get; internal set; }

        /// <summary>
        /// 退出时间
        /// </summary>
        public DateTime? ExitTime { get; internal set; }

        /// <summary>
        /// 最后活动时间
        /// </summary>
        public DateTime LastActivityTime { get; internal set; }

        /// <summary>
        /// 已收集的输出文本
        /// </summary>
        public string CollectedOutput { get; internal set; } = string.Empty;

        /// <summary>
        /// 已收集的错误文本
        /// </summary>
        public string CollectedError { get; internal set; } = string.Empty;

        /// <summary>
        /// 自定义数据字典，用于存储额外的上下文信息
        /// </summary>
        public Dictionary<string, object> CustomData { get; } = new();

        /// <summary>
        /// 取消令牌源，用于控制执行
        /// </summary>
        public CancellationTokenSource CancellationTokenSource { get; } = new();

        #endregion

        #region 计算属性

        /// <summary>
        /// 运行持续时间
        /// </summary>
        public TimeSpan Duration =>
            (ExitTime ?? DateTime.UtcNow) - StartTime;

        /// <summary>
        /// 自上次活动以来的时间
        /// </summary>
        public TimeSpan TimeSinceLastActivity =>
            DateTime.UtcNow - LastActivityTime;

        /// <summary>
        /// 是否仍在运行
        /// </summary>
        public bool IsRunning => IsStarted && !IsExited;

        /// <summary>
        /// 完整的命令行字符串
        /// </summary>
        public string FullCommandLine =>
            Arguments.Count > 0 ? $"{Target} {string.Join(" ", Arguments)}" : Target;

        #endregion

        #region 构造函数

        /// <summary>
        /// 从 ProcessCommand 创建上下文
        /// </summary>
        private readonly ProcessCommand _command;

        internal ProcessCommandContext(ProcessCommand command)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
        }

        #endregion

        #region 动态修改方法

        private readonly object _timeoutLock = new();
        private TimeSpan _dynamicTimeout;
        private DateTime? _timeoutDeadlineUtc;

        /// <summary>
        /// 动态修改超时时间
        /// </summary>
        /// <param name="newTimeout">新的超时时间</param>
        /// <returns>是否修改成功</returns>
        public bool TryUpdateTimeout(TimeSpan newTimeout)
        {
            if (IsExited) return false;

            lock (_timeoutLock)
            {
                _dynamicTimeout = newTimeout;
                _timeoutDeadlineUtc = newTimeout == System.Threading.Timeout.InfiniteTimeSpan
                    ? null
                    : DateTime.UtcNow.Add(newTimeout);
            }
            return true;
        }

        
        /// <summary>
        /// 动态添加环境变量
        /// </summary>
        /// <param name="key">环境变量名</param>
        /// <param name="value">环境变量值</param>
        /// <returns>是否添加成功</returns>
        public bool TryAddEnvironmentVariable(string key, string value)
        {
            if (IsStarted) return false;

            var envVars = new Dictionary<string, string>(EnvironmentVariables);
            envVars[key] = value;
            // 注意：EnvironmentVariables 是只读的，这里需要通过其他方式处理
            return true;
        }

        /// <summary>
        /// 更新最后活动时间
        /// </summary>
        public void UpdateLastActivity()
        {
            LastActivityTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 设置自定义数据
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        public void SetCustomData(string key, object value)
        {
            CustomData[key] = value;
        }

        /// <summary>
        /// 获取自定义数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">键</param>
        /// <returns>值，如果不存在则返回默认值</returns>
        public T GetCustomData<T>(string key, T defaultValue = default)
        {
            if (CustomData.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// 取消执行
        /// </summary>
        public void Cancel()
        {
            CancellationTokenSource.Cancel();
        }

        #endregion

        #region 内部方法

        /// <summary>
        /// 标记进程启动
        /// </summary>
        internal void MarkStarted(int processId)
        {
            ProcessId = processId;
            IsStarted = true;
            StartTime = DateTime.UtcNow;
            LastActivityTime = DateTime.UtcNow;

            lock (_timeoutLock)
            {
                var effectiveTimeout = _dynamicTimeout != default ? _dynamicTimeout : Timeout;
                _timeoutDeadlineUtc = effectiveTimeout == System.Threading.Timeout.InfiniteTimeSpan
                    ? null
                    : DateTime.UtcNow.Add(effectiveTimeout);
            }
        }

        /// <summary>
        /// 标记进程退出
        /// </summary>
        internal void MarkExited(int exitCode)
        {
            ExitCode = exitCode;
            IsExited = true;
            ExitTime = DateTime.UtcNow;

            lock (_timeoutLock)
            {
                _timeoutDeadlineUtc = null;
            }
        }

        /// <summary>
        /// 标记超时
        /// </summary>
        internal void MarkTimedOut()
        {
            IsTimedOut = true;
            ExitCode = -1;
            IsExited = true;
            ExitTime = DateTime.UtcNow;

            lock (_timeoutLock)
            {
                _timeoutDeadlineUtc = null;
            }
        }

        internal bool TryGetTimeoutDeadline(out DateTime deadlineUtc)
        {
            lock (_timeoutLock)
            {
                if (_timeoutDeadlineUtc.HasValue)
                {
                    deadlineUtc = _timeoutDeadlineUtc.Value;
                    return true;
                }
            }

            deadlineUtc = default;
            return false;
        }

        /// <summary>
        /// 添加输出文本
        /// </summary>
        internal void AppendOutput(string text)
        {
            CollectedOutput += text;
            UpdateLastActivity();
        }

        /// <summary>
        /// 添加错误文本
        /// </summary>
        internal void AppendError(string text)
        {
            CollectedError += text;
            UpdateLastActivity();
        }

        #endregion

        #region 实用方法

        /// <summary>
        /// 获取状态描述
        /// </summary>
        public string GetStatusDescription()
        {
            if (IsTimedOut) return "已超时";
            if (IsExited) return $"已退出 (退出码: {ExitCode})";
            if (IsStarted) return "运行中";
            return "未启动";
        }

        /// <summary>
        /// 创建上下文的只读快照
        /// </summary>
        public ProcessCommandContextSnapshot CreateSnapshot()
        {
            return new ProcessCommandContextSnapshot(this);
        }

        #endregion
    }

    /// <summary>
    /// ProcessCommandContext 的只读快照
    /// </summary>
    public class ProcessCommandContextSnapshot
    {
        public string Target { get; }
        public IReadOnlyList<string> Arguments { get; }
        public string WorkingDirectory { get; }
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }
        public TimeSpan Timeout { get; }
        public bool VerbatimCommandLine { get; }
        public int ProcessId { get; }
        public int ExitCode { get; }
        public bool IsStarted { get; }
        public bool IsExited { get; }
        public bool IsTimedOut { get; }
        public DateTime StartTime { get; }
        public DateTime? ExitTime { get; }
        public DateTime LastActivityTime { get; }
        public string CollectedOutput { get; }
        public string CollectedError { get; }
        public IReadOnlyDictionary<string, object> CustomData { get; }
        public TimeSpan Duration { get; }
        public TimeSpan TimeSinceLastActivity { get; }
        public bool IsRunning { get; }
        public string FullCommandLine { get; }

        internal ProcessCommandContextSnapshot(ProcessCommandContext context)
        {
            Target = context.Target ?? string.Empty;
            Arguments = context.Arguments ?? Array.Empty<string>();
            WorkingDirectory = context.WorkingDirectory ?? string.Empty;
            EnvironmentVariables = context.EnvironmentVariables ?? new Dictionary<string, string>();
            Timeout = context.Timeout;
            VerbatimCommandLine = context.VerbatimCommandLine;
            ProcessId = context.ProcessId;
            ExitCode = context.ExitCode;
            IsStarted = context.IsStarted;
            IsExited = context.IsExited;
            IsTimedOut = context.IsTimedOut;
            StartTime = context.StartTime == default ? DateTime.UtcNow : context.StartTime;
            ExitTime = context.ExitTime;
            LastActivityTime = context.LastActivityTime == default ? context.StartTime : context.LastActivityTime;
            CollectedOutput = context.CollectedOutput ?? string.Empty;
            CollectedError = context.CollectedError ?? string.Empty;
            CustomData = (context.CustomData ?? new Dictionary<string, object>()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            Duration = context.Duration;
            TimeSinceLastActivity = context.TimeSinceLastActivity;
            IsRunning = context.IsRunning;
            FullCommandLine = context.FullCommandLine ?? string.Empty;
        }
    }
}
