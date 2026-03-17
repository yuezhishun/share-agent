#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB_DIR="${WEB_DIR:-/www/wwwroot/pty-agent-web}"
GW_SERVICE="${GW_SERVICE:-terminal-gateway-dotnet.service}"
GW_SERVICE_FILE="/etc/systemd/system/${GW_SERVICE}"
LOG_DIR="${LOG_DIR:-/www/wwwlogs}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "please run as root (required to write ${GW_SERVICE_FILE} and manage systemd service)"
  exit 1
fi

mkdir -p "${LOG_DIR}"

cat > "${GW_SERVICE_FILE}" <<SERVICE
[Unit]
Description=PTY Agent Terminal Gateway Dotnet API
After=network.target

[Service]
Type=simple
WorkingDirectory=${REPO_ROOT}
Environment=HOST=127.0.0.1
Environment=PORT=7300
ExecStart=/usr/bin/dotnet run --project ${REPO_ROOT}/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
Restart=always
RestartSec=2
StandardOutput=append:${LOG_DIR}/terminal-gateway-dotnet.out.log
StandardError=append:${LOG_DIR}/terminal-gateway-dotnet.err.log

[Install]
WantedBy=multi-user.target
SERVICE

echo "[1/4] build secretary-web"
cd "${REPO_ROOT}/apps/secretary-web"
npm run build

echo "[2/4] publish dist -> ${WEB_DIR}"
mkdir -p "${WEB_DIR}"
cp -a "${REPO_ROOT}/apps/secretary-web/dist/." "${WEB_DIR}/"

echo "[3/4] reload systemd"
systemctl daemon-reload

echo "[4/4] ensure terminal-gateway-dotnet"
systemctl enable "${GW_SERVICE}"
systemctl restart "${GW_SERVICE}"

sleep 2
echo "--- service status ---"
systemctl --no-pager --lines=20 status "${GW_SERVICE}" || true

echo "deploy done"
