using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerminalGateway.Api.Models;

public sealed class AgentBackendDescriptor
{
    public required string Backend { get; init; }
    public required string Name { get; init; }
    public string? CliCommand { get; init; }
    public IReadOnlyList<string> AcpArgs { get; init; } = [];
    public bool Enabled { get; init; }
    public bool SupportsStreaming { get; init; }
    public bool AuthRequired { get; init; }
    public bool RequiresCustomTransport { get; init; }
}

public sealed class AgentHealthResult
{
    public required string Backend { get; init; }
    public required bool Available { get; init; }
    public string? Message { get; init; }
    public string? ResolvedCommand { get; init; }
}

public sealed class AgentSessionConnectRequest
{
    public string? NodeId { get; set; }
    [JsonPropertyName("nodeId")]
    public string? NodeIdCamel { set => NodeId = value; }
    public string? ConversationId { get; set; }
    [JsonPropertyName("conversationId")]
    public string? ConversationIdCamel { set => ConversationId = value; }
    public string? Backend { get; set; }
    public string? CliPath { get; set; }
    [JsonPropertyName("cliPath")]
    public string? CliPathCamel { set => CliPath = value; }
    public string? WorkingDirectory { get; set; }
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectoryCamel { set => WorkingDirectory = value; }
    public List<string>? ExtraArgs { get; set; }
    [JsonPropertyName("extraArgs")]
    public List<string>? ExtraArgsCamel { set => ExtraArgs = value; }
    public Dictionary<string, string>? Environment { get; set; }
    public string? ResumeSessionId { get; set; }
    [JsonPropertyName("resumeSessionId")]
    public string? ResumeSessionIdCamel { set => ResumeSessionId = value; }
    public string? SessionMode { get; set; }
    [JsonPropertyName("sessionMode")]
    public string? SessionModeCamel { set => SessionMode = value; }
    public string? ModelId { get; set; }
    [JsonPropertyName("modelId")]
    public string? ModelIdCamel { set => ModelId = value; }
    public bool InitializeOnly { get; set; }
    [JsonPropertyName("initializeOnly")]
    public bool InitializeOnlyCamel { set => InitializeOnly = value; }
}

public sealed class AgentSessionPromptRequest
{
    public string? Text { get; set; }
}

public sealed class AgentSessionModeRequest
{
    public string? Mode { get; set; }
}

public sealed class AgentSessionModelRequest
{
    public string? ModelId { get; set; }
    [JsonPropertyName("modelId")]
    public string? ModelIdCamel { set => ModelId = value; }
}

public sealed class AgentPermissionResponseRequest
{
    public string? RequestId { get; set; }
    [JsonPropertyName("requestId")]
    public string? RequestIdCamel { set => RequestId = value; }
    public string? OptionId { get; set; }
    [JsonPropertyName("optionId")]
    public string? OptionIdCamel { set => OptionId = value; }
    public JsonElement Payload { get; set; }
}

public sealed class AgentConfigOptionRequest
{
    public string? ConfigId { get; set; }
    [JsonPropertyName("configId")]
    public string? ConfigIdCamel { set => ConfigId = value; }
    public JsonElement Value { get; set; }
}

public sealed class AgentSessionSummary
{
    public required string GatewaySessionId { get; init; }
    public required string ConversationId { get; init; }
    public required string Backend { get; init; }
    public required string NodeId { get; init; }
    public required string Status { get; init; }
    public string? SessionId { get; init; }
    public string? WorkingDirectory { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int PendingPermissionCount { get; init; }
}

public sealed class AgentGatewayEventEnvelope
{
    public required string GatewaySessionId { get; init; }
    public required string EventType { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public JsonElement Payload { get; init; }
}

public sealed class AgentGatewayHubHandshakeRequest
{
    public string? GatewaySessionId { get; set; }
}
