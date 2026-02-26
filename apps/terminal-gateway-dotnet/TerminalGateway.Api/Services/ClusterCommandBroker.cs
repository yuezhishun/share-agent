using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using TerminalGateway.Api.Endpoints;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class ClusterCommandBroker
{
    private readonly NodeRegistry _nodes;
    private readonly IHubContext<ClusterHub> _clusterHub;
    private readonly GatewayOptions _options;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ClusterCommandResult>> _pending = new(StringComparer.Ordinal);

    public ClusterCommandBroker(NodeRegistry nodes, IHubContext<ClusterHub> clusterHub, GatewayOptions options)
    {
        _nodes = nodes;
        _clusterHub = clusterHub;
        _options = options;
    }

    public async Task<ClusterCommandResult> SendAsync(string nodeId, string commandType, object payload, CancellationToken cancellationToken)
    {
        if (!_nodes.TryGetConnectionId(nodeId, out var connectionId))
        {
            throw new InvalidOperationException($"node not connected: {nodeId}");
        }

        var commandId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<ClusterCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[commandId] = tcs;

        try
        {
            var envelope = new ClusterCommandEnvelope
            {
                CommandId = commandId,
                NodeId = nodeId,
                Type = commandType,
                Payload = JsonSerializer.SerializeToElement(payload)
            };
            await _clusterHub.Clients.Client(connectionId).SendAsync("ClusterCommand", envelope, cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(3, _options.NodeHeartbeatTimeoutSeconds)));
            var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token);
            var completed = await Task.WhenAny(tcs.Task, timeoutTask);
            if (completed != tcs.Task)
            {
                throw new TimeoutException($"cluster command timed out for node {nodeId}");
            }

            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(commandId, out _);
        }
    }

    public bool Complete(ClusterCommandResult result)
    {
        if (!_pending.TryGetValue(result.CommandId, out var tcs))
        {
            return false;
        }

        return tcs.TrySetResult(result);
    }
}
