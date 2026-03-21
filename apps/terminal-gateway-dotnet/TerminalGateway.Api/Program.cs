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
builder.Services.AddSingleton<TerminalConnectionRegistryV2>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<TerminalEventRelay>();
builder.Services.AddSingleton<TerminalOracleManager>();
builder.Services.AddSingleton<TerminalEventRelayV2>();
builder.Services.AddSingleton<NodeRegistry>();
builder.Services.AddSingleton<RemoteInstanceRegistry>();
builder.Services.AddSingleton<ClusterCommandBroker>();
builder.Services.AddSingleton<ClusterCommandExecutor>();
builder.Services.AddSingleton<ClusterEventDeduplicator>();
builder.Services.AddSingleton<FileApiService>();
builder.Services.AddSingleton<ProjectApiService>();
builder.Services.AddSingleton<ProcessApiService>();
builder.Services.AddHostedService<SlaveClusterBridgeService>();

var app = builder.Build();

app.MapApiRoutes();
app.MapApiRoutesV2();
app.MapProcessEndpoints();
app.MapHub<TerminalHubV2>("/hubs/terminal");
app.MapHub<TerminalHubV2>("/hubs/terminal-v2");
app.MapHub<ClusterHub>("/hubs/cluster");

_ = app.Services.GetRequiredService<TerminalEventRelay>();
_ = app.Services.GetRequiredService<TerminalEventRelayV2>();

app.Run($"http://{options.Host}:{options.Port}");

public partial class Program;
