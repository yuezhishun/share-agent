param(
  [string]$MasterUrl = "https://pty.addai.vip",
  [string]$ClusterToken = "dev-cluster-token",
  [string]$EnvironmentName = "ClusterWindowsSlaveLocal"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$project = Join-Path $repoRoot "apps\\terminal-gateway-dotnet\\TerminalGateway.Api\\TerminalGateway.Api.csproj"

$env:ASPNETCORE_ENVIRONMENT = $EnvironmentName
$env:DOTNET_ENVIRONMENT = $EnvironmentName
$env:MASTER_URL = $MasterUrl
$env:CLUSTER_TOKEN = $ClusterToken

Write-Host "cluster slave env=$EnvironmentName masterUrl=$MasterUrl"
& dotnet run --project $project
