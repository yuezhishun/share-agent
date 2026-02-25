using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Pty;

public sealed class PtyLaunchOptions
{
    public required string Executable { get; init; }
    public required IReadOnlyList<string> Args { get; init; }
    public required string Cwd { get; init; }
    public required IDictionary<string, string> Env { get; init; }
    public required int Cols { get; init; }
    public required int Rows { get; init; }
}

public interface IPtyEngine
{
    Task<IPtyRuntimeSession> CreateAsync(PtyLaunchOptions options, CancellationToken cancellationToken = default);
}
