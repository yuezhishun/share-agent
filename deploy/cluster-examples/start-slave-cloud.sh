#!/usr/bin/env bash
set -euo pipefail

MASTER_URL="${MASTER_URL:-https://your-master.example.com}"
CLUSTER_TOKEN="${CLUSTER_TOKEN:-dev-cluster-token}"

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="$REPO_ROOT/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj"
ENVIRONMENT_NAME="ClusterLinuxSlaveCloud"

export ASPNETCORE_ENVIRONMENT="$ENVIRONMENT_NAME"
export DOTNET_ENVIRONMENT="$ENVIRONMENT_NAME"
export MASTER_URL
export CLUSTER_TOKEN

echo "slave env=$ENVIRONMENT_NAME masterUrl=$MASTER_URL"
exec dotnet run --project "$PROJECT"
