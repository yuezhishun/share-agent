using System.Text.Json;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class AgentGatewayEndpoints
{
    public static IEndpointRouteBuilder MapAgentGatewayEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/agents", (AgentCatalogService catalog) =>
            Results.Ok(new { items = catalog.List() }));

        app.MapGet("/api/agents/{backend}/health", (string backend, string? cliPath, AgentCatalogService catalog) =>
            Results.Ok(catalog.CheckHealth(backend, cliPath)));

        app.MapGet("/api/nodes/{nodeId}/agents", async (string nodeId, AgentCatalogService catalog, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return Results.Ok(new { items = catalog.List() });
            }

            var result = await broker.SendAsync(nodeId, "agent.list", new { }, ct);
            return result.Ok ? Results.Ok(result.Payload) : Results.BadRequest(new { error = result.Error, node_id = nodeId });
        });

        app.MapGet("/api/nodes/{nodeId}/agents/{backend}/health", async (string nodeId, string backend, string? cliPath, AgentCatalogService catalog, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return Results.Ok(catalog.CheckHealth(backend, cliPath));
            }

            var result = await broker.SendAsync(nodeId, "agent.health", new { backend, cli_path = cliPath }, ct);
            return result.Ok ? Results.Ok(result.Payload) : Results.BadRequest(new { error = result.Error, node_id = nodeId, backend });
        });

        app.MapGet("/api/agent-sessions", (AgentGatewayService gateway) =>
            Results.Ok(new { items = gateway.ListSessions() }));

        app.MapGet("/api/agent-sessions/{gatewaySessionId}", (string gatewaySessionId, AgentGatewayService gateway) =>
        {
            var session = gateway.GetSession(gatewaySessionId);
            return session is null
                ? Results.NotFound(new { error = "agent session not found", gateway_session_id = gatewaySessionId })
                : Results.Ok(session);
        });

        app.MapGet("/api/agent-sessions/{gatewaySessionId}/events", (string gatewaySessionId, AgentGatewayService gateway) =>
        {
            try
            {
                return Results.Ok(new { items = gateway.GetEvents(gatewaySessionId) });
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message, gateway_session_id = gatewaySessionId });
            }
        });

        app.MapPost("/api/agent-sessions", async (HttpRequest request, AgentSessionConnectRequest body, AgentGatewayService gateway, GatewayOptions options, CancellationToken ct) =>
        {
            try
            {
                var session = await gateway.ConnectAsync(body, ct);
                var protocol = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                var hubScheme = protocol == "https" ? "wss" : "ws";
                return Results.Ok(new
                {
                    session,
                    hub_url = $"{hubScheme}://{request.Host}/hubs/agent",
                    node_id = options.NodeId
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (NotSupportedException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/nodes/{nodeId}/agent-sessions", async (HttpRequest request, string nodeId, AgentSessionConnectRequest body, AgentGatewayService gateway, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                return await ConnectLocalAsync(request, body, gateway, options, ct);
            }

            var result = await broker.SendAsync(nodeId, "agent.session.connect", body, ct);
            if (!result.Ok)
            {
                return Results.BadRequest(new { error = result.Error, node_id = nodeId });
            }

            return Results.Ok(new
            {
                session = result.Payload,
                node_id = nodeId,
                hub_url = BuildHubUrl(request)
            });
        });

        app.MapPost("/api/agent-sessions/{gatewaySessionId}/prompt", async (string gatewaySessionId, AgentSessionPromptRequest body, AgentGatewayService gateway, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await gateway.PromptAsync(gatewaySessionId, body.Text ?? string.Empty, ct);
                return Results.Ok(new { ok = true });
            }));

        app.MapPost("/api/agent-sessions/{gatewaySessionId}/cancel", async (string gatewaySessionId, AgentGatewayService gateway, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await gateway.CancelAsync(gatewaySessionId, ct);
                return Results.Ok(new { ok = true });
            }));

        app.MapPost("/api/agent-sessions/{gatewaySessionId}/mode", async (string gatewaySessionId, AgentSessionModeRequest body, AgentGatewayService gateway, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await gateway.SetModeAsync(gatewaySessionId, body.Mode ?? "default", ct);
                return Results.Ok(new { ok = true });
            }));

        app.MapPost("/api/agent-sessions/{gatewaySessionId}/model", async (string gatewaySessionId, AgentSessionModelRequest body, AgentGatewayService gateway, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await gateway.SetModelAsync(gatewaySessionId, body.ModelId ?? string.Empty, ct);
                return Results.Ok(new { ok = true });
            }));

        app.MapPost("/api/agent-sessions/{gatewaySessionId}/config-option", async (string gatewaySessionId, AgentConfigOptionRequest body, AgentGatewayService gateway, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await gateway.SetConfigOptionAsync(gatewaySessionId, body.ConfigId ?? string.Empty, body.Value, ct);
                return Results.Ok(new { ok = true });
            }));

        app.MapPost("/api/agent-sessions/{gatewaySessionId}/permission", async (string gatewaySessionId, AgentPermissionResponseRequest body, AgentGatewayService gateway, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await gateway.RespondPermissionAsync(gatewaySessionId, body, ct);
                return Results.Ok(new { ok = true });
            }));

        app.MapDelete("/api/agent-sessions/{gatewaySessionId}", async (string gatewaySessionId, AgentGatewayService gateway) =>
        {
            await gateway.DisconnectAsync(gatewaySessionId);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/nodes/{nodeId}/agent-sessions/{gatewaySessionId}/prompt", async (string nodeId, string gatewaySessionId, AgentSessionPromptRequest body, AgentGatewayService gateway, GatewayOptions options, ClusterCommandBroker broker, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                if (IsLocalNode(nodeId, options))
                {
                    await gateway.PromptAsync(gatewaySessionId, body.Text ?? string.Empty, ct);
                }
                else
                {
                    var result = await broker.SendAsync(nodeId, "agent.session.prompt", new { gateway_session_id = gatewaySessionId, text = body.Text ?? string.Empty }, ct);
                    if (!result.Ok)
                    {
                        throw new InvalidOperationException(result.Error ?? "remote prompt failed");
                    }
                }

                return Results.Ok(new { ok = true, node_id = nodeId });
            }));

        app.MapDelete("/api/nodes/{nodeId}/agent-sessions/{gatewaySessionId}", async (string nodeId, string gatewaySessionId, AgentGatewayService gateway, GatewayOptions options, ClusterCommandBroker broker) =>
        {
            if (IsLocalNode(nodeId, options))
            {
                await gateway.DisconnectAsync(gatewaySessionId);
            }
            else
            {
                var result = await broker.SendAsync(nodeId, "agent.session.disconnect", new { gateway_session_id = gatewaySessionId }, CancellationToken.None);
                if (!result.Ok)
                {
                    return Results.BadRequest(new { error = result.Error, node_id = nodeId });
                }
            }

            return Results.Ok(new { ok = true, node_id = nodeId });
        });

        return app;
    }

    private static async Task<IResult> ConnectLocalAsync(HttpRequest request, AgentSessionConnectRequest body, AgentGatewayService gateway, GatewayOptions options, CancellationToken ct)
    {
        try
        {
            var session = await gateway.ConnectAsync(body, ct);
            return Results.Ok(new
            {
                session,
                hub_url = BuildHubUrl(request),
                node_id = options.NodeId
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (NotSupportedException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (TimeoutException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static bool IsLocalNode(string nodeId, GatewayOptions options)
        => string.IsNullOrWhiteSpace(nodeId) || string.Equals(nodeId.Trim(), options.NodeId, StringComparison.Ordinal);

    private static string BuildHubUrl(HttpRequest request)
    {
        var protocol = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        return $"{protocol}://{request.Host}/hubs/agent";
    }
}
