#!/usr/bin/env bash
set -euo pipefail

ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-ClusterLinuxSlaveLocal}"
SERVICE_NAME="${SERVICE_NAME:-terminal-gateway-slave.service}"
ENV_FILE="${ENV_FILE:-/etc/default/terminal-gateway-slave}"
LOG_DIR="${LOG_DIR:-/www/wwwlogs}"
HOST_VALUE="${HOST_VALUE:-127.0.0.1}"
PORT_VALUE="${PORT_VALUE:-7320}"
FILES_BASE_PATH="${FILES_BASE_PATH:-/home/yueyuan}"
MASTER_URL="${MASTER_URL:-https://pty.addai.vip}"
CLUSTER_TOKEN="${CLUSTER_TOKEN:-dev-cluster-token}"
SETTINGS_STORE_FILE="${TERMINAL_SETTINGS_STORE_FILE:-/tmp/pty-agent-terminal-settings-slave.json}"
PROFILE_STORE_FILE="${TERMINAL_PROFILE_STORE_FILE:-/tmp/pty-agent-terminal-profiles-slave.json}"
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "please run as root"
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="${REPO_ROOT}/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj"

mkdir -p "${LOG_DIR}" "$(dirname "${ENV_FILE}")"

cat > "${ENV_FILE}" <<ENVFILE
HOST=${HOST_VALUE}
PORT=${PORT_VALUE}
MASTER_URL=${MASTER_URL}
CLUSTER_TOKEN=${CLUSTER_TOKEN}
FILES_BASE_PATH=${FILES_BASE_PATH}
TERMINAL_SETTINGS_STORE_FILE=${SETTINGS_STORE_FILE}
TERMINAL_PROFILE_STORE_FILE=${PROFILE_STORE_FILE}
ENVFILE

cat > "${SERVICE_FILE}" <<SERVICE
[Unit]
Description=Terminal Gateway Cluster Slave
After=network.target

[Service]
Type=simple
WorkingDirectory=${REPO_ROOT}
Environment=ASPNETCORE_ENVIRONMENT=${ENVIRONMENT_NAME}
Environment=DOTNET_ENVIRONMENT=${ENVIRONMENT_NAME}
EnvironmentFile=-${ENV_FILE}
ExecStart=/usr/bin/dotnet run --project ${PROJECT}
Restart=always
RestartSec=2
StandardOutput=append:${LOG_DIR}/terminal-gateway-slave.out.log
StandardError=append:${LOG_DIR}/terminal-gateway-slave.err.log

[Install]
WantedBy=multi-user.target
SERVICE

systemctl daemon-reload
systemctl enable "${SERVICE_NAME}"
systemctl restart "${SERVICE_NAME}"
systemctl --no-pager --lines=20 status "${SERVICE_NAME}" || true

echo "cluster slave service installed"
