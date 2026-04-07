#!/usr/bin/env bash
set -euo pipefail

CLUSTER_TOKEN="${CLUSTER_TOKEN:-dev-cluster-token}"
ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-ClusterLinuxMaster}"

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="${REPO_ROOT}/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj"

export ASPNETCORE_ENVIRONMENT="${ENVIRONMENT_NAME}"
export DOTNET_ENVIRONMENT="${ENVIRONMENT_NAME}"
export CLUSTER_TOKEN

echo "cluster master env=${ENVIRONMENT_NAME}"
exec dotnet run --project "${PROJECT}"
