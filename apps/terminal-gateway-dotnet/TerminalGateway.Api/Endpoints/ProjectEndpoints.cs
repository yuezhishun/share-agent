using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/projects/discover", (ProjectDiscoveryService service, GatewayOptions options) =>
        {
            return Results.Ok(service.Discover(options.CodexConfigPath, options.ClaudeConfigPath));
        });

        return app;
    }
}
