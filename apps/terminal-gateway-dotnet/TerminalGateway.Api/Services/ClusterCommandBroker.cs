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
    private readonly ConcurrentDictionary<string, PendingCommand> _pending = new(StringComparer.Ordinal);

    public ClusterCommandBroker(NodeRegistry nodes, IHubContext<ClusterHub> clusterHub, GatewayOptions options)
    {
        _nodes = nodes;
        _clusterHub = clusterHub;
        _options = options;
    }

    public Task<ClusterCommandResult> SendAsync(string nodeId, string commandType, object payload, CancellationToken cancellationToken)
    {
        return SendAsync(_options.NodeId, nodeId, commandType, payload, cancellationToken);
    }

    public async Task<ClusterCommandResult> SendAsync(string sourceNodeId, string targetNodeId, string commandType, object payload, CancellationToken cancellationToken)
    {
        if (!_nodes.TryGetConnectionId(targetNodeId, out var connectionId))
        {
            throw new InvalidOperationException($"node not connected: {targetNodeId}");
        }

        var commandId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<ClusterCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[commandId] = new PendingCommand(
            NormalizeNodeId(sourceNodeId, _options.NodeId),
            NormalizeNodeId(targetNodeId, string.Empty),
            tcs);

        try
        {
            var envelope = new ClusterCommandEnvelope
            {
                CommandId = commandId,
                NodeId = targetNodeId,
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
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
                throw new TimeoutException($"cluster command timed out for node {targetNodeId}");
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
        if (!_pending.TryGetValue(result.CommandId, out var pending))
        {
            return false;
        }

        var resultSource = NormalizeNodeId(result.SourceNodeId, _options.NodeId);
        var resultTarget = NormalizeNodeId(result.TargetNodeId ?? result.NodeId, string.Empty);
        if (!string.Equals(resultTarget, pending.TargetNodeId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(result.SourceNodeId)
            && !string.Equals(resultSource, pending.SourceNodeId, StringComparison.Ordinal))
        {
            return false;
        }

        return pending.Completion.TrySetResult(result);
    }

    private static string NormalizeNodeId(string? nodeId, string fallback)
    {
        var value = (nodeId ?? string.Empty).Trim();
        return value.Length == 0 ? fallback : value;
    }

    private sealed record PendingCommand(string SourceNodeId, string TargetNodeId, TaskCompletionSource<ClusterCommandResult> Completion);
}
