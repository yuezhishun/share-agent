#!/usr/bin/env bash
set -euo pipefail

MASTER_URL="${MASTER_URL:-https://pty.addai.vip}"
CLUSTER_TOKEN="${CLUSTER_TOKEN:-dev-cluster-token}"
ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-ClusterLinuxSlaveLocal}"

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="${REPO_ROOT}/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj"

export ASPNETCORE_ENVIRONMENT="${ENVIRONMENT_NAME}"
export DOTNET_ENVIRONMENT="${ENVIRONMENT_NAME}"
export MASTER_URL
export CLUSTER_TOKEN

echo "cluster slave env=${ENVIRONMENT_NAME} masterUrl=${MASTER_URL}"
exec dotnet run --project "${PROJECT}"
