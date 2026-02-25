using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/settings/global-quick-commands", (SessionManager manager) =>
            Results.Ok(new { quickCommands = manager.GetGlobalQuickCommands() }));

        app.MapPut("/settings/global-quick-commands", (SetQuickCommandsRequest request, SessionManager manager) =>
        {
            try
            {
                return Results.Ok(new { quickCommands = manager.SetGlobalQuickCommands(request.QuickCommands ?? []) });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/settings/fs-allowed-roots", (SessionManager manager) =>
            Results.Ok(new { fsAllowedRoots = manager.GetFsAllowedRoots() }));

        app.MapPut("/settings/fs-allowed-roots", (SetFsAllowedRootsRequest request, SessionManager manager) =>
        {
            try
            {
                return Results.Ok(new { fsAllowedRoots = manager.SetFsAllowedRoots(request.FsAllowedRoots ?? []) });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }
}
