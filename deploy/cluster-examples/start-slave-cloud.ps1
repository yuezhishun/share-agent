param(
  [string]$MasterUrl = "https://your-master.example.com",
  [string]$ClusterToken = "dev-cluster-token"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$projectDir = Join-Path $repoRoot "apps\\terminal-gateway-dotnet\\TerminalGateway.Api"
$project = Join-Path $projectDir "TerminalGateway.Api.csproj"

$environmentName = "ClusterWindowsSlaveCloud"

$command = [string]::Join([Environment]::NewLine, @(
  "`$env:ASPNETCORE_ENVIRONMENT='$environmentName'"
  "`$env:DOTNET_ENVIRONMENT='$environmentName'"
  "`$env:MASTER_URL='$MasterUrl'"
  "`$env:CLUSTER_TOKEN='$ClusterToken'"
  "dotnet run --project '$project'"
))

Write-Host "slave env=$environmentName masterUrl=$MasterUrl"
powershell.exe -NoLogo -NoProfile -WorkingDirectory $projectDir -Command $command
