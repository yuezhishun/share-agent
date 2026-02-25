using System.Text.Json.Serialization;

namespace TerminalGateway.Api.Models;

public sealed class WsClientMessage
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("cols")]
    public int? Cols { get; set; }

    [JsonPropertyName("rows")]
    public int? Rows { get; set; }

    [JsonPropertyName("ts")]
    public long? Ts { get; set; }
}
