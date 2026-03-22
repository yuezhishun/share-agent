using System.Text.Json;

namespace TerminalGateway.Api.Models;

public sealed class ClusterRegisterNodeRequest
{
    public string? Token { get; set; }
    public string? NodeId { get; set; }
    public string? NodeName { get; set; }
    public string? NodeLabel { get; set; }
    public string? NodeRole { get; set; }
    public int? InstanceCount { get; set; }
}

public sealed class ClusterHeartbeatRequest
{
    public string? Token { get; set; }
    public string? NodeId { get; set; }
    public int? InstanceCount { get; set; }
}

public sealed class ClusterCommandEnvelope
{
    public required string CommandId { get; set; }
    public string? NodeId { get; set; }
    public string? SourceNodeId { get; set; }
    public string? TargetNodeId { get; set; }
    public required string Type { get; set; }
    public JsonElement Payload { get; set; }
}

public sealed class ClusterCommandResult
{
    public required string CommandId { get; set; }
    public string? NodeId { get; set; }
    public string? SourceNodeId { get; set; }
    public string? TargetNodeId { get; set; }
    public required bool Ok { get; set; }
    public string? Error { get; set; }
    public JsonElement Payload { get; set; }
}

public sealed class ClusterProxyCommandRequest
{
    public string? Token { get; set; }
    public string? CommandId { get; set; }
    public string? SourceNodeId { get; set; }
    public string? TargetNodeId { get; set; }
    public string? Type { get; set; }
    public JsonElement Payload { get; set; }
}

public sealed class ClusterTerminalEventEnvelope
{
    public string? Token { get; set; }
    public string? EventId { get; set; }
    public string? NodeId { get; set; }
    public string? InstanceId { get; set; }
    public long Seq { get; set; }
    public long Ts { get; set; }
    public string? Type { get; set; }
    public JsonElement Payload { get; set; }
}

public sealed class ClusterInstanceSubscriptionRequest
{
    public string? Token { get; set; }
    public string? SourceNodeId { get; set; }
    public string? InstanceId { get; set; }
}

public sealed class ClusterNodeInstancesSyncRequest
{
    public string? Token { get; set; }
    public string? SourceNodeId { get; set; }
    public IReadOnlyList<InstanceSummary>? Items { get; set; }
}

public sealed class NodeInstanceInputRequest
{
    public string? Data { get; set; }
}

public sealed class NodeInstanceResizeRequest
{
    public int? Cols { get; set; }
    public int? Rows { get; set; }
}

public sealed class NodeSummary
{
    public required string NodeId { get; init; }
    public required string NodeName { get; init; }
    public required string NodeRole { get; init; }
    public string? NodeLabel { get; init; }
    public bool IsCurrent { get; init; }
    public required bool NodeOnline { get; init; }
    public required int InstanceCount { get; init; }
    public required string LastSeenAt { get; init; }
}
