namespace PtyAgent.Api.Infrastructure;

public sealed class RuntimeOptions
{
    // auto | nodepty | process
    public string TerminalBackend { get; set; } = "auto";
    public int PtyColumns { get; set; } = 160;
    public int PtyRows { get; set; } = 40;

    public string TerminalGatewayBaseUrl { get; set; } = "http://127.0.0.1:7300";
    public string TerminalGatewayToken { get; set; } = "dev-terminal-token";
    public int TerminalGatewayTimeoutMs { get; set; } = 5000;
}
