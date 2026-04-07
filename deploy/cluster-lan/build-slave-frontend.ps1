#!/usr/bin/env pwsh
#requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$WebDir = if ($env:WEB_DIR) { $env:WEB_DIR } else { Join-Path $RepoRoot "nginx\\html-slave" }
$AppDir = Join-Path $RepoRoot "apps\\secretary-web"
$DistDir = Join-Path $AppDir "dist"

$env:VITE_WEBPTY_BASE = ''
$env:VITE_WEBPTY_HUB_PATH = '/hubs/terminal'
$env:VITE_APP_BASE_PATH = '/'

Push-Location $AppDir
try {
  npm run build
  if ($LASTEXITCODE -ne 0) {
    throw "npm run build failed with exit code $LASTEXITCODE"
  }
} finally {
  Pop-Location
  Remove-Item Env:\VITE_WEBPTY_BASE -ErrorAction SilentlyContinue
  Remove-Item Env:\VITE_WEBPTY_HUB_PATH -ErrorAction SilentlyContinue
  Remove-Item Env:\VITE_APP_BASE_PATH -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $WebDir | Out-Null
Remove-Item -Path (Join-Path $WebDir '*') -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $DistDir '*') -Destination $WebDir -Recurse -Force

Write-Host "cluster-lan slave frontend build done -> $WebDir"
