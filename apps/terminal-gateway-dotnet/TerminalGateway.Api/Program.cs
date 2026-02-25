using TerminalGateway.Api.Endpoints;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Pty;
using TerminalGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(JsonOptionsSetup.ConfigureHttpJson);

var options = GatewayOptions.FromEnvironment(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ISystemTimeProvider, SystemTimeProvider>();
builder.Services.AddSingleton<WriteTokenService>();
builder.Services.AddSingleton<IPtyEngine, PortaPtyEngine>();
builder.Services.AddSingleton(sp => new ProfileService(options.ProfileStoreFile));
builder.Services.AddSingleton(sp => new SettingsService(options.SettingsStoreFile, options.FsAllowedRoots));
builder.Services.AddSingleton<FsBrowserService>();
builder.Services.AddSingleton<ProjectDiscoveryService>();
builder.Services.AddSingleton(sp => new SessionManager(
    sp.GetRequiredService<IPtyEngine>(),
    sp.GetRequiredService<ProfileService>(),
    sp.GetRequiredService<SettingsService>(),
    sp.GetRequiredService<WriteTokenService>(),
    sp.GetRequiredService<ISystemTimeProvider>(),
    options.MaxOutputBufferBytes));

var app = builder.Build();
app.UseWebSockets();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/internal", StringComparison.Ordinal))
    {
        await next();
        return;
    }

    var token = context.Request.Headers["X-Internal-Token"].ToString();
    if (!string.Equals(token, options.InternalToken, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
        return;
    }

    await next();
});

app.MapHealthEndpoints();
app.MapProjectEndpoints();
app.MapFsEndpoints();
app.MapProfileEndpoints();
app.MapSettingsEndpoints();
app.MapSessionEndpoints();
app.MapInternalSessionEndpoints();
app.MapTerminalWebSocketEndpoint();

app.Run($"http://{options.Host}:{options.Port}");

public partial class Program;
