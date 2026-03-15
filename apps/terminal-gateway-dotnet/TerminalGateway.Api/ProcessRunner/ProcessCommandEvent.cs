using System;

namespace ProcessRunner;

/// <summary>
/// 进程命令事件基类
/// </summary>
public abstract class ProcessCommandEvent
{
    /// <summary>
    /// 命令执行上下文的快照
    /// </summary>
    public ProcessCommandContextSnapshot Context { get; set; } = null!;

    /// <summary>
    /// 事件发生时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 事件类型名称
    /// </summary>
    public virtual string EventType => GetType().Name;
}

/// <summary>
/// 进程命令结果
/// </summary>
public class ProcessResult
{
    /// <summary>
    /// 命令执行上下文的快照
    /// </summary>
    public ProcessCommandContextSnapshot Context { get; set; } = null!;

    /// <summary>
    /// 进程ID
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// 退出码
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// 标准输出
    /// </summary>
    public string StandardOutput { get; set; } = string.Empty;

    /// <summary>
    /// 标准错误
    /// </summary>
    public string StandardError { get; set; } = string.Empty;

    /// <summary>
    /// 执行完成时间
    /// </summary>
    public DateTime CompletionTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 执行持续时间
    /// </summary>
    public TimeSpan Duration => Context.Duration;

    /// <summary>
    /// 是否成功执行（退出码为0）
    /// </summary>
    public bool IsSuccess => ExitCode == 0;

    /// <summary>
    /// 是否超时
    /// </summary>
    public bool IsTimedOut => Context.IsTimedOut;
}

/// <summary>
/// 进程启动事件
/// </summary>
public class ProcessStartedEvent : ProcessCommandEvent
{
    public int ProcessId { get; set; }
}

/// <summary>
/// 标准输出事件
/// </summary>
public class StandardOutputEvent : ProcessCommandEvent
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// 标准错误事件
/// </summary>
public class StandardErrorEvent : ProcessCommandEvent
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// 进程退出事件
/// </summary>
public class ProcessExitedEvent : ProcessCommandEvent
{
    public int ExitCode { get; set; }
}

/// <summary>
/// 超时事件
/// </summary>
public class ProcessTimeoutEvent : ProcessCommandEvent
{
    public string Message { get; set; } = string.Empty;
}