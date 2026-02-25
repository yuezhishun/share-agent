namespace TerminalGateway.Api.Models;

public sealed class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
}

public sealed class OkResponse
{
    public bool Ok { get; set; } = true;
}
