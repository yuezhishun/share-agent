using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public sealed class TerminalHubV2 : Hub
{
    private readonly InstanceManager _manager;
    private readonly TerminalConnectionRegistryV2 _registry;
    private readonly ClusterCommandBroker _broker;
    private readonly RemoteInstanceRegistry _remoteInstances;
    private readonly GatewayOptions _options;
    private readonly TerminalOracleManager _oracle;

    public TerminalHubV2(
        InstanceManager manager,
        TerminalConnectionRegistryV2 registry,
        ClusterCommandBroker broker,
        RemoteInstanceRegistry remoteInstances,
        GatewayOptions options,
        TerminalOracleManager oracle)
    {
        _manager = manager;
        _registry = registry;
        _broker = broker;
        _remoteInstances = remoteInstances;
        _options = options;
        _oracle = oracle;
    }

    public static string BuildInstanceGroup(string instanceId) => $"instance-v2:{instanceId}";

    public async Task JoinInstance(JoinInstanceRequest request)
    {
        var instanceId = (request.InstanceId ?? string.Empty).Trim();
        if (instanceId.Length == 0)
        {
            throw new HubException("instance_id is required");
        }

        var connectionId = Context.ConnectionId;
        var joined = _registry.GetInstances(connectionId);
        if (!joined.Contains(instanceId, StringComparer.Ordinal))
        {
            await Groups.AddToGroupAsync(connectionId, BuildInstanceGroup(instanceId));
            _registry.Bind(connectionId, instanceId);
            if (string.IsNullOrWhiteSpace(_manager.GetDisplayOwner(instanceId)))
            {
                _manager.SetDisplayOwner(instanceId, connectionId);
            }
        }

        var snapshot = _oracle.BuildSnapshot(instanceId);
        if (snapshot is not null)
        {
            await Clients.Caller.SendAsync("TerminalEvent", snapshot);
            return;
        }

        if (!TryResolveRemoteNode(instanceId, out var nodeId))
        {
            throw new HubException("instance not found");
        }

        if (IsLocalNode(nodeId))
        {
            throw new HubException("instance not found");
        }

        var remoteSnapshot = await RequestRemoteSyncAsync(nodeId, instanceId, new TerminalSyncRequest { Type = "screen" }, Context.ConnectionAborted);
        if (remoteSnapshot.ValueKind == JsonValueKind.Object)
        {
            await Clients.Caller.SendAsync("TerminalEvent", remoteSnapshot);
        }
    }

    public async Task LeaveInstance(LeaveInstanceRequest request)
    {
        var instanceId = (request.InstanceId ?? string.Empty).Trim();
        var connectionId = Context.ConnectionId;
        if (instanceId.Length == 0)
        {
            var bound = _registry.UnbindAll(connectionId);
            foreach (var item in bound)
            {
                await Groups.RemoveFromGroupAsync(connectionId, BuildInstanceGroup(item));
                await ReassignDisplayOwnerAsync(item);
            }
            return;
        }

        if (_registry.Unbind(connectionId, instanceId))
        {
            await Groups.RemoveFromGroupAsync(connectionId, BuildInstanceGroup(instanceId));
            await ReassignDisplayOwnerAsync(instanceId);
        }
    }

    public async Task SendInput(TerminalInputRequest request)
    {
        var instanceId = (request.InstanceId ?? string.Empty).Trim();
        if (instanceId.Length == 0)
        {
            throw new HubException("instance_id is required");
        }

        if (!_manager.WriteStdin(instanceId, request.Data ?? string.Empty))
        {
            if (!TryResolveRemoteNode(instanceId, out var nodeId))
            {
                throw new HubException("instance not found");
            }

            if (IsLocalNode(nodeId))
            {
                throw new HubException("instance not found");
            }

            var result = await _broker.SendAsync(nodeId, "instance.input", new
            {
                instance_id = instanceId,
                data = request.Data ?? string.Empty
            }, Context.ConnectionAborted);
            if (!result.Ok)
            {
                throw new HubException(result.Error ?? "remote input failed");
            }
        }
    }

    public async Task RequestResize(TerminalResizeRequest request)
    {
        var instanceId = (request.InstanceId ?? string.Empty).Trim();
        if (instanceId.Length == 0)
        {
            throw new HubException("instance_id is required");
        }

        var cols = request.Cols ?? 0;
        var rows = request.Rows ?? 0;
        var reqId = string.IsNullOrWhiteSpace(request.ReqId) ? $"resize-v2-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : request.ReqId;
        var resized = _manager.RequestResize(instanceId, Context.ConnectionId, cols, rows);
        if (!resized.Found)
        {
            if (!TryResolveRemoteNode(instanceId, out var nodeId))
            {
                throw new HubException("instance not found");
            }

            if (IsLocalNode(nodeId))
            {
                throw new HubException("instance not found");
            }

            var result = await _broker.SendAsync(nodeId, "instance.resize", new
            {
                instance_id = instanceId,
                cols,
                rows
            }, Context.ConnectionAborted);
            if (!result.Ok)
            {
                throw new HubException(result.Error ?? "remote resize failed");
            }

            await Clients.Caller.SendAsync("TerminalEvent", BuildResizeAck(instanceId, nodeId, reqId, true, cols, rows, 0, null));
            if (TryReadRemoteSnapshot(result.Payload, out var remoteSnapshot))
            {
                await Clients.Group(BuildInstanceGroup(instanceId)).SendAsync("TerminalEvent", ConvertSnapshot(remoteSnapshot));
            }
            return;
        }

        if (!resized.Accepted)
        {
            await Clients.Caller.SendAsync("TerminalEvent", BuildResizeAck(instanceId, _options.NodeId, reqId, false, resized.Cols, resized.Rows, resized.RenderEpoch, "not_owner"));
            return;
        }

        await Clients.Caller.SendAsync("TerminalEvent", BuildResizeAck(instanceId, _options.NodeId, reqId, true, resized.Cols, resized.Rows, resized.RenderEpoch, null));
        var snapshot = _oracle.BuildSnapshot(instanceId);
        if (snapshot is not null)
        {
            await Clients.Group(BuildInstanceGroup(instanceId)).SendAsync("TerminalEvent", snapshot);
        }
    }

    public async Task RequestSync(TerminalSyncRequest request)
    {
        var instanceId = (request.InstanceId ?? string.Empty).Trim();
        if (instanceId.Length == 0)
        {
            throw new HubException("instance_id is required");
        }

        var syncType = (request.Type ?? "screen").Trim().ToLowerInvariant();
        if (syncType == "raw")
        {
            var reqId = string.IsNullOrWhiteSpace(request.ReqId) ? $"raw-sync-v2-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : request.ReqId;
            request.ReqId = reqId;
            var replay = _manager.RawReplayEvent(instanceId, request.SinceSeq is null ? null : Math.Max(0, (int)request.SinceSeq.Value), reqId);
            if (replay is not null)
            {
                await Clients.Caller.SendAsync("TerminalEvent", ConvertRawReplay(replay));
                await Clients.Caller.SendAsync("TerminalEvent", BuildRawSyncComplete(instanceId, reqId, ReadLong(JsonSerializer.SerializeToElement(replay), "to_seq")));
                return;
            }

            if (!TryResolveRemoteNode(instanceId, out var rawNodeId))
            {
                throw new HubException("instance not found");
            }

            if (IsLocalNode(rawNodeId))
            {
                throw new HubException("instance not found");
            }

            var remoteRaw = await RequestRemoteSyncAsync(rawNodeId, instanceId, request, Context.ConnectionAborted);
            if (!IsRawEvent(remoteRaw))
            {
                throw new HubException("remote raw sync failed");
            }

            await Clients.Caller.SendAsync("TerminalEvent", ConvertRawReplay(remoteRaw));
            await Clients.Caller.SendAsync("TerminalEvent", BuildRawSyncComplete(instanceId, reqId, ReadLong(remoteRaw, "to_seq") ?? ReadLong(remoteRaw, "seq")));
            return;
        }

        var snapshot = _oracle.BuildSnapshot(instanceId);
        if (snapshot is not null)
        {
            await Clients.Caller.SendAsync("TerminalEvent", snapshot);
            return;
        }

        if (!TryResolveRemoteNode(instanceId, out var nodeId))
        {
            throw new HubException("instance not found");
        }

        if (IsLocalNode(nodeId))
        {
            throw new HubException("instance not found");
        }

        var remoteSnapshot = await RequestRemoteSyncAsync(nodeId, instanceId, request, Context.ConnectionAborted);
        if (remoteSnapshot.ValueKind != JsonValueKind.Object)
        {
            throw new HubException("remote screen sync failed");
        }
        await Clients.Caller.SendAsync("TerminalEvent", remoteSnapshot);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var bound = _registry.UnbindAll(Context.ConnectionId);
        foreach (var instanceId in bound)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildInstanceGroup(instanceId));
            await ReassignDisplayOwnerAsync(instanceId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task ReassignDisplayOwnerAsync(string instanceId)
    {
        var currentOwner = _manager.GetDisplayOwner(instanceId);
        if (!string.Equals(currentOwner, Context.ConnectionId, StringComparison.Ordinal))
        {
            return;
        }

        var nextOwner = _registry.GetConnections(instanceId).FirstOrDefault();
        _manager.SetDisplayOwner(instanceId, nextOwner);
    }

    private bool TryResolveRemoteNode(string instanceId, out string nodeId)
    {
        if (_remoteInstances.TryGetNode(instanceId, out nodeId))
        {
            return true;
        }

        nodeId = string.Empty;
        return false;
    }

    private bool IsLocalNode(string nodeId)
    {
        return string.Equals((nodeId ?? string.Empty).Trim(), _options.NodeId, StringComparison.Ordinal);
    }

    private async Task<JsonElement> RequestRemoteSyncAsync(string nodeId, string instanceId, TerminalSyncRequest request, CancellationToken cancellationToken)
    {
        var syncType = (request.Type ?? "screen").Trim().ToLowerInvariant();
        var reqId = string.IsNullOrWhiteSpace(request.ReqId) ? $"sync-v2-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : request.ReqId;
        var sinceSeq = request.SinceSeq is null
            ? (long?)null
            : Math.Max(0, request.SinceSeq.Value);

        var result = await _broker.SendAsync(nodeId, "instance.sync", new
        {
            instance_id = instanceId,
            type = syncType,
            req_id = reqId,
            since_seq = sinceSeq,
            protocol = "v2"
        }, cancellationToken);
        if (!result.Ok)
        {
            throw new HubException(result.Error ?? "remote sync failed");
        }

        return result.Payload;
    }

    private static object BuildResizeAck(string instanceId, string nodeId, string reqId, bool accepted, int cols, int rows, long renderEpoch, string? reason)
    {
        return new
        {
            v = 2,
            type = "term.v2.resize.ack",
            instance_id = instanceId,
            node_id = nodeId,
            node_name = nodeId,
            req_id = reqId,
            accepted,
            reason,
            size = new { cols, rows },
            render_epoch = renderEpoch,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static object ConvertRawReplay(object replay)
    {
        var element = JsonSerializer.SerializeToElement(replay);
        return new
        {
            v = 2,
            type = "term.v2.raw",
            instance_id = element.TryGetProperty("instance_id", out var instanceId) ? instanceId.GetString() : string.Empty,
            node_id = element.TryGetProperty("node_id", out var nodeId) ? nodeId.GetString() : string.Empty,
            node_name = element.TryGetProperty("node_name", out var nodeName) ? nodeName.GetString() : string.Empty,
            ts = element.TryGetProperty("ts", out var ts) && ts.TryGetInt64(out var tsNumber) ? tsNumber : 0,
            replay = true,
            req_id = element.TryGetProperty("req_id", out var reqId) ? reqId.GetString() : string.Empty,
            since_seq = element.TryGetProperty("since_seq", out var sinceSeq) && sinceSeq.TryGetInt64(out var sinceSeqNumber) ? sinceSeqNumber : 0,
            from_seq = element.TryGetProperty("from_seq", out var fromSeq) && fromSeq.TryGetInt64(out var fromSeqNumber) ? fromSeqNumber : 0,
            to_seq = element.TryGetProperty("to_seq", out var toSeq) && toSeq.TryGetInt64(out var toSeqNumber) ? toSeqNumber : 0,
            seq = element.TryGetProperty("seq", out var seq) && seq.TryGetInt64(out var seqNumber) ? seqNumber : 0,
            reset = element.TryGetProperty("reset", out var reset) && reset.ValueKind == JsonValueKind.True,
            truncated = element.TryGetProperty("truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True,
            data = element.TryGetProperty("data", out var data) ? data.GetString() ?? string.Empty : string.Empty
        };
    }

    private static object BuildRawSyncComplete(string instanceId, string reqId, long? toSeq)
    {
        return new
        {
            v = 2,
            type = "term.v2.sync.complete",
            instance_id = instanceId,
            req_id = reqId,
            to_seq = toSeq ?? 0,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static bool IsRawEvent(JsonElement payload)
    {
        return payload.ValueKind == JsonValueKind.Object
            && string.Equals(ReadString(payload, "type"), "term.raw", StringComparison.Ordinal);
    }

    private static bool TryReadRemoteSnapshot(JsonElement payload, out JsonElement snapshot)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("snapshot", out snapshot)
            && snapshot.ValueKind == JsonValueKind.Object
            && snapshot.TryGetProperty("type", out var typeValue)
            && typeValue.ValueKind == JsonValueKind.String
            && string.Equals(typeValue.GetString(), "term.snapshot", StringComparison.Ordinal))
        {
            return true;
        }

        snapshot = default;
        return false;
    }

    private static object ConvertSnapshot(JsonElement payload)
    {
        return new
        {
            v = 2,
            type = "term.v2.snapshot",
            instance_id = payload.TryGetProperty("instance_id", out var instanceId) ? instanceId.GetString() : string.Empty,
            node_id = payload.TryGetProperty("node_id", out var nodeId) ? nodeId.GetString() : string.Empty,
            node_name = payload.TryGetProperty("node_name", out var nodeName) ? nodeName.GetString() : string.Empty,
            seq = payload.TryGetProperty("seq", out var seq) && seq.TryGetInt64(out var seqNumber) ? seqNumber : 0,
            ts = payload.TryGetProperty("ts", out var ts) && ts.TryGetInt64(out var tsNumber) ? tsNumber : 0,
            size = payload.TryGetProperty("size", out var size) ? size : default,
            cursor = payload.TryGetProperty("cursor", out var cursor) ? cursor : default,
            render_epoch = payload.TryGetProperty("render_epoch", out var renderEpoch) && renderEpoch.TryGetInt64(out var renderEpochNumber) ? renderEpochNumber : 0,
            instance_epoch = payload.TryGetProperty("instance_epoch", out var instanceEpoch) && instanceEpoch.TryGetInt64(out var instanceEpochNumber) ? instanceEpochNumber : 0,
            rows = payload.TryGetProperty("rows", out var rows) ? rows : default
        };
    }

    private static string? ReadString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? ReadLong(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt64(),
            JsonValueKind.String when long.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }
}
