using TerminalGateway.Api.Endpoints;
using TerminalGateway.Api.Infrastructure;
using TerminalGateway.Api.Pty;
using TerminalGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(JsonOptionsSetup.ConfigureHttpJson);

var options = GatewayOptions.FromConfiguration(builder.Configuration);
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
builder.Services.AddSingleton<SlaveClusterBridgeService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SlaveClusterBridgeService>());

var app = builder.Build();

app.MapApiRoutes();
app.MapProcessEndpoints();
app.MapHub<TerminalHub>("/hubs/terminal");
app.MapHub<ClusterHub>("/hubs/cluster");

_ = app.Services.GetRequiredService<TerminalEventRelay>();

app.Run($"http://{options.Host}:{options.Port}");

public partial class Program;
