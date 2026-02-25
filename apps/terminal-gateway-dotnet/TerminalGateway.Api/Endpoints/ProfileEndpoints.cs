using TerminalGateway.Api.Models;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Endpoints;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/profiles", (SessionManager manager) => Results.Ok(manager.ListProfiles()));

        app.MapPost("/profiles", (CreateProfileRequest request, SessionManager manager) =>
        {
            try
            {
                return Results.Ok(manager.CreateProfile(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPut("/profiles/{profileId}", (string profileId, UpdateProfileRequest request, SessionManager manager) =>
        {
            try
            {
                return Results.Ok(manager.UpdateProfile(profileId, request));
            }
            catch (Exception ex)
            {
                var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                return Results.Json(new { error = ex.Message }, statusCode: code);
            }
        });

        app.MapDelete("/profiles/{profileId}", (string profileId, SessionManager manager) =>
        {
            try
            {
                return Results.Ok(manager.DeleteProfile(profileId));
            }
            catch (Exception ex)
            {
                var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                return Results.Json(new { error = ex.Message }, statusCode: code);
            }
        });

        return app;
    }
}
