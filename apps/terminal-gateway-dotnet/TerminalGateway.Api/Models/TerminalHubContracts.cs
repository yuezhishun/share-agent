namespace TerminalGateway.Api.Models;

public sealed class JoinInstanceRequest
{
    public string? InstanceId { get; set; }
}

public sealed class LeaveInstanceRequest
{
    public string? InstanceId { get; set; }
}

public sealed class TerminalInputRequest
{
    public string? InstanceId { get; set; }
    public string? Data { get; set; }
}

public sealed class TerminalResizeRequest
{
    public string? InstanceId { get; set; }
    public int? Cols { get; set; }
    public int? Rows { get; set; }
    public string? ReqId { get; set; }
}

public sealed class TerminalSyncRequest
{
    public string? InstanceId { get; set; }
    public string? Type { get; set; }
    public string? Before { get; set; }
    public int? Limit { get; set; }
    public string? ReqId { get; set; }
}
