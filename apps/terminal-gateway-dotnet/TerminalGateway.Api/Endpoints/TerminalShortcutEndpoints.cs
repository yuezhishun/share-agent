using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class TerminalShortcutEndpoints
{
    public static IEndpointRouteBuilder MapTerminalShortcutEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/terminal/shortcuts", (TerminalShortcutService shortcuts) =>
            Results.Ok(new { items = shortcuts.List() }));

        app.MapPost("/api/terminal/shortcuts", (CreateTerminalShortcutRequest body, TerminalShortcutService shortcuts) =>
        {
            try
            {
                return Results.Ok(shortcuts.Create(body));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPut("/api/terminal/shortcuts/{shortcutId}", (string shortcutId, UpdateTerminalShortcutRequest body, TerminalShortcutService shortcuts) =>
        {
            try
            {
                return Results.Ok(shortcuts.Update(shortcutId, body));
            }
            catch (Exception ex)
            {
                var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status400BadRequest;
                return Results.Json(new { error = ex.Message, shortcut_id = shortcutId }, statusCode: code);
            }
        });

        app.MapDelete("/api/terminal/shortcuts/{shortcutId}", (string shortcutId, TerminalShortcutService shortcuts) =>
        {
            try
            {
                return Results.Ok(shortcuts.Delete(shortcutId));
            }
            catch (Exception ex)
            {
                var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status400BadRequest;
                return Results.Json(new { error = ex.Message, shortcut_id = shortcutId }, statusCode: code);
            }
        });

        return app;
    }
}
