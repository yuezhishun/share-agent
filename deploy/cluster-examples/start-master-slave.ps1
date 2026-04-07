param(
  [string]$ClusterToken = "dev-cluster-token"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$projectDir = Join-Path $repoRoot "apps\\terminal-gateway-dotnet\\TerminalGateway.Api"
$project = Join-Path $projectDir "TerminalGateway.Api.csproj"

function Start-GatewayProcess {
  param(
    [string]$EnvironmentName
  )

  $lines = @(
    "`$env:ASPNETCORE_ENVIRONMENT='$EnvironmentName'"
    "`$env:DOTNET_ENVIRONMENT='$EnvironmentName'"
    "`$env:CLUSTER_TOKEN='$ClusterToken'"
    "dotnet run --project '$project'"
  )
  $command = [string]::Join([Environment]::NewLine, $lines)
  return Start-Process powershell.exe -WorkingDirectory $projectDir -ArgumentList @("-NoLogo", "-NoProfile", "-Command", $command) -PassThru
}

$masterEnvironment = "ClusterWindowsMaster"
$slaveEnvironment = "ClusterWindowsSlaveLocal"

$master = Start-GatewayProcess -EnvironmentName $masterEnvironment

Start-Sleep -Seconds 3

$slave = Start-GatewayProcess -EnvironmentName $slaveEnvironment

Write-Host "master  http://127.0.0.1:7310"
Write-Host "slave   http://127.0.0.1:7320"
Write-Host "environments:"
Write-Host "  master -> $masterEnvironment"
Write-Host "  slave  -> $slaveEnvironment"

function Wait-ForGatewayProcesses {
  param(
    [Parameter(Mandatory = $true)]
    [System.Diagnostics.Process[]]$Processes
  )

  while ($true) {
    $activeProcesses = @(
      $Processes |
        Where-Object { $_ -and -not $_.HasExited }
    )

    if ($activeProcesses.Count -eq 0) {
      break
    }

    $exited = $false
    while (-not $exited) {
      foreach ($process in $activeProcesses) {
        if ($process.WaitForExit(500)) {
          $exited = $true
          break
        }
      }
    }

    foreach ($process in $Processes) {
      if ($process) {
        $null = $process.Refresh()
      }
    }
  }
}

try {
  Wait-ForGatewayProcesses -Processes @($master, $slave)

  $failed = @($master, $slave) | Where-Object { $_.ExitCode -ne 0 }
  if ($failed.Count -gt 0) {
    $details = $failed | ForEach-Object { "PID $($_.Id) exited with code $($_.ExitCode)" }
    throw "Gateway process exited early. $([string]::Join('; ', $details))"
  }
} finally {
  if (!$master.HasExited) { Stop-Process -Id $master.Id -Force }
  if (!$slave.HasExited) { Stop-Process -Id $slave.Id -Force }
}
