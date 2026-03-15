namespace ProcessRunner;

/// <summary>
/// 命令结果验证选项
/// </summary>
public enum CommandResultValidation
{
    /// <summary>
    /// 不验证退出码
    /// </summary>
    None,

    /// <summary>
    /// 验证退出码为 0，非零则抛出异常
    /// </summary>
    ZeroExitCode
}
