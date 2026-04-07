using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class CliEndpoints
{
    public static IEndpointRouteBuilder MapCliEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/nodes/{nodeId}/terminal-envs", async (string nodeId, string? group, string? search, TerminalEnvService terminalEnvs, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return Results.Ok(new { items = terminalEnvs.List(group, search).Select(ToApiTerminalEnv) });
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "terminal.env.list", new { group, search }, ct)
                    : await broker.SendAsync(nodeId, "terminal.env.list", new { group, search }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote terminal env list failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/terminal-envs", async (string nodeId, CreateTerminalEnvEntryRequest body, TerminalEnvService terminalEnvs, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(ToApiTerminalEnv(terminalEnvs.Create(body)));
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "terminal.env.create", body, ct)
                    : await broker.SendAsync(nodeId, "terminal.env.create", body, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote terminal env create failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapPut("/api/nodes/{nodeId}/terminal-envs/{envId}", async (string nodeId, string envId, UpdateTerminalEnvEntryRequest body, TerminalEnvService terminalEnvs, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(ToApiTerminalEnv(terminalEnvs.Update(envId, body)));
                }
                catch (Exception ex)
                {
                    var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                    return Results.Json(new { error = ex.Message, node_id = nodeId, env_id = envId }, statusCode: code);
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "terminal.env.update", new { env_id = envId, updates = body }, ct)
                    : await broker.SendAsync(nodeId, "terminal.env.update", new { env_id = envId, updates = body }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote terminal env update failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapDelete("/api/nodes/{nodeId}/terminal-envs/{envId}", async (string nodeId, string envId, TerminalEnvService terminalEnvs, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(terminalEnvs.Delete(envId));
                }
                catch (Exception ex)
                {
                    var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                    return Results.Json(new { error = ex.Message, node_id = nodeId, env_id = envId }, statusCode: code);
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "terminal.env.delete", new { env_id = envId }, ct)
                    : await broker.SendAsync(nodeId, "terminal.env.delete", new { env_id = envId }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote terminal env delete failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapGet("/api/nodes/{nodeId}/cli/templates", async (string nodeId, string? kind, CliTemplateService templates, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return Results.Ok(new { items = templates.List(kind) });
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.template.list", new { kind }, ct)
                    : await broker.SendAsync(nodeId, "cli.template.list", new { kind }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote cli template list failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/cli/templates", async (string nodeId, CreateCliTemplateRequest body, CliTemplateService templates, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(templates.Create(body));
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.template.create", body, ct)
                    : await broker.SendAsync(nodeId, "cli.template.create", body, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote cli template create failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapPut("/api/nodes/{nodeId}/cli/templates/{templateId}", async (string nodeId, string templateId, UpdateCliTemplateRequest body, CliTemplateService templates, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(templates.Update(templateId, body));
                }
                catch (Exception ex)
                {
                    var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                    return Results.Json(new { error = ex.Message, node_id = nodeId }, statusCode: code);
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.template.update", new { template_id = templateId, updates = body }, ct)
                    : await broker.SendAsync(nodeId, "cli.template.update", new { template_id = templateId, updates = body }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote cli template update failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapDelete("/api/nodes/{nodeId}/cli/templates/{templateId}", async (string nodeId, string templateId, CliTemplateService templates, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(templates.Delete(templateId));
                }
                catch (Exception ex)
                {
                    var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                    return Results.Json(new { error = ex.Message, node_id = nodeId }, statusCode: code);
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.template.delete", new { template_id = templateId }, ct)
                    : await broker.SendAsync(nodeId, "cli.template.delete", new { template_id = templateId }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote cli template delete failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/cli/processes", async (string nodeId, StartCliProcessRequest body, CliProcessService processes, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
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
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.process.start", body, ct)
                    : await broker.SendAsync(nodeId, "cli.process.start", body, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote cli process start failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapGet("/api/nodes/{nodeId}/cli/processes", async (string nodeId, CliProcessService processes, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return Results.Ok(new { items = processes.ListManaged() });
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.process.list", new { }, ct)
                    : await broker.SendAsync(nodeId, "cli.process.list", new { }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.BadRequest(new { error = result.Error ?? "remote cli process list failed", node_id = nodeId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId });
            }
        });

        app.MapGet("/api/nodes/{nodeId}/cli/processes/{processId}", async (string nodeId, string processId, CliProcessService processes, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
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
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.process.get", new { process_id = processId }, ct)
                    : await broker.SendAsync(nodeId, "cli.process.get", new { process_id = processId }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.NotFound(new { error = result.Error ?? "remote cli process not found", node_id = nodeId, process_id = processId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
            }
        });

        app.MapGet("/api/nodes/{nodeId}/cli/processes/{processId}/output", async (string nodeId, string processId, CliProcessService processes, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
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
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.process.output", new { process_id = processId }, ct)
                    : await broker.SendAsync(nodeId, "cli.process.output", new { process_id = processId }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.NotFound(new { error = result.Error ?? "remote cli process output not found", node_id = nodeId, process_id = processId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/cli/processes/{processId}/wait", async (string nodeId, string processId, int? timeout_ms, CliProcessService processes, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
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
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.process.wait", new { process_id = processId, timeout_ms }, ct)
                    : await broker.SendAsync(nodeId, "cli.process.wait", new { process_id = processId, timeout_ms }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.NotFound(new { error = result.Error ?? "remote cli process wait failed", node_id = nodeId, process_id = processId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/cli/processes/{processId}/stop", async (string nodeId, string processId, StopCliProcessRequest body, CliProcessService processes, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
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
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.process.stop", new { process_id = processId, force = body.Force == true }, ct)
                    : await broker.SendAsync(nodeId, "cli.process.stop", new { process_id = processId, force = body.Force == true }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.NotFound(new { error = result.Error ?? "remote cli process stop failed", node_id = nodeId, process_id = processId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
            }
        });

        app.MapDelete("/api/nodes/{nodeId}/cli/processes/{processId}", async (string nodeId, string processId, CliProcessService processes, GatewayOptions options, ClusterCommandBroker broker, SlaveClusterBridgeService bridge, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                try
                {
                    return Results.Ok(processes.RemoveManaged(processId));
                }
                catch (Exception ex)
                {
                    var code = ex.Message.Contains("不存在", StringComparison.Ordinal) || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                        ? StatusCodes.Status404NotFound
                        : StatusCodes.Status400BadRequest;
                    return Results.Json(new { error = ex.Message, node_id = nodeId, process_id = processId }, statusCode: code);
                }
            }

            try
            {
                var result = IsSlaveMode(options)
                    ? await bridge.RequestCommandAsync(nodeId, "cli.process.remove", new { process_id = processId }, ct)
                    : await broker.SendAsync(nodeId, "cli.process.remove", new { process_id = processId }, ct);
                return result.Ok
                    ? Results.Ok(result.Payload)
                    : Results.NotFound(new { error = result.Error ?? "remote cli process remove failed", node_id = nodeId, process_id = processId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, node_id = nodeId, process_id = processId });
            }
        });

        return app;
    }

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

    private static object ToApiTerminalEnv(TerminalEnvEntryRecord item)
    {
        return new
        {
            id = item.EnvId,
            key = item.Key,
            valueType = item.ValueType,
            value = item.Value,
            group = item.GroupName,
            enabled = item.Enabled,
            sortOrder = item.SortOrder,
            createdAt = item.CreatedAt,
            updatedAt = item.UpdatedAt
        };
    }
}
