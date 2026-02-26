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
        var previous = _registry.GetInstance(connectionId);
        if (!string.IsNullOrWhiteSpace(previous) && !string.Equals(previous, instanceId, StringComparison.Ordinal))
        {
            await Groups.RemoveFromGroupAsync(connectionId, BuildInstanceGroup(previous));
        }

        await Groups.AddToGroupAsync(connectionId, BuildInstanceGroup(instanceId));
        _registry.Bind(connectionId, instanceId);

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
            instanceId = _registry.Unbind(connectionId) ?? string.Empty;
        }
        else
        {
            _registry.Unbind(connectionId);
        }

        if (instanceId.Length > 0)
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
        var snapshot = _manager.Resize(instanceId, cols, rows);
        if (snapshot is null)
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

            var remoteSnapshot = await RequestRemoteSyncAsync(nodeId, instanceId, new TerminalSyncRequest { Type = "screen" }, Context.ConnectionAborted);
            if (remoteSnapshot.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                await Clients.Caller.SendAsync("TerminalEvent", remoteSnapshot);
            }
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

        await Clients.Caller.SendAsync("TerminalEvent", snapshot);
    }

    public async Task RequestSync(TerminalSyncRequest request)
    {
        var instanceId = (request.InstanceId ?? string.Empty).Trim();
        if (instanceId.Length == 0)
        {
            throw new HubException("instance_id is required");
        }

        var syncType = (request.Type ?? "screen").Trim().ToLowerInvariant();
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
        var instanceId = _registry.Unbind(Context.ConnectionId);
        if (!string.IsNullOrWhiteSpace(instanceId))
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
        var syncType = (request.Type ?? "screen").Trim().ToLowerInvariant();
        var before = string.IsNullOrWhiteSpace(request.Before) ? "h-1" : request.Before;
        var limit = Math.Clamp(request.Limit ?? 50, 1, 500);
        var reqId = string.IsNullOrWhiteSpace(request.ReqId) ? $"sync-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : request.ReqId;

        var result = await _broker.SendAsync(nodeId, "instance.sync", new
        {
            instance_id = instanceId,
            type = syncType,
            before,
            limit,
            req_id = reqId
        }, cancellationToken);
        if (!result.Ok)
        {
            throw new HubException(result.Error ?? "remote sync failed");
        }

        return result.Payload;
    }
}
