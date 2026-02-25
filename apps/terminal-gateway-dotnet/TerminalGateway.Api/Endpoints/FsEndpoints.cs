using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class FsEndpoints
{
    public static IEndpointRouteBuilder MapFsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/fs/dirs", (string? path, FsBrowserService fs, SessionManager manager) =>
        {
            try
            {
                return Results.Ok(fs.ListDirectories(path, manager.GetFsAllowedRoots()));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }
}
