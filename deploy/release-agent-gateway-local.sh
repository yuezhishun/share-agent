#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GW_SERVICE="${GW_SERVICE:-terminal-gateway-dotnet.service}"
GW_SERVICE_FILE="/etc/systemd/system/${GW_SERVICE}"
LOG_DIR="${LOG_DIR:-/www/wwwlogs}"
PORT="${PORT:-7300}"
HOST="${HOST:-127.0.0.1}"
FILES_BASE_PATH="${FILES_BASE_PATH:-/home/yueyuan}"
AIONUI_ENV_FILE="${AIONUI_ENV_FILE:-/etc/default/aionui-agent-gateway}"
AIONUI_GATEWAY_URL="${AIONUI_GATEWAY_URL:-http://${HOST}:${PORT}}"
AIONUI_GATEWAY_TOKEN="${AIONUI_GATEWAY_TOKEN:-dev-terminal-token}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "please run as root (required to write ${GW_SERVICE_FILE} and manage systemd service)"
  exit 1
fi

mkdir -p "${LOG_DIR}"
mkdir -p "$(dirname "${AIONUI_ENV_FILE}")"

cat > "${GW_SERVICE_FILE}" <<SERVICE
[Unit]
Description=PTY Agent Terminal Gateway Dotnet API
After=network.target

[Service]
Type=simple
WorkingDirectory=${REPO_ROOT}
Environment=HOST=${HOST}
Environment=PORT=${PORT}
Environment=FILES_BASE_PATH=${FILES_BASE_PATH}
Environment=AGENT_GATEWAY_ENABLED=1
Environment=TERMINAL_GATEWAY_TOKEN=${AIONUI_GATEWAY_TOKEN}
ExecStart=/usr/bin/dotnet run --project ${REPO_ROOT}/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
Restart=always
RestartSec=2
StandardOutput=append:${LOG_DIR}/terminal-gateway-dotnet.out.log
StandardError=append:${LOG_DIR}/terminal-gateway-dotnet.err.log

[Install]
WantedBy=multi-user.target
SERVICE

cat > "${AIONUI_ENV_FILE}" <<ENVFILE
AGENT_GATEWAY_ENABLED=1
AIONUI_AGENT_GATEWAY_URL=${AIONUI_GATEWAY_URL}
AIONUI_AGENT_GATEWAY_TOKEN=${AIONUI_GATEWAY_TOKEN}
ENVFILE

echo "[1/3] reload systemd"
systemctl daemon-reload

echo "[2/3] restart ${GW_SERVICE}"
systemctl enable "${GW_SERVICE}"
systemctl restart "${GW_SERVICE}"

echo "[3/3] wrote AionUi environment file -> ${AIONUI_ENV_FILE}"
echo "--- service status ---"
systemctl --no-pager --lines=20 status "${GW_SERVICE}" || true

echo "agent gateway deploy done"
