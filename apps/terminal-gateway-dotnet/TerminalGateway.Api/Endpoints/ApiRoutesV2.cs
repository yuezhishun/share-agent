using System.Text.Json;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class ApiRoutesV2
{
    public static IEndpointRouteBuilder MapApiRoutesV2(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v2/health", (InstanceManager manager) =>
            Results.Ok(new
            {
                ok = true,
                now = DateTimeOffset.UtcNow.ToString("O"),
                instances = manager.List().Count,
                metrics = manager.MetricsSnapshot(),
                protocol = "v2"
            }));

        app.MapGet("/api/v2/instances", async (InstanceManager manager, RemoteInstanceRegistry remoteInstances, GatewayOptions options, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            var localItems = manager.List();

            if (IsSlaveMode(options))
            {
                var masterView = await bridge.GetMasterInstancesAsync(ct);
                if (masterView.Ok)
                {
                    return Results.Ok(new
                    {
                        items = MergeSlaveVisibleInstances(localItems, masterView.Items),
                        protocol = "v2"
                    });
                }

                return Results.Ok(new
                {
                    items = MergeSlaveFallbackInstances(localItems, remoteInstances.List()),
                    protocol = "v2",
                    degraded = true,
                    cluster_error = masterView.Error
                });
            }

            return Results.Ok(new
            {
                items = MergeSlaveFallbackInstances(localItems, remoteInstances.List()),
                protocol = "v2"
            });
        });
        app.MapGet("/api/v2/nodes", async (InstanceManager manager, NodeRegistry nodes, GatewayOptions options, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            var localItems = nodes.ListNodes(manager.List().Count);
            if (IsSlaveMode(options))
            {
                var masterView = await bridge.GetMasterNodesAsync(ct);
                if (masterView.Ok)
                {
                    return Results.Ok(new
                    {
                        items = MergeSlaveVisibleNodes(localItems, masterView.Items, options.NodeId),
                        protocol = "v2"
                    });
                }

                return Results.Ok(new { items = localItems, protocol = "v2", degraded = true, cluster_error = masterView.Error });
            }

            return Results.Ok(new { items = localItems, protocol = "v2" });
        });

        app.MapPost("/api/v2/instances", async (HttpRequest request, CreateInstanceRequest body, InstanceManager manager, GatewayOptions options, CancellationToken ct) =>
        {
            try
            {
                var instance = await manager.CreateAsync(body, options.FilesBasePath, ct);
                var protocol = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                var hubUrl = $"{protocol}://{request.Host}/hubs/terminal-v2";
                return Results.Ok(new { instance_id = instance.Id, hub_url = hubUrl, protocol = "v2" });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message, @base = options.FilesBasePath }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/v2/nodes/{nodeId}/instances", async (HttpRequest request, string nodeId, CreateInstanceRequest body, InstanceManager manager, GatewayOptions options, ClusterCommandBroker broker, RemoteInstanceRegistry remoteInstances, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    var instance = await manager.CreateAsync(body, options.FilesBasePath, ct);
                    var protocol = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                    var hubUrl = $"{protocol}://{request.Host}/hubs/terminal-v2";
                    return Results.Ok(new { instance_id = instance.Id, hub_url = hubUrl, node_id = options.NodeId, protocol = "v2" });
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = ex.Message, @base = options.FilesBasePath }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }

            try
            {
                var commandResult = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "instance.create", body, ct)
                    : await broker.SendAsync(nodeId, "instance.create", body, ct);
                if (!commandResult.Ok)
                {
                    return Results.BadRequest(new { error = commandResult.Error ?? "remote create failed", node_id = nodeId });
                }

                var instanceId = commandResult.Payload.ValueKind == JsonValueKind.Object && commandResult.Payload.TryGetProperty("instance_id", out var idProp)
                    ? idProp.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(instanceId))
                {
                    return Results.BadRequest(new { error = "remote create response missing instance_id", node_id = nodeId });
                }

                var protocol = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                var hubUrl = $"{protocol}://{request.Host}/hubs/terminal-v2";
                var summary = ReadRemoteSummary(commandResult.Payload, instanceId, nodeId);
                remoteInstances.Upsert(summary);
                return Results.Ok(new { instance_id = instanceId, hub_url = hubUrl, node_id = summary.NodeId, protocol = "v2" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapDelete("/api/v2/instances/{id}", (string id, InstanceManager manager) =>
        {
            return manager.Terminate(id) ? Results.Ok(new { ok = true, protocol = "v2" }) : Results.NotFound(new { error = "instance not found" });
        });

        app.MapDelete("/api/v2/nodes/{nodeId}/instances/{instanceId}", async (string nodeId, string instanceId, InstanceManager manager, GatewayOptions options, ClusterCommandBroker broker, RemoteInstanceRegistry remoteInstances, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return manager.Terminate(instanceId)
                    ? Results.Ok(new { ok = true, node_id = nodeId, instance_id = instanceId, protocol = "v2" })
                    : Results.NotFound(new { error = "instance not found", node_id = nodeId, instance_id = instanceId });
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "instance.terminate", new { instance_id = instanceId }, ct)
                    : await broker.SendAsync(nodeId, "instance.terminate", new { instance_id = instanceId }, ct);
                if (result.Ok)
                {
                    remoteInstances.Remove(instanceId);
                    return Results.Ok(new { ok = true, node_id = nodeId, instance_id = instanceId, protocol = "v2" });
                }

                return Results.BadRequest(new { error = result.Error ?? "remote terminate failed", node_id = nodeId, instance_id = instanceId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId });
            }
        });

        return app;
    }

    private static bool IsLocalNode(string nodeId, GatewayOptions options)
    {
        return string.Equals((nodeId ?? string.Empty).Trim(), options.NodeId, StringComparison.Ordinal);
    }

    private static bool IsSlaveMode(GatewayOptions options)
    {
        return string.Equals(options.GatewayRole, "slave", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(options.MasterUrl);
    }

    private static InstanceSummary ReadRemoteSummary(JsonElement payload, string instanceId, string nodeId)
    {
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("summary", out var summaryElement))
        {
            return ReadInstanceSummary(summaryElement, instanceId, nodeId);
        }

        return new InstanceSummary
        {
            Id = instanceId,
            Command = "remote-shell",
            Cwd = string.Empty,
            Cols = 0,
            Rows = 0,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Status = "running",
            Clients = 0,
            NodeId = nodeId,
            NodeName = nodeId,
            NodeRole = "slave",
            NodeOnline = true
        };
    }

    private static InstanceSummary ReadInstanceSummary(JsonElement summary, string fallbackInstanceId, string fallbackNodeId)
    {
        return new InstanceSummary
        {
            Id = ReadString(summary, "id") ?? fallbackInstanceId,
            Command = ReadString(summary, "command") ?? "remote-shell",
            Cwd = ReadString(summary, "cwd") ?? string.Empty,
            Cols = ReadInt(summary, "cols"),
            Rows = ReadInt(summary, "rows"),
            CreatedAt = ReadString(summary, "created_at") ?? DateTimeOffset.UtcNow.ToString("O"),
            Status = ReadString(summary, "status") ?? "running",
            Clients = ReadInt(summary, "clients"),
            NodeId = ReadString(summary, "node_id") ?? fallbackNodeId,
            NodeName = ReadString(summary, "node_name") ?? fallbackNodeId,
            NodeRole = ReadString(summary, "node_role") ?? "slave",
            NodeOnline = ReadBool(summary, "node_online", true)
        };
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

    private static bool ReadBool(JsonElement element, string propertyName, bool fallback)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }

    private static IReadOnlyList<NodeSummary> MergeSlaveVisibleNodes(
        IReadOnlyList<NodeSummary> localItems,
        IReadOnlyList<NodeSummary> masterItems,
        string currentNodeId)
    {
        var localById = localItems
            .Where(item => !string.IsNullOrWhiteSpace(item.NodeId))
            .ToDictionary(item => item.NodeId, item => item, StringComparer.Ordinal);

        return masterItems
            .Concat(localItems)
            .Where(item => !string.IsNullOrWhiteSpace(item.NodeId))
            .GroupBy(item => item.NodeId, StringComparer.Ordinal)
            .Select(group =>
            {
                var preferred = localById.TryGetValue(group.Key, out var local) ? local : group.First();
                return new NodeSummary
                {
                    NodeId = preferred.NodeId,
                    NodeName = preferred.NodeName,
                    NodeRole = preferred.NodeRole,
                    NodeLabel = preferred.NodeLabel,
                    IsCurrent = string.Equals(preferred.NodeId, currentNodeId, StringComparison.Ordinal),
                    NodeOnline = preferred.NodeOnline,
                    InstanceCount = preferred.InstanceCount,
                    LastSeenAt = preferred.LastSeenAt
                };
            })
            .OrderBy(item => item.NodeId, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<InstanceSummary> MergeSlaveVisibleInstances(
        IReadOnlyList<InstanceSummary> localItems,
        IReadOnlyList<InstanceSummary> masterItems)
    {
        return masterItems
            .Concat(localItems)
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => item.CreatedAt, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<InstanceSummary> MergeSlaveFallbackInstances(
        IReadOnlyList<InstanceSummary> localItems,
        IReadOnlyList<InstanceSummary> cachedRemoteItems)
    {
        return localItems
            .Concat(cachedRemoteItems)
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => item.CreatedAt, StringComparer.Ordinal)
            .ToList();
    }
}
