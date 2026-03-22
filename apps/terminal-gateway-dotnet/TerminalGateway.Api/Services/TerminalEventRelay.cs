using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using TerminalGateway.Api.Endpoints;

namespace TerminalGateway.Api.Services;

public sealed class TerminalEventRelay
{
    private readonly IHubContext<TerminalHub> _hub;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _instanceGates = new(StringComparer.Ordinal);

    public TerminalEventRelay(InstanceManager manager, IHubContext<TerminalHub> hub)
    {
        _hub = hub;

        manager.Raw += (instanceId, payload) => Enqueue(instanceId, ConvertPayload(payload));
        manager.Exited += (instanceId, payload) => Enqueue(instanceId, ConvertPayload(payload));
        manager.StateChanged += (instanceId, payload) => Enqueue(instanceId, ConvertPayload(payload));
    }

    private void Enqueue(string instanceId, object? payload)
    {
        if (payload is null)
        {
            return;
        }

        _ = EnqueueAsync(instanceId, payload);
    }

    private async Task EnqueueAsync(string instanceId, object payload)
    {
        var gate = _instanceGates.GetOrAdd(instanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await _hub.Clients.Group(TerminalHub.BuildInstanceGroup(instanceId)).SendAsync("TerminalEvent", payload);
        }
        catch
        {
        }
        finally
        {
            gate.Release();
        }
    }

    private static object? ConvertPayload(object payload)
    {
        var element = JsonSerializer.SerializeToElement(payload);
        var type = element.TryGetProperty("type", out var typeValue) && typeValue.ValueKind == JsonValueKind.String
            ? typeValue.GetString()
            : string.Empty;

        if (string.Equals(type, "term.raw", StringComparison.Ordinal))
        {
            return new
            {
                v = 1,
                type = "term.raw",
                instance_id = ReadString(element, "instance_id"),
                node_id = ReadString(element, "node_id"),
                node_name = ReadString(element, "node_name"),
                seq = ReadInt(element, "seq"),
                ts = ReadLong(element, "ts"),
                replay = false,
                data = ReadString(element, "data") ?? string.Empty
            };
        }

        if (string.Equals(type, "term.owner.changed", StringComparison.Ordinal))
        {
            return new
            {
                v = 1,
                type = "term.owner.changed",
                instance_id = ReadString(element, "instance_id"),
                node_id = ReadString(element, "node_id"),
                node_name = ReadString(element, "node_name"),
                owner_connection_id = ReadString(element, "owner_connection_id"),
                render_epoch = ReadLong(element, "render_epoch"),
                instance_epoch = ReadLong(element, "instance_epoch"),
                ts = ReadLong(element, "ts")
            };
        }

        return payload;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static long ReadLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number)
            ? number
            : 0;
    }
}
