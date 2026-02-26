using System.Text.Json;

namespace TerminalGateway.Api.Models;

public sealed class CreateInstanceRequest
{
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
    public int? Cols { get; set; }
    public int? Rows { get; set; }
    public string? Cwd { get; set; }
    public Dictionary<string, string>? Env { get; set; }
}

public sealed class InstanceSummary
{
    public required string Id { get; init; }
    public required string Command { get; init; }
    public required string Cwd { get; init; }
    public required int Cols { get; init; }
    public required int Rows { get; init; }
    public required string CreatedAt { get; init; }
    public required string Status { get; init; }
    public required int Clients { get; init; }
    public required string NodeId { get; init; }
    public required string NodeName { get; init; }
    public required string NodeRole { get; init; }
    public required bool NodeOnline { get; init; }
}

public abstract class WebCliClientMessage
{
    public required string Type { get; init; }
    public required string InstanceId { get; init; }

    public static WebCliClientMessage? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("v", out var v) || v.GetInt32() != 1)
            {
                return null;
            }

            var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            var instanceId = root.TryGetProperty("instance_id", out var instanceProp) ? instanceProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(instanceId))
            {
                return null;
            }

            switch (type)
            {
                case "term.stdin":
                    return root.TryGetProperty("data", out var dataProp)
                        ? new WsStdinMessage { Type = type, InstanceId = instanceId, Data = dataProp.GetString() ?? string.Empty }
                        : null;
                case "term.resize":
                    if (!root.TryGetProperty("size", out var sizeProp))
                    {
                        return null;
                    }

                    return new WsResizeMessage
                    {
                        Type = type,
                        InstanceId = instanceId,
                        ReqId = root.TryGetProperty("req_id", out var reqIdProp) ? reqIdProp.GetString() ?? string.Empty : string.Empty,
                        Cols = sizeProp.TryGetProperty("cols", out var colsProp) ? colsProp.GetInt32() : 0,
                        Rows = sizeProp.TryGetProperty("rows", out var rowsProp) ? rowsProp.GetInt32() : 0
                    };
                case "term.history.get":
                    return new WsHistoryGetMessage
                    {
                        Type = type,
                        InstanceId = instanceId,
                        ReqId = root.TryGetProperty("req_id", out var reqIdProperty) ? reqIdProperty.GetString() ?? string.Empty : string.Empty,
                        Before = root.TryGetProperty("before", out var beforeProp) ? beforeProp.GetString() ?? string.Empty : string.Empty,
                        Limit = root.TryGetProperty("limit", out var limitProp) ? limitProp.GetInt32() : 50
                    };
                case "term.resync":
                    return new WsResyncMessage { Type = type, InstanceId = instanceId };
                case "ping":
                    return new WsPingMessage
                    {
                        Type = type,
                        InstanceId = instanceId,
                        Ts = root.TryGetProperty("ts", out var tsProp) && tsProp.ValueKind == JsonValueKind.Number ? tsProp.GetInt64() : null
                    };
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }
}

public sealed class WsStdinMessage : WebCliClientMessage
{
    public required string Data { get; init; }
}

public sealed class WsResizeMessage : WebCliClientMessage
{
    public required string ReqId { get; init; }
    public required int Cols { get; init; }
    public required int Rows { get; init; }
}

public sealed class WsHistoryGetMessage : WebCliClientMessage
{
    public required string ReqId { get; init; }
    public required string Before { get; init; }
    public required int Limit { get; init; }
}

public sealed class WsResyncMessage : WebCliClientMessage;

public sealed class WsPingMessage : WebCliClientMessage
{
    public long? Ts { get; init; }
}
