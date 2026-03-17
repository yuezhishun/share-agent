using System;

namespace ProcessRunner;

/// <summary>
/// 进程执行异常
/// </summary>
public class ProcessExecutionException : Exception
{
    /// <summary>
    /// 执行结果
    /// </summary>
    public ProcessResult Result { get; }

    /// <summary>
    /// 退出码
    /// </summary>
    public int ExitCode => Result.ExitCode;

    /// <summary>
    /// 标准输出
    /// </summary>
    public string StandardOutput => Result.StandardOutput;

    /// <summary>
    /// 标准错误
    /// </summary>
    public string StandardError => Result.StandardError;

    /// <summary>
    /// 创建进程执行异常
    /// </summary>
    public ProcessExecutionException(string message, ProcessResult result)
        : base(message)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    /// <summary>
    /// 创建进程执行异常
    /// </summary>
    public ProcessExecutionException(string message, ProcessResult result, Exception innerException)
        : base(message, innerException)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }
}
