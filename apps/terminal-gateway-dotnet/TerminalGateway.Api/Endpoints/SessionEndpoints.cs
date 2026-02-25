using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sessions", (bool? includeExited, string? profileId, string? taskId, SessionManager manager) =>
        {
            var include = includeExited ?? true;
            return Results.Ok(manager.List(include, profileId, taskId));
        });

        app.MapPost("/sessions", async (CreateSessionRequest request, SessionManager manager, CancellationToken ct) =>
        {
            try
            {
                var created = await manager.CreateAsync(request, ct);
                return Results.Ok(new Dictionary<string, object?>((IDictionary<string, object?>)ToDictionary(created.Session))
                {
                    ["writeToken"] = created.WriteToken
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/sessions/{sessionId}/terminate", async (string sessionId, SessionTerminateRequest request, SessionManager manager, CancellationToken ct) =>
        {
            try
            {
                await manager.TerminateAsync(sessionId, request.Signal, ct);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapDelete("/sessions/{sessionId}", (string sessionId, SessionManager manager) =>
        {
            try
            {
                return Results.Ok(manager.Remove(sessionId));
            }
            catch (Exception ex)
            {
                var code = ex.Message.Contains("cannot remove running session", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status404NotFound;
                return Results.Json(new { error = ex.Message }, statusCode: code);
            }
        });

        app.MapPost("/sessions/prune-exited", (SessionManager manager) => Results.Ok(manager.PruneExited()));

        app.MapGet("/sessions/{sessionId}/snapshot", (string sessionId, int? limitBytes, SessionManager manager) =>
        {
            try
            {
                return Results.Ok(manager.Snapshot(sessionId, limitBytes));
            }
            catch (Exception ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/sessions/{sessionId}/history", (string sessionId, int? beforeSeq, int? limitBytes, SessionManager manager) =>
        {
            try
            {
                return Results.Ok(manager.History(sessionId, beforeSeq, limitBytes));
            }
            catch (Exception ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        return app;
    }

    private static Dictionary<string, object?> ToDictionary(object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
    }
}
