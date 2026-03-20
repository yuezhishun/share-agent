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

        app.MapGet("/api/v2/instances", (InstanceManager manager) => Results.Ok(new { items = manager.List(), protocol = "v2" }));
        app.MapGet("/api/v2/nodes", (InstanceManager manager, NodeRegistry nodes) => Results.Ok(new { items = nodes.ListNodes(manager.List().Count), protocol = "v2" }));

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

        app.MapPost("/api/v2/nodes/{nodeId}/instances", async (HttpRequest request, string nodeId, CreateInstanceRequest body, InstanceManager manager, GatewayOptions options, ClusterCommandBroker broker, RemoteInstanceRegistry remoteInstances, CancellationToken ct) =>
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
                var commandResult = await broker.SendAsync(nodeId, "instance.create", body, ct);
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
                remoteInstances.Upsert(instanceId, nodeId);
                return Results.Ok(new { instance_id = instanceId, hub_url = hubUrl, node_id = nodeId, protocol = "v2" });
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

        return app;
    }

    private static bool IsLocalNode(string nodeId, GatewayOptions options)
    {
        return string.Equals((nodeId ?? string.Empty).Trim(), options.NodeId, StringComparison.Ordinal);
    }
}
