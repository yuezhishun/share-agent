using System.Collections.Concurrent;
using System.Net.WebSockets;
using TerminalGateway.Api.Pty;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Models;

public sealed class SessionRecord
{
    public required string SessionId { get; init; }
    public required string TaskId { get; init; }
    public required string CliType { get; init; }
    public required string Mode { get; init; }
    public string? ProfileId { get; init; }
    public required string Title { get; init; }
    public required string Shell { get; init; }
    public required string Cwd { get; init; }
    public required List<string> Args { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset LastActivityAt { get; set; }
    public string Status { get; set; } = "running";
    public int? ExitCode { get; set; }
    public string? WriteTokenHash { get; init; }
    public WebSocket? WriterPeer { get; set; }
    public bool OutputTruncated { get; set; }
    public required int MaxOutputBufferBytes { get; init; }
    public required SessionReplayBuffer ReplayBuffer { get; init; }
    public required IPtyRuntimeSession PtySession { get; init; }
    public HashSet<WebSocket> Subscribers { get; } = [];
    public object Sync { get; } = new();
}
