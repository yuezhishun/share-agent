#!/usr/bin/env bash
set -euo pipefail

ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-SingleLinuxMaster}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="${REPO_ROOT}/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj"

export ASPNETCORE_ENVIRONMENT="${ENVIRONMENT_NAME}"
export DOTNET_ENVIRONMENT="${ENVIRONMENT_NAME}"

echo "single-master env=${ENVIRONMENT_NAME}"
exec dotnet run --project "${PROJECT}"
