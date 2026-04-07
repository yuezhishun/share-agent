using TerminalGateway.Api.Endpoints;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Pty;
using TerminalGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(JsonOptionsSetup.ConfigureHttpJson);

var options = GatewayOptions.FromConfiguration(builder.Configuration);
EnsureGatewayDirectories(options);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IPtyEngine, PortaPtyEngine>();
builder.Services.AddSingleton<InstanceManager>(sp => new InstanceManager(
    sp.GetRequiredService<IPtyEngine>(),
    options.HistoryLimit,
    options.RawReplayMaxBytes,
    options.DefaultCols,
    options.DefaultRows,
    options.NodeId,
    options.NodeName,
    options.GatewayRole,
    options.PathPrefixes,
    options.GitBashPath));
builder.Services.AddSingleton<TerminalConnectionRegistry>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<TerminalOracleManager>();
builder.Services.AddSingleton<TerminalEventRelay>();
builder.Services.AddSingleton<NodeRegistry>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RemoteInstanceRegistry>();
builder.Services.AddSingleton<ClusterCommandBroker>();
builder.Services.AddSingleton<ClusterCommandExecutor>();
builder.Services.AddSingleton<ClusterEventDeduplicator>();
builder.Services.AddSingleton<ClusterTerminalSubscriptionService>();
builder.Services.AddSingleton<FileApiService>();
builder.Services.AddSingleton<ProjectApiService>();
builder.Services.AddSingleton<ProcessApiService>();
builder.Services.AddSingleton<AgentCatalogService>();
builder.Services.AddSingleton<AgentGatewayService>();
builder.Services.AddSingleton(sp => new CliTemplateService(
    sp.GetRequiredService<GatewayOptions>().CliTemplateDbPath,
    sp.GetRequiredService<GatewayOptions>().FilesBasePath));
builder.Services.AddSingleton(sp => new TerminalEnvService(
    sp.GetRequiredService<GatewayOptions>().CliTemplateDbPath));
builder.Services.AddSingleton(sp => new TerminalShortcutService(
    sp.GetRequiredService<GatewayOptions>().CliTemplateDbPath));
builder.Services.AddSingleton<CliProcessService>();
builder.Services.AddSingleton<SlaveClusterBridgeService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SlaveClusterBridgeService>());

var app = builder.Build();

app.MapApiRoutes();
app.MapProcessEndpoints();
app.MapAgentGatewayEndpoints();
app.MapCliEndpoints();
app.MapTerminalShortcutEndpoints();
app.MapHub<TerminalHub>("/hubs/terminal");
app.MapHub<AgentHub>("/hubs/agent");
app.MapHub<ClusterHub>("/hubs/cluster");

_ = app.Services.GetRequiredService<TerminalEventRelay>();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run($"http://{options.Host}:{options.Port}");



static void EnsureGatewayDirectories(GatewayOptions options)
{
    EnsureDirectory(options.FilesBasePath);
    EnsureParentDirectory(options.CliTemplateDbPath);
    EnsureParentDirectory(options.SettingsStoreFile);
}

static void EnsureDirectory(string? path)
{
    var normalized = (path ?? string.Empty).Trim();
    if (normalized.Length == 0 || !Path.IsPathRooted(normalized))
    {
        return;
    }

    Directory.CreateDirectory(Path.GetFullPath(normalized));
}

static void EnsureParentDirectory(string? filePath)
{
    var normalized = (filePath ?? string.Empty).Trim();
    if (normalized.Length == 0 || !Path.IsPathRooted(normalized))
    {
        return;
    }

    var parent = Path.GetDirectoryName(Path.GetFullPath(normalized));
    if (!string.IsNullOrWhiteSpace(parent))
    {
        Directory.CreateDirectory(parent);
    }
}
