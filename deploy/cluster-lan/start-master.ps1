param(
  [string]$ClusterToken = "dev-cluster-token",
  [string]$EnvironmentName = "ClusterWindowsMaster"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$project = Join-Path $repoRoot "apps\\terminal-gateway-dotnet\\TerminalGateway.Api\\TerminalGateway.Api.csproj"

$env:ASPNETCORE_ENVIRONMENT = $EnvironmentName
$env:DOTNET_ENVIRONMENT = $EnvironmentName
$env:CLUSTER_TOKEN = $ClusterToken

Write-Host "cluster master env=$EnvironmentName"
& dotnet run --project $project
