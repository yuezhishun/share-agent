using System.Text.Json;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class ApiRoutes
{
    public static IEndpointRouteBuilder MapApiRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", (InstanceManager manager) =>
            Results.Ok(new
            {
                ok = true,
                now = DateTimeOffset.UtcNow.ToString("O"),
                instances = manager.List().Count
            }));

        app.MapGet("/api/instances", (InstanceManager manager) => Results.Ok(new { items = manager.List() }));
        app.MapGet("/api/nodes", (InstanceManager manager, NodeRegistry nodes) => Results.Ok(new { items = nodes.ListNodes(manager.List().Count) }));

        app.MapPost("/api/instances", async (HttpRequest request, CreateInstanceRequest body, InstanceManager manager, GatewayOptions options, CancellationToken ct) =>
        {
            try
            {
                var instance = await manager.CreateAsync(body, options.FilesBasePath, ct);
                var protocol = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                var hubUrl = $"{protocol}://{request.Host}/hubs/terminal";
                return Results.Ok(new { instance_id = instance.Id, hub_url = hubUrl });
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

        app.MapPost("/api/nodes/{nodeId}/instances", async (HttpRequest request, string nodeId, CreateInstanceRequest body, InstanceManager manager, GatewayOptions options, ClusterCommandBroker broker, RemoteInstanceRegistry remoteInstances, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    var instance = await manager.CreateAsync(body, options.FilesBasePath, ct);
                    var protocol = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                    var hubUrl = $"{protocol}://{request.Host}/hubs/terminal";
                    return Results.Ok(new { instance_id = instance.Id, hub_url = hubUrl, node_id = options.NodeId });
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
                var hubUrl = $"{protocol}://{request.Host}/hubs/terminal";
                remoteInstances.Upsert(instanceId, nodeId);
                return Results.Ok(new { instance_id = instanceId, hub_url = hubUrl, node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapDelete("/api/instances/{id}", (string id, InstanceManager manager) =>
        {
            return manager.Terminate(id) ? Results.Ok(new { ok = true }) : Results.NotFound(new { error = "instance not found" });
        });

        app.MapPost("/api/nodes/{nodeId}/instances/{instanceId}/input", async (string nodeId, string instanceId, NodeInstanceInputRequest body, InstanceManager manager, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return manager.WriteStdin(instanceId, body.Data ?? string.Empty)
                    ? Results.Ok(new { ok = true, node_id = nodeId, instance_id = instanceId })
                    : Results.NotFound(new { error = "instance not found", node_id = nodeId, instance_id = instanceId });
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "instance.input", new { instance_id = instanceId, data = body.Data ?? string.Empty }, ct);
                return result.Ok
                    ? Results.Ok(new { ok = true, node_id = nodeId, instance_id = instanceId })
                    : Results.BadRequest(new { error = result.Error ?? "remote input failed", node_id = nodeId, instance_id = instanceId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/instances/{instanceId}/resize", async (string nodeId, string instanceId, NodeInstanceResizeRequest body, InstanceManager manager, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            var cols = body.Cols ?? 0;
            var rows = body.Rows ?? 0;
            if (IsLocalNode(nodeId, options))
            {
                var snapshot = manager.Resize(instanceId, cols, rows);
                return snapshot is null
                    ? Results.NotFound(new { error = "instance not found", node_id = nodeId, instance_id = instanceId })
                    : Results.Ok(new { ok = true, node_id = nodeId, instance_id = instanceId, cols, rows });
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "instance.resize", new { instance_id = instanceId, cols, rows }, ct);
                return result.Ok
                    ? Results.Ok(new { ok = true, node_id = nodeId, instance_id = instanceId, cols, rows })
                    : Results.BadRequest(new { error = result.Error ?? "remote resize failed", node_id = nodeId, instance_id = instanceId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId });
            }
        });

        app.MapDelete("/api/nodes/{nodeId}/instances/{instanceId}", async (string nodeId, string instanceId, InstanceManager manager, GatewayOptions options, ClusterCommandBroker broker, RemoteInstanceRegistry remoteInstances, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return manager.Terminate(instanceId)
                    ? Results.Ok(new { ok = true, node_id = nodeId, instance_id = instanceId })
                    : Results.NotFound(new { error = "instance not found", node_id = nodeId, instance_id = instanceId });
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "instance.terminate", new { instance_id = instanceId }, ct);
                if (result.Ok)
                {
                    remoteInstances.Remove(instanceId);
                    return Results.Ok(new { ok = true, node_id = nodeId, instance_id = instanceId });
                }

                return Results.BadRequest(new { error = result.Error ?? "remote terminate failed", node_id = nodeId, instance_id = instanceId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/files/upload", async (HttpRequest request, string nodeId, InstanceManager manager, FileApiService files, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart form-data is required", node_id = nodeId });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null)
            {
                return Results.BadRequest(new { error = "file is required", node_id = nodeId });
            }

            var instanceId = form["instance_id"].ToString();
            if (instanceId.Length == 0)
            {
                return Results.BadRequest(new { error = "instance_id is required", node_id = nodeId });
            }

            if (IsLocalNode(nodeId, options))
            {
                var state = manager.Get(instanceId);
                if (state is null)
                {
                    return Results.NotFound(new { error = "instance not found", node_id = nodeId, instance_id = instanceId });
                }

                try
                {
                    await using var stream = file.OpenReadStream();
                    var uploaded = await files.SaveUploadAsync(options.FilesBasePath, state.Cwd, file.FileName, stream, file.Length, ct);
                    return Results.Ok(new { node_id = nodeId, instance_id = instanceId, upload = uploaded });
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId });
                }
            }

            try
            {
                await using var input = file.OpenReadStream();
                await using var buffer = new MemoryStream();
                await input.CopyToAsync(buffer, ct);
                if (buffer.Length > FileApiService.UploadMaxBytes)
                {
                    return Results.BadRequest(new { error = "file too large", node_id = nodeId, instance_id = instanceId });
                }

                var result = await broker.SendAsync(nodeId, "files.upload", new
                {
                    instance_id = instanceId,
                    file_name = file.FileName,
                    content_base64 = Convert.ToBase64String(buffer.ToArray())
                }, ct);

                if (!result.Ok)
                {
                    return Results.BadRequest(new { error = result.Error ?? "remote upload failed", node_id = nodeId, instance_id = instanceId });
                }

                return Results.Ok(new { node_id = nodeId, instance_id = instanceId, upload = result.Payload });
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId });
            }
        });

        app.MapGet("/api/projects", (ProjectApiService projects, GatewayOptions options) => Results.Ok(projects.ListProjects(options.FilesBasePath)));

        app.MapGet("/api/files/list", (string? path, string? show_hidden, FileApiService files, GatewayOptions options) =>
        {
            try
            {
                var showHidden = show_hidden == "1" || string.Equals(show_hidden, "true", StringComparison.OrdinalIgnoreCase);
                return Results.Ok(files.List(options.FilesBasePath, path, showHidden));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message, @base = options.FilesBasePath }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message, path });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/files/read", async (string? path, int? max_lines, FileApiService files, GatewayOptions options, CancellationToken ct) =>
        {
            var maxLines = Math.Clamp(max_lines ?? 500, 1, 2000);
            try
            {
                var result = await files.ReadAsync(options.FilesBasePath, path, maxLines, ct);
                return Results.Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message, @base = options.FilesBasePath }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message, path = ex.FileName });
            }
            catch (InvalidDataException ex)
            {
                return Results.Json(new { error = ex.Message, path }, statusCode: StatusCodes.Status415UnsupportedMediaType);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }

    private static bool IsLocalNode(string nodeId, GatewayOptions options)
    {
        var normalized = (nodeId ?? string.Empty).Trim();
        return normalized.Length == 0 || string.Equals(normalized, options.NodeId, StringComparison.Ordinal);
    }
}
