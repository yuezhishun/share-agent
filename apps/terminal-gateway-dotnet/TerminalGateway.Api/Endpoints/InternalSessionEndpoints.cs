using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class InternalSessionEndpoints
{
    public static IEndpointRouteBuilder MapInternalSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/sessions", async (CreateSessionRequest request, SessionManager manager, CancellationToken ct) =>
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

        app.MapGet("/internal/sessions/{sessionId}", (string sessionId, SessionManager manager) =>
        {
            try
            {
                return Results.Ok(manager.Status(sessionId));
            }
            catch (Exception ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPost("/internal/sessions/{sessionId}/input", async (string sessionId, SessionInputRequest request, SessionManager manager, CancellationToken ct) =>
        {
            try
            {
                await manager.WriteAsync(sessionId, request.Data, ct);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPost("/internal/sessions/{sessionId}/resize", async (string sessionId, SessionResizeRequest request, SessionManager manager, CancellationToken ct) =>
        {
            try
            {
                await manager.ResizeAsync(sessionId, request.Cols, request.Rows, ct);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPost("/internal/sessions/{sessionId}/terminate", async (string sessionId, SessionTerminateRequest request, SessionManager manager, CancellationToken ct) =>
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

        return app;
    }

    private static Dictionary<string, object?> ToDictionary(object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
    }
}
