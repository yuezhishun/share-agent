using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class ProcessEndpoints
{
    public static IEndpointRouteBuilder MapProcessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/processes/run", async (RunProcessRequest body, ProcessApiService processes, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await processes.RunAsync(body, ct));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/processes", async (RunProcessRequest body, ProcessApiService processes, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await processes.StartManagedAsync(body, ct));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/processes", (ProcessApiService processes) => Results.Ok(new { items = processes.ListManaged() }));

        app.MapGet("/api/processes/{processId}", (string processId, ProcessApiService processes) =>
        {
            try
            {
                return Results.Ok(processes.GetManaged(processId));
            }
            catch (Exception ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/api/processes/{processId}/output", (string processId, ProcessApiService processes) =>
        {
            try
            {
                return Results.Ok(new { items = processes.GetOutput(processId) });
            }
            catch (Exception ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPost("/api/processes/{processId}/wait", async (string processId, int? timeout_ms, ProcessApiService processes) =>
        {
            try
            {
                return Results.Ok(await processes.WaitManagedAsync(processId, timeout_ms));
            }
            catch (Exception ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPost("/api/processes/{processId}/stop", async (string processId, StopManagedProcessRequest body, ProcessApiService processes) =>
        {
            try
            {
                return Results.Ok(await processes.StopManagedAsync(processId, body.Force == true));
            }
            catch (Exception ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        return app;
    }
}
