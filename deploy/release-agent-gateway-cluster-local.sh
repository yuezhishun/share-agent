#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MASTER_SERVICE="${MASTER_SERVICE:-terminal-gateway-master.service}"
SLAVE_SERVICE="${SLAVE_SERVICE:-terminal-gateway-slave.service}"
MASTER_SERVICE_FILE="/etc/systemd/system/${MASTER_SERVICE}"
SLAVE_SERVICE_FILE="/etc/systemd/system/${SLAVE_SERVICE}"
LOG_DIR="${LOG_DIR:-/www/wwwlogs}"
MASTER_PORT="${MASTER_PORT:-7310}"
SLAVE_PORT="${SLAVE_PORT:-7320}"
MASTER_URL="${MASTER_URL:-http://127.0.0.1:${MASTER_PORT}}"
CLUSTER_TOKEN="${CLUSTER_TOKEN:-dev-cluster-token}"
MASTER_NODE_ID="${MASTER_NODE_ID:-master-local}"
SLAVE_NODE_ID="${SLAVE_NODE_ID:-slave-local}"
MASTER_FILES_BASE_PATH="${MASTER_FILES_BASE_PATH:-/home/yueyuan}"
SLAVE_FILES_BASE_PATH="${SLAVE_FILES_BASE_PATH:-/home/yueyuan}"
AIONUI_ENV_FILE="${AIONUI_ENV_FILE:-/etc/default/aionui-agent-gateway}"
AIONUI_GATEWAY_TOKEN="${AIONUI_GATEWAY_TOKEN:-dev-terminal-token}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "please run as root (required to write systemd service files and manage services)"
  exit 1
fi

mkdir -p "${LOG_DIR}"
mkdir -p "$(dirname "${AIONUI_ENV_FILE}")"

write_service() {
  local service_file="$1"
  local role="$2"
  local port="$3"
  local node_id="$4"
  local files_base="$5"
  local master_url="$6"

  cat > "${service_file}" <<SERVICE
[Unit]
Description=PTY Agent Terminal Gateway (${role})
After=network.target

[Service]
Type=simple
WorkingDirectory=${REPO_ROOT}
Environment=HOST=127.0.0.1
Environment=PORT=${port}
Environment=GATEWAY_ROLE=${role}
Environment=NODE_ID=${node_id}
Environment=FILES_BASE_PATH=${files_base}
Environment=CLUSTER_TOKEN=${CLUSTER_TOKEN}
Environment=AGENT_GATEWAY_ENABLED=1
Environment=TERMINAL_GATEWAY_TOKEN=${AIONUI_GATEWAY_TOKEN}
SERVICE

  if [[ -n "${master_url}" ]]; then
    cat >> "${service_file}" <<SERVICE
Environment=MASTER_URL=${master_url}
SERVICE
  fi

  cat >> "${service_file}" <<SERVICE
ExecStart=/usr/bin/dotnet run --project ${REPO_ROOT}/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
Restart=always
RestartSec=2
StandardOutput=append:${LOG_DIR}/$(basename "${service_file}" .service).out.log
StandardError=append:${LOG_DIR}/$(basename "${service_file}" .service).err.log

[Install]
WantedBy=multi-user.target
SERVICE
}

write_service "${MASTER_SERVICE_FILE}" "master" "${MASTER_PORT}" "${MASTER_NODE_ID}" "${MASTER_FILES_BASE_PATH}" ""
write_service "${SLAVE_SERVICE_FILE}" "slave" "${SLAVE_PORT}" "${SLAVE_NODE_ID}" "${SLAVE_FILES_BASE_PATH}" "${MASTER_URL}"

cat > "${AIONUI_ENV_FILE}" <<ENVFILE
AGENT_GATEWAY_ENABLED=1
AIONUI_AGENT_GATEWAY_URL=${MASTER_URL}
AIONUI_AGENT_GATEWAY_TOKEN=${AIONUI_GATEWAY_TOKEN}
ENVFILE

echo "[1/3] reload systemd"
systemctl daemon-reload

echo "[2/3] restart master/slave services"
systemctl enable "${MASTER_SERVICE}" "${SLAVE_SERVICE}"
systemctl restart "${MASTER_SERVICE}" "${SLAVE_SERVICE}"

echo "[3/3] wrote AionUi environment file -> ${AIONUI_ENV_FILE}"
echo "--- master service status ---"
systemctl --no-pager --lines=20 status "${MASTER_SERVICE}" || true
echo "--- slave service status ---"
systemctl --no-pager --lines=20 status "${SLAVE_SERVICE}" || true

echo "agent gateway cluster deploy done"
