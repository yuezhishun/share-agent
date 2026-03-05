using Microsoft.AspNetCore.SignalR;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public sealed class TerminalHub : Hub
{
    private readonly InstanceManager _manager;
    private readonly TerminalConnectionRegistry _registry;
    private readonly ClusterCommandBroker _broker;
    private readonly RemoteInstanceRegistry _remoteInstances;
    private readonly GatewayOptions _options;

    public TerminalHub(
        InstanceManager manager,
        TerminalConnectionRegistry registry,
        ClusterCommandBroker broker,
        RemoteInstanceRegistry remoteInstances,
        GatewayOptions options)
    {
        _manager = manager;
        _registry = registry;
        _broker = broker;
        _remoteInstances = remoteInstances;
        _options = options;
    }

    public static string BuildInstanceGroup(string instanceId) => $"instance:{instanceId}";

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
        }

        var snapshot = _manager.Snapshot(instanceId, advanceSeq: false);
        if (snapshot is not null)
        {
            await Clients.Caller.SendAsync("TerminalEvent", snapshot);
            return;
        }

        if (!TryResolveRemoteNode(instanceId, out var nodeId))
        {
            throw new HubException("instance not found");
        }

        var remoteSnapshot = await RequestRemoteSyncAsync(nodeId, instanceId, new TerminalSyncRequest { Type = "screen" }, Context.ConnectionAborted);
        if (remoteSnapshot.ValueKind == System.Text.Json.JsonValueKind.Object)
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
            }
            return;
        }

        if (_registry.Unbind(connectionId, instanceId))
        {
            await Groups.RemoveFromGroupAsync(connectionId, BuildInstanceGroup(instanceId));
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
        var resized = _manager.Resize(instanceId, cols, rows);
        if (resized is null)
        {
            if (!TryResolveRemoteNode(instanceId, out var nodeId))
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

            await Clients.Caller.SendAsync("TerminalEvent", new
            {
                v = 1,
                type = "term.resize.ack",
                instance_id = instanceId,
                node_id = nodeId,
                node_name = nodeId,
                req_id = string.IsNullOrWhiteSpace(request.ReqId) ? $"resize-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : request.ReqId,
                size = new { cols, rows },
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            return;
        }

        await Clients.Caller.SendAsync("TerminalEvent", new
        {
            v = 1,
            type = "term.resize.ack",
            instance_id = instanceId,
            node_id = _options.NodeId,
            node_name = _options.NodeName,
            req_id = string.IsNullOrWhiteSpace(request.ReqId) ? $"resize-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : request.ReqId,
            size = new { cols, rows },
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    public async Task RequestSync(TerminalSyncRequest request)
    {
        var instanceId = (request.InstanceId ?? string.Empty).Trim();
        if (instanceId.Length == 0)
        {
            throw new HubException("instance_id is required");
        }

        var syncType = (request.Type ?? "raw").Trim().ToLowerInvariant();
        if (syncType == "raw")
        {
            var reqId = string.IsNullOrWhiteSpace(request.ReqId) ? $"raw-sync-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : request.ReqId;
            request.ReqId = reqId;
            var sinceSeq = request.SinceSeq is null
                ? (int?)null
                : Math.Max(0, (int)Math.Min(int.MaxValue, request.SinceSeq.Value));
            var replay = _manager.RawReplayEvent(instanceId, sinceSeq, reqId);
            if (replay is not null)
            {
                await Clients.Caller.SendAsync("TerminalEvent", replay);
                await Clients.Caller.SendAsync("TerminalEvent", BuildRawSyncComplete(instanceId, reqId, ReadLong(System.Text.Json.JsonSerializer.SerializeToElement(replay), "to_seq")));
                return;
            }

            if (!TryResolveRemoteNode(instanceId, out var nodeId))
            {
                throw new HubException("instance not found");
            }

            var remoteRaw = await RequestRemoteSyncAsync(nodeId, instanceId, request, Context.ConnectionAborted);
            if (IsRawEvent(remoteRaw))
            {
                await Clients.Caller.SendAsync("TerminalEvent", remoteRaw);
                await Clients.Caller.SendAsync("TerminalEvent", BuildRawSyncComplete(instanceId, reqId, ReadLong(remoteRaw, "to_seq") ?? ReadLong(remoteRaw, "seq")));
                return;
            }

            throw new HubException("remote raw sync failed");
        }

        if (syncType == "history")
        {
            var before = string.IsNullOrWhiteSpace(request.Before) ? "h-1" : request.Before;
            var limit = Math.Clamp(request.Limit ?? 50, 1, 500);
            var reqId = string.IsNullOrWhiteSpace(request.ReqId) ? $"history-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : request.ReqId;
            var chunk = _manager.HistoryChunk(instanceId, reqId, before, limit);
            if (chunk is null)
            {
                if (!TryResolveRemoteNode(instanceId, out var nodeId))
                {
                    throw new HubException("instance not found");
                }

                var remoteChunk = await RequestRemoteSyncAsync(nodeId, instanceId, request, Context.ConnectionAborted);
                if (remoteChunk.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    throw new HubException("remote history sync failed");
                }
                await Clients.Caller.SendAsync("TerminalEvent", remoteChunk);
                return;
            }

            await Clients.Caller.SendAsync("TerminalEvent", chunk);
            return;
        }

        var snapshot = _manager.Snapshot(instanceId, advanceSeq: false);
        if (snapshot is null)
        {
            if (!TryResolveRemoteNode(instanceId, out var nodeId))
            {
                throw new HubException("instance not found");
            }

            var remoteSnapshot = await RequestRemoteSyncAsync(nodeId, instanceId, request, Context.ConnectionAborted);
            if (remoteSnapshot.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new HubException("remote screen sync failed");
            }
            await Clients.Caller.SendAsync("TerminalEvent", remoteSnapshot);
            return;
        }

        await Clients.Caller.SendAsync("TerminalEvent", snapshot);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var bound = _registry.UnbindAll(Context.ConnectionId);
        foreach (var instanceId in bound)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildInstanceGroup(instanceId));
        }

        await base.OnDisconnectedAsync(exception);
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

    private async Task<System.Text.Json.JsonElement> RequestRemoteSyncAsync(string nodeId, string instanceId, TerminalSyncRequest request, CancellationToken cancellationToken)
    {
        var syncType = (request.Type ?? "raw").Trim().ToLowerInvariant();
        var before = string.IsNullOrWhiteSpace(request.Before) ? "h-1" : request.Before;
        var limit = Math.Clamp(request.Limit ?? 50, 1, 500);
        var reqId = string.IsNullOrWhiteSpace(request.ReqId) ? $"sync-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : request.ReqId;
        var sinceSeq = request.SinceSeq is null
            ? (long?)null
            : Math.Max(0, request.SinceSeq.Value);

        var result = await _broker.SendAsync(nodeId, "instance.sync", new
        {
            instance_id = instanceId,
            type = syncType,
            before,
            limit,
            req_id = reqId,
            since_seq = sinceSeq
        }, cancellationToken);
        if (!result.Ok)
        {
            throw new HubException(result.Error ?? "remote sync failed");
        }

        return result.Payload;
    }

    private static bool IsRawEvent(System.Text.Json.JsonElement payload)
    {
        if (payload.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return false;
        }

        if (!string.Equals(ReadString(payload, "type"), "term.raw", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static object BuildRawSyncComplete(string instanceId, string reqId, long? toSeq)
    {
        return new
        {
            v = 1,
            type = "term.sync.complete",
            instance_id = instanceId,
            req_id = reqId,
            to_seq = toSeq ?? 0,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static string? ReadString(System.Text.Json.JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? ReadLong(System.Text.Json.JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => value.GetInt64(),
            System.Text.Json.JsonValueKind.String when long.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }
}
