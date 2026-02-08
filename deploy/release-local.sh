#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB_DIR="${WEB_DIR:-/www/wwwroot/pty-agent-web}"
ORCH_SERVICE="${ORCH_SERVICE:-pty-orchestrator.service}"
GW_SERVICE="${GW_SERVICE:-terminal-gateway.service}"
ORCH_SERVICE_FILE="/etc/systemd/system/${ORCH_SERVICE}"

cat > "${ORCH_SERVICE_FILE}" <<'SERVICE'
[Unit]
Description=PTY Agent Orchestrator API
After=network.target terminal-gateway.service
Wants=terminal-gateway.service

[Service]
Type=simple
WorkingDirectory=/home/yueyuan/pty-agent
Environment=Runtime__TerminalBackend=nodepty
Environment=Runtime__TerminalGatewayBaseUrl=http://127.0.0.1:7300
Environment=Runtime__TerminalGatewayToken=dev-terminal-token
Environment=Runtime__TerminalGatewayTimeoutMs=5000
Environment=Orchestration__EngineProvider=maf
ExecStart=/usr/bin/dotnet run --project /home/yueyuan/pty-agent/apps/orchestrator/src/PtyAgent.Api/PtyAgent.Api.csproj --urls http://127.0.0.1:5121
Restart=always
RestartSec=2
StandardOutput=append:/www/wwwlogs/orchestrator.out.log
StandardError=append:/www/wwwlogs/orchestrator.err.log

[Install]
WantedBy=multi-user.target
SERVICE

echo "[1/5] build secretary-web"
cd "${REPO_ROOT}/apps/secretary-web"
npm run build

echo "[2/5] publish dist -> ${WEB_DIR}"
mkdir -p "${WEB_DIR}"
cp -a "${REPO_ROOT}/apps/secretary-web/dist/." "${WEB_DIR}/"

echo "[3/5] reload systemd"
systemctl daemon-reload

echo "[4/5] ensure terminal-gateway"
systemctl enable "${GW_SERVICE}"
systemctl restart "${GW_SERVICE}"

echo "[5/5] ensure orchestrator"
systemctl enable "${ORCH_SERVICE}"
systemctl restart "${ORCH_SERVICE}"

sleep 2
echo "--- service status ---"
systemctl --no-pager --lines=20 status "${GW_SERVICE}" || true
systemctl --no-pager --lines=20 status "${ORCH_SERVICE}" || true

echo "deploy done"
