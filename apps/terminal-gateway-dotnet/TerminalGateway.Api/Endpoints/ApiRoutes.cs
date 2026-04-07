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
                instances = manager.List().Count,
                metrics = manager.MetricsSnapshot()
            }));

        app.MapGet("/api/instances", async (InstanceManager manager, RemoteInstanceRegistry remoteInstances, GatewayOptions options, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            var localItems = manager.List();

            if (IsSlaveMode(options))
            {
                var masterView = await bridge.GetMasterInstancesAsync(ct);
                if (masterView.Ok)
                {
                    remoteInstances.UpsertRange(masterView.Items.Where(item =>
                        !string.Equals((item.NodeId ?? string.Empty).Trim(), options.NodeId, StringComparison.Ordinal)));
                    return Results.Ok(new
                    {
                        items = MergeSlaveVisibleInstances(localItems, masterView.Items)
                    });
                }

                return Results.Ok(new
                {
                    items = MergeSlaveFallbackInstances(localItems, remoteInstances.List()),
                    degraded = true,
                    cluster_error = masterView.Error
                });
            }

            return Results.Ok(new
            {
                items = MergeSlaveFallbackInstances(localItems, remoteInstances.List())
            });
        });

        app.MapGet("/api/nodes", async (InstanceManager manager, NodeRegistry nodes, GatewayOptions options, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            var localItems = nodes.ListNodes(manager.List().Count);
            if (IsSlaveMode(options))
            {
                var masterView = await bridge.GetMasterNodesAsync(ct);
                if (masterView.Ok)
                {
                    return Results.Ok(new
                    {
                        items = MergeSlaveVisibleNodes(localItems, masterView.Items, options.NodeId)
                    });
                }

                return Results.Ok(new { items = localItems, degraded = true, cluster_error = masterView.Error });
            }

            return Results.Ok(new { items = localItems });
        });

        app.MapPost("/api/instances", async (HttpRequest request, CreateInstanceRequest body, InstanceManager manager, GatewayOptions options, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            try
            {
                var instance = await manager.CreateAsync(body, options.FilesBasePath, ct);
                if (IsSlaveMode(options))
                {
                    await bridge.TrySyncLocalInstancesAsync(ct);
                }
                var protocol = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                var hubUrl = $"{protocol}://{request.Host}/hubs/terminal";
                return Results.Ok(new { instance_id = instance.Id, hub_url = hubUrl, node_id = options.NodeId, summary = instance });
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

        app.MapPost("/api/nodes/{nodeId}/instances", async (HttpRequest request, string nodeId, CreateInstanceRequest body, InstanceManager manager, GatewayOptions options, ClusterCommandBroker broker, RemoteInstanceRegistry remoteInstances, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    var instance = await manager.CreateAsync(body, options.FilesBasePath, ct);
                    var protocol = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                    var hubUrl = $"{protocol}://{request.Host}/hubs/terminal";
                    return Results.Ok(new { instance_id = instance.Id, hub_url = hubUrl, node_id = options.NodeId, summary = instance });
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
                var hubUrl = $"{protocol}://{request.Host}/hubs/terminal";
                var summary = ReadRemoteSummary(commandResult.Payload, instanceId, nodeId);
                remoteInstances.Upsert(summary);
                return Results.Ok(new { instance_id = instanceId, hub_url = hubUrl, node_id = summary.NodeId, summary });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapDelete("/api/instances/{id}", async (string id, InstanceManager manager, GatewayOptions options, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            var terminated = manager.Terminate(id);
            if (terminated && IsSlaveMode(options))
            {
                await bridge.TrySyncLocalInstancesAsync(ct);
            }
            return terminated ? Results.Ok(new { ok = true }) : Results.NotFound(new { error = "instance not found" });
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

        app.MapDelete("/api/nodes/{nodeId}/instances/{instanceId}", async (string nodeId, string instanceId, InstanceManager manager, GatewayOptions options, ClusterCommandBroker broker, RemoteInstanceRegistry remoteInstances, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return manager.Terminate(instanceId)
                    ? Results.Ok(new { ok = true, node_id = nodeId, instance_id = instanceId })
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
                    return Results.Ok(new { ok = true, node_id = nodeId, instance_id = instanceId });
                }

                return Results.BadRequest(new { error = result.Error ?? "remote terminate failed", node_id = nodeId, instance_id = instanceId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/processes/run", async (string nodeId, RunProcessRequest body, ProcessApiService processes, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(await processes.RunAsync(body, ct));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = ex.Message, node_id = nodeId }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
                }
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "process.run", body, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote process run failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/processes", async (string nodeId, RunProcessRequest body, ProcessApiService processes, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(await processes.StartManagedAsync(body, ct));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = ex.Message, node_id = nodeId }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
                }
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "process.start", body, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote process start failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapGet("/api/nodes/{nodeId}/processes", async (string nodeId, ProcessApiService processes, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return Results.Ok(new { items = processes.ListManaged() });
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "process.list", new { }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote process list failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapGet("/api/nodes/{nodeId}/processes/{processId}", async (string nodeId, string processId, ProcessApiService processes, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(processes.GetManaged(processId));
                }
                catch (Exception ex)
                {
                    return Results.NotFound(new { error = ex.Message, node_id = nodeId, process_id = processId });
                }
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "process.get", new { process_id = processId }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.NotFound(new { error = result.Error ?? "remote process not found", node_id = nodeId, process_id = processId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
            }
        });

        app.MapGet("/api/nodes/{nodeId}/processes/{processId}/output", async (string nodeId, string processId, ProcessApiService processes, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(new { items = processes.GetOutput(processId) });
                }
                catch (Exception ex)
                {
                    return Results.NotFound(new { error = ex.Message, node_id = nodeId, process_id = processId });
                }
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "process.output", new { process_id = processId }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.NotFound(new { error = result.Error ?? "remote process output not found", node_id = nodeId, process_id = processId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/processes/{processId}/wait", async (string nodeId, string processId, int? timeout_ms, ProcessApiService processes, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(await processes.WaitManagedAsync(processId, timeout_ms));
                }
                catch (Exception ex)
                {
                    return Results.NotFound(new { error = ex.Message, node_id = nodeId, process_id = processId });
                }
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "process.wait", new { process_id = processId, timeout_ms }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.NotFound(new { error = result.Error ?? "remote process wait failed", node_id = nodeId, process_id = processId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/processes/{processId}/stop", async (string nodeId, string processId, StopManagedProcessRequest body, ProcessApiService processes, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(await processes.StopManagedAsync(processId, body.Force == true));
                }
                catch (Exception ex)
                {
                    return Results.NotFound(new { error = ex.Message, node_id = nodeId, process_id = processId });
                }
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "process.stop", new { process_id = processId, force = body.Force == true }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.NotFound(new { error = result.Error ?? "remote process stop failed", node_id = nodeId, process_id = processId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
            }
        });

        app.MapDelete("/api/nodes/{nodeId}/processes/{processId}", async (string nodeId, string processId, ProcessApiService processes, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(processes.RemoveManaged(processId));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
                }
                catch (Exception ex)
                {
                    return Results.NotFound(new { error = ex.Message, node_id = nodeId, process_id = processId });
                }
            }

            try
            {
                var result = await broker.SendAsync(nodeId, "process.remove", new { process_id = processId }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.NotFound(new { error = result.Error ?? "remote process remove failed", node_id = nodeId, process_id = processId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/files/upload", async (HttpRequest request, string nodeId, InstanceManager manager, FileApiService files, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
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
            var targetPath = form["path"].ToString();
            if (instanceId.Length == 0 && targetPath.Length == 0)
            {
                return Results.BadRequest(new { error = "instance_id or path is required", node_id = nodeId });
            }

            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    await using var stream = file.OpenReadStream();
                    object uploaded;
                    if (instanceId.Length > 0)
                    {
                        var state = manager.Get(instanceId);
                        if (state is null)
                        {
                            return Results.NotFound(new { error = "instance not found", node_id = nodeId, instance_id = instanceId });
                        }
                        uploaded = await files.SaveUploadAsync(options.FilesBasePath, state.Cwd, file.FileName, stream, file.Length, ct);
                    }
                    else
                    {
                        uploaded = await files.UploadToPathAsync(options.FilesBasePath, targetPath, file.FileName, stream, file.Length, ct);
                    }
                    return Results.Ok(new { node_id = nodeId, instance_id = instanceId, path = targetPath, upload = uploaded });
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId, path = targetPath });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId, path = targetPath });
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

                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "files.upload", new
                    {
                        instance_id = instanceId,
                        path = targetPath,
                        file_name = file.FileName,
                        content_base64 = Convert.ToBase64String(buffer.ToArray())
                    }, ct)
                    : await broker.SendAsync(nodeId, "files.upload", new
                {
                    instance_id = instanceId,
                    path = targetPath,
                    file_name = file.FileName,
                    content_base64 = Convert.ToBase64String(buffer.ToArray())
                }, ct);

                if (!result.Ok)
                {
                    return Results.BadRequest(new { error = result.Error ?? "remote upload failed", node_id = nodeId, instance_id = instanceId, path = targetPath });
                }

                return Results.Ok(new { node_id = nodeId, instance_id = instanceId, path = targetPath, upload = result.Payload });
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId, path = targetPath });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, instance_id = instanceId, path = targetPath });
            }
        });

        app.MapGet("/api/nodes/{nodeId}/files/list", async (string nodeId, string? path, string? show_hidden, FileApiService files, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            var showHidden = ParseBooleanFlag(show_hidden);
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(files.List(options.FilesBasePath, path, showHidden));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = ex.Message, @base = options.FilesBasePath, node_id = nodeId }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (DirectoryNotFoundException ex)
                {
                    return Results.NotFound(new { error = ex.Message, path, node_id = nodeId });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "files.list", new
                    {
                        path,
                        show_hidden = showHidden
                    }, ct)
                    : await broker.SendAsync(nodeId, "files.list", new
                {
                    path,
                    show_hidden = showHidden
                }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote file list failed", node_id = nodeId, path });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, path });
            }
        });

        app.MapGet("/api/nodes/{nodeId}/files/read", async (string nodeId, string? path, int? max_lines, int? chunk_bytes, int? line_offset, string? direction, string? mode, FileApiService files, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            var maxLines = Math.Clamp(max_lines ?? options.FileChunkMaxLines, 1, 5000);
            var chunkBytes = Math.Clamp(chunk_bytes ?? options.FileChunkBytes, 1, 1024 * 1024);
            var lineOffset = Math.Max(0, line_offset ?? 0);
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(await files.ReadAsync(
                        options.FilesBasePath,
                        path,
                        maxLines,
                        mode,
                        ct,
                        chunkBytes,
                        lineOffset,
                        direction,
                        options.LargeFileThresholdBytes));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = ex.Message, @base = options.FilesBasePath, node_id = nodeId }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new { error = ex.Message, path = ex.FileName, node_id = nodeId });
                }
                catch (InvalidDataException ex)
                {
                    return Results.Json(new { error = ex.Message, path, node_id = nodeId }, statusCode: StatusCodes.Status415UnsupportedMediaType);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId, path });
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "files.read", new
                    {
                        path,
                        max_lines = maxLines,
                        chunk_bytes = chunkBytes,
                        line_offset = lineOffset,
                        direction,
                        mode
                    }, ct)
                    : await broker.SendAsync(nodeId, "files.read", new
                {
                    path,
                    max_lines = maxLines,
                    chunk_bytes = chunkBytes,
                    line_offset = lineOffset,
                    direction,
                    mode
                }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote file read failed", node_id = nodeId, path });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, path });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/files/write", async (string nodeId, FileWriteRequest body, FileApiService files, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(await files.WriteAsync(options.FilesBasePath, body.Path, body.Content, ct));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = ex.Message, @base = options.FilesBasePath, node_id = nodeId }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (DirectoryNotFoundException ex)
                {
                    return Results.NotFound(new { error = ex.Message, path = body.Path, node_id = nodeId });
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new { error = ex.Message, path = body.Path, node_id = nodeId });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, path = body.Path, node_id = nodeId });
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "files.write", body, ct)
                    : await broker.SendAsync(nodeId, "files.write", body, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote file write failed", node_id = nodeId, path = body.Path });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, path = body.Path });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/files/mkdir", async (string nodeId, FileMkdirRequest body, FileApiService files, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            var targetPath = string.IsNullOrWhiteSpace(body.Path) ? options.FilesBasePath : body.Path;
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    var item = files.CreateDirectory(options.FilesBasePath, targetPath, body.Name);
                    return Results.Ok(new { item });
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = ex.Message, @base = options.FilesBasePath, node_id = nodeId }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (DirectoryNotFoundException ex)
                {
                    return Results.NotFound(new { error = ex.Message, path = targetPath, node_id = nodeId });
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new { error = ex.Message, path = targetPath, node_id = nodeId });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, path = targetPath, node_id = nodeId });
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "files.mkdir", new
                    {
                        path = targetPath,
                        name = body.Name
                    }, ct)
                    : await broker.SendAsync(nodeId, "files.mkdir", new
                {
                    path = targetPath,
                    name = body.Name
                }, ct);
                return result.Ok
                    ? Results.Ok(new { item = result.Payload })
                    : Results.BadRequest(new { error = result.Error ?? "remote mkdir failed", node_id = nodeId, path = targetPath });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, path = targetPath });
            }
        });

        app.MapGet("/api/nodes/{nodeId}/files/download", async (string nodeId, string? path, FileApiService files, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                FileApiService.DownloadStreamResult? download = null;
                try
                {
                    download = files.OpenDownloadStream(options.FilesBasePath, path);
                    return Results.Stream(download.Stream, download.ContentType, download.Name, enableRangeProcessing: download.EnableRangeProcessing);
                }
                catch (UnauthorizedAccessException ex)
                {
                    download?.Stream.Dispose();
                    return Results.Json(new { error = ex.Message, @base = options.FilesBasePath, node_id = nodeId }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (FileNotFoundException ex)
                {
                    download?.Stream.Dispose();
                    return Results.NotFound(new { error = ex.Message, path = ex.FileName ?? path, node_id = nodeId });
                }
                catch (Exception ex)
                {
                    download?.Stream.Dispose();
                    return Results.BadRequest(new { error = ex.Message, path, node_id = nodeId });
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "files.download", new { path }, ct)
                    : await broker.SendAsync(nodeId, "files.download", new { path }, ct);
                if (!result.Ok)
                {
                    return Results.BadRequest(new { error = result.Error ?? "remote download failed", node_id = nodeId, path });
                }

                if (result.Payload.ValueKind != JsonValueKind.Object)
                {
                    return Results.BadRequest(new { error = "remote download response invalid", node_id = nodeId, path });
                }

                var fileName = result.Payload.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                var contentType = result.Payload.TryGetProperty("content_type", out var typeProp) ? typeProp.GetString() : null;
                var contentBase64 = result.Payload.TryGetProperty("content_base64", out var bodyProp) ? bodyProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentBase64))
                {
                    return Results.BadRequest(new { error = "remote download response missing file payload", node_id = nodeId, path });
                }

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(contentBase64);
                }
                catch
                {
                    return Results.BadRequest(new { error = "remote download response invalid", node_id = nodeId, path });
                }

                return Results.File(bytes, contentType ?? "application/octet-stream", fileName);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, path });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/files/rename", async (string nodeId, FileRenameRequest body, FileApiService files, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    var item = files.RenameEntry(options.FilesBasePath, body.Path, body.NewName);
                    return Results.Ok(new { item });
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = ex.Message, @base = options.FilesBasePath, node_id = nodeId }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new { error = ex.Message, path = ex.FileName ?? body.Path, node_id = nodeId });
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new { error = ex.Message, path = body.Path, node_id = nodeId });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, path = body.Path, node_id = nodeId });
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "files.rename", new
                    {
                        path = body.Path,
                        new_name = body.NewName
                    }, ct)
                    : await broker.SendAsync(nodeId, "files.rename", new
                {
                    path = body.Path,
                    new_name = body.NewName
                }, ct);
                return result.Ok
                    ? Results.Ok(new { item = result.Payload })
                    : Results.BadRequest(new { error = result.Error ?? "remote rename failed", node_id = nodeId, path = body.Path });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, path = body.Path });
            }
        });

        app.MapDelete("/api/nodes/{nodeId}/files/remove", async (string nodeId, string? path, string? recursive, FileApiService files, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            var allowRecursive = ParseBooleanFlag(recursive);
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    var result = files.RemoveEntry(options.FilesBasePath, path, allowRecursive);
                    return Results.Ok(result);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = ex.Message, @base = options.FilesBasePath, node_id = nodeId }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new { error = ex.Message, path = ex.FileName ?? path, node_id = nodeId });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message, path, node_id = nodeId });
                }
                catch (IOException ex)
                {
                    return Results.BadRequest(new { error = ex.Message, path, node_id = nodeId });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, path, node_id = nodeId });
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "files.remove", new
                    {
                        path,
                        recursive = allowRecursive
                    }, ct)
                    : await broker.SendAsync(nodeId, "files.remove", new
                {
                    path,
                    recursive = allowRecursive
                }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote remove failed", node_id = nodeId, path });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, path });
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

        app.MapGet("/api/files/read", async (string? path, int? max_lines, int? chunk_bytes, int? line_offset, string? direction, string? mode, FileApiService files, GatewayOptions options, CancellationToken ct) =>
        {
            var maxLines = Math.Clamp(max_lines ?? options.FileChunkMaxLines, 1, 5000);
            var chunkBytes = Math.Clamp(chunk_bytes ?? options.FileChunkBytes, 1, 1024 * 1024);
            var lineOffset = Math.Max(0, line_offset ?? 0);
            try
            {
                var result = await files.ReadAsync(
                    options.FilesBasePath,
                    path,
                    maxLines,
                    mode,
                    ct,
                    chunkBytes,
                    lineOffset,
                    direction,
                    options.LargeFileThresholdBytes);
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

        app.MapPost("/api/files/write", async (FileWriteRequest body, FileApiService files, GatewayOptions options, CancellationToken ct) =>
        {
            try
            {
                var result = await files.WriteAsync(options.FilesBasePath, body.Path, body.Content, ct);
                return Results.Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message, @base = options.FilesBasePath }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message, path = body.Path });
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new { error = ex.Message, path = body.Path });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, path = body.Path });
            }
        });

        app.MapPost("/api/files/upload", async (HttpRequest request, string? path, FileApiService files, GatewayOptions options, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart form-data is required" });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null)
            {
                return Results.BadRequest(new { error = "file is required" });
            }

            var targetPath = form["path"].ToString();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = path;
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var uploaded = await files.UploadToPathAsync(options.FilesBasePath, targetPath, file.FileName, stream, file.Length, ct);
                return Results.Ok(new { upload = uploaded });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message, @base = options.FilesBasePath }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message, path = targetPath });
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new { error = ex.Message, path = targetPath });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, path = targetPath });
            }
        });

        app.MapPost("/api/files/mkdir", (FileMkdirRequest body, FileApiService files, GatewayOptions options) =>
        {
            var targetPath = string.IsNullOrWhiteSpace(body.Path) ? options.FilesBasePath : body.Path;
            try
            {
                var item = files.CreateDirectory(options.FilesBasePath, targetPath, body.Name);
                return Results.Ok(new { item });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message, @base = options.FilesBasePath }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message, path = targetPath });
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new { error = ex.Message, path = targetPath });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, path = targetPath });
            }
        });

        app.MapPost("/api/files/rename", (FileRenameRequest body, FileApiService files, GatewayOptions options) =>
        {
            try
            {
                var item = files.RenameEntry(options.FilesBasePath, body.Path, body.NewName);
                return Results.Ok(new { item });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message, @base = options.FilesBasePath }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message, path = ex.FileName ?? body.Path });
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new { error = ex.Message, path = body.Path });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, path = body.Path });
            }
        });

        app.MapDelete("/api/files/remove", (string? path, string? recursive, FileApiService files, GatewayOptions options) =>
        {
            var allowRecursive = recursive == "1" || string.Equals(recursive, "true", StringComparison.OrdinalIgnoreCase);
            try
            {
                var result = files.RemoveEntry(options.FilesBasePath, path, allowRecursive);
                return Results.Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message, @base = options.FilesBasePath }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message, path = ex.FileName ?? path });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message, path });
            }
            catch (IOException ex)
            {
                return Results.BadRequest(new { error = ex.Message, path });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, path });
            }
        });

        app.MapGet("/api/files/download", (string? path, FileApiService files, GatewayOptions options) =>
        {
            FileApiService.DownloadStreamResult? download = null;
            try
            {
                download = files.OpenDownloadStream(options.FilesBasePath, path);
                return Results.Stream(download.Stream, download.ContentType, download.Name, enableRangeProcessing: download.EnableRangeProcessing);
            }
            catch (UnauthorizedAccessException ex)
            {
                download?.Stream.Dispose();
                return Results.Json(new { error = ex.Message, @base = options.FilesBasePath }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (FileNotFoundException ex)
            {
                download?.Stream.Dispose();
                return Results.NotFound(new { error = ex.Message, path = ex.FileName ?? path });
            }
            catch (Exception ex)
            {
                download?.Stream.Dispose();
                return Results.BadRequest(new { error = ex.Message, path });
            }
        });

        return app;
    }

    private sealed record FileMkdirRequest(string? Path, string? Name);
    private sealed record FileRenameRequest(string? Path, string? NewName);
    private sealed record FileWriteRequest(string? Path, string? Content);

    private static bool IsLocalNode(string nodeId, GatewayOptions options)
    {
        var normalized = (nodeId ?? string.Empty).Trim();
        return normalized.Length == 0 || string.Equals(normalized, options.NodeId, StringComparison.Ordinal);
    }

    private static bool IsSlaveMode(GatewayOptions options)
    {
        return string.Equals(options.GatewayRole, "slave", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(options.MasterUrl);
    }

    private static bool ParseBooleanFlag(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized == "1" || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase);
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
                    NodeOs = preferred.NodeOs,
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
