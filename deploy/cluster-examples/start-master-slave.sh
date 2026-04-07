#!/usr/bin/env bash
set -euo pipefail

CLUSTER_TOKEN="${CLUSTER_TOKEN:-dev-cluster-token}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="$REPO_ROOT/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj"
MASTER_ENVIRONMENT="ClusterLinuxMaster"
SLAVE_ENVIRONMENT="ClusterLinuxSlaveLocal"

cleanup() {
  if [[ -n "${MASTER_PID:-}" ]]; then kill "$MASTER_PID" 2>/dev/null || true; fi
  if [[ -n "${SLAVE_PID:-}" ]]; then kill "$SLAVE_PID" 2>/dev/null || true; fi
}
trap cleanup EXIT INT TERM

(
  export ASPNETCORE_ENVIRONMENT="$MASTER_ENVIRONMENT"
  export DOTNET_ENVIRONMENT="$MASTER_ENVIRONMENT"
  export CLUSTER_TOKEN
  exec dotnet run --project "$PROJECT"
) &
MASTER_PID=$!

sleep 3

(
  export ASPNETCORE_ENVIRONMENT="$SLAVE_ENVIRONMENT"
  export DOTNET_ENVIRONMENT="$SLAVE_ENVIRONMENT"
  export CLUSTER_TOKEN
  exec dotnet run --project "$PROJECT"
) &
SLAVE_PID=$!

echo "master  http://127.0.0.1:7310"
echo "slave   http://127.0.0.1:7320"
echo "environments:"
echo "  master -> $MASTER_ENVIRONMENT"
echo "  slave  -> $SLAVE_ENVIRONMENT"

wait "$MASTER_PID" "$SLAVE_PID"
