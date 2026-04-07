param(
  [string]$EnvironmentName = "SingleWindowsMaster"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$projectDir = Join-Path $repoRoot "apps\\terminal-gateway-dotnet\\TerminalGateway.Api"
$project = Join-Path $projectDir "TerminalGateway.Api.csproj"

$env:ASPNETCORE_ENVIRONMENT = $EnvironmentName
$env:DOTNET_ENVIRONMENT = $EnvironmentName

Write-Host "single-master env=$EnvironmentName"
& dotnet run --project $project
