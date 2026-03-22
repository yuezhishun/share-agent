#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_PROJECT="${REPO_ROOT}/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj"

MASTER_WEB_DIR="${MASTER_WEB_DIR:-/www/wwwroot/pty-agent-web-master}"
SLAVE_WEB_DIR="${SLAVE_WEB_DIR:-/www/wwwroot/pty-agent-web-slave}"
MASTER_WEBPTY_BASE="${MASTER_WEBPTY_BASE:-/web-pty}"
MASTER_HUB_PATH="${MASTER_HUB_PATH:-/hubs/terminal}"
MASTER_APP_BASE_PATH="${MASTER_APP_BASE_PATH:-/}"
SLAVE_WEBPTY_BASE="${SLAVE_WEBPTY_BASE:-/slave/web-pty}"
SLAVE_HUB_PATH="${SLAVE_HUB_PATH:-/slave/hubs/terminal}"
SLAVE_APP_BASE_PATH="${SLAVE_APP_BASE_PATH:-/slave/}"

MASTER_SERVICE="${MASTER_SERVICE:-terminal-gateway-master.service}"
SLAVE_SERVICE="${SLAVE_SERVICE:-terminal-gateway-slave.service}"
MASTER_SERVICE_FILE="/etc/systemd/system/${MASTER_SERVICE}"
SLAVE_SERVICE_FILE="/etc/systemd/system/${SLAVE_SERVICE}"

LOG_DIR="${LOG_DIR:-/www/wwwlogs}"
MASTER_PORT="${MASTER_PORT:-7310}"
SLAVE_PORT="${SLAVE_PORT:-7320}"
HOST_ADDR="${HOST_ADDR:-127.0.0.1}"

MASTER_NODE_ID="${MASTER_NODE_ID:-master-local}"
MASTER_NODE_NAME="${MASTER_NODE_NAME:-Master Local}"
SLAVE_NODE_ID="${SLAVE_NODE_ID:-slave-local}"
SLAVE_NODE_NAME="${SLAVE_NODE_NAME:-Slave Local}"

MASTER_URL="${MASTER_URL:-http://${HOST_ADDR}:${MASTER_PORT}}"
CLUSTER_TOKEN="${CLUSTER_TOKEN:-dev-cluster-token}"
MASTER_FILES_BASE_PATH="${MASTER_FILES_BASE_PATH:-${FILES_BASE_PATH:-/home/yueyuan}}"
SLAVE_FILES_BASE_PATH="${SLAVE_FILES_BASE_PATH:-${FILES_BASE_PATH:-/home/yueyuan/gitlab}}"

MASTER_SETTINGS_STORE_FILE="${MASTER_SETTINGS_STORE_FILE:-/tmp/pty-agent-terminal-settings-master.json}"
SLAVE_SETTINGS_STORE_FILE="${SLAVE_SETTINGS_STORE_FILE:-/tmp/pty-agent-terminal-settings-slave.json}"
MASTER_PROFILE_STORE_FILE="${MASTER_PROFILE_STORE_FILE:-/tmp/pty-agent-terminal-profiles-master.json}"
SLAVE_PROFILE_STORE_FILE="${SLAVE_PROFILE_STORE_FILE:-/tmp/pty-agent-terminal-profiles-slave.json}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "please run as root (required to write systemd service files and manage services)"
  exit 1
fi

mkdir -p "${LOG_DIR}" "${MASTER_WEB_DIR}" "${SLAVE_WEB_DIR}"

write_service() {
  local service_file="$1"
  local role="$2"
  local port="$3"
  local node_id="$4"
  local node_name="$5"
  local master_url="$6"
  local settings_store_file="$7"
  local profile_store_file="$8"
  local files_base_path="$9"

  cat > "${service_file}" <<SERVICE
[Unit]
Description=PTY Agent Terminal Gateway (${role})
After=network.target

[Service]
Type=simple
WorkingDirectory=${REPO_ROOT}
Environment=HOST=${HOST_ADDR}
Environment=PORT=${port}
Environment=GATEWAY_ROLE=${role}
Environment=NODE_ID=${node_id}
Environment=NODE_NAME=${node_name}
Environment=CLUSTER_TOKEN=${CLUSTER_TOKEN}
Environment=FILES_BASE_PATH=${files_base_path}
Environment=TERMINAL_SETTINGS_STORE_FILE=${settings_store_file}
Environment=TERMINAL_PROFILE_STORE_FILE=${profile_store_file}
SERVICE

  if [[ -n "${master_url}" ]]; then
    cat >> "${service_file}" <<SERVICE
Environment=MASTER_URL=${master_url}
SERVICE
  fi

  cat >> "${service_file}" <<SERVICE
ExecStart=/usr/bin/dotnet run --project ${API_PROJECT}
Restart=always
RestartSec=2
StandardOutput=append:${LOG_DIR}/terminal-gateway-${role}.out.log
StandardError=append:${LOG_DIR}/terminal-gateway-${role}.err.log

[Install]
WantedBy=multi-user.target
SERVICE
}

echo "[1/5] write systemd units"
write_service "${MASTER_SERVICE_FILE}" "master" "${MASTER_PORT}" "${MASTER_NODE_ID}" "${MASTER_NODE_NAME}" "" "${MASTER_SETTINGS_STORE_FILE}" "${MASTER_PROFILE_STORE_FILE}" "${MASTER_FILES_BASE_PATH}"
write_service "${SLAVE_SERVICE_FILE}" "slave" "${SLAVE_PORT}" "${SLAVE_NODE_ID}" "${SLAVE_NODE_NAME}" "${MASTER_URL}" "${SLAVE_SETTINGS_STORE_FILE}" "${SLAVE_PROFILE_STORE_FILE}" "${SLAVE_FILES_BASE_PATH}"

echo "[2/6] build secretary-web for master"
cd "${REPO_ROOT}/apps/secretary-web"
VITE_WEBPTY_BASE="${MASTER_WEBPTY_BASE}" \
VITE_WEBPTY_HUB_PATH="${MASTER_HUB_PATH}" \
VITE_APP_BASE_PATH="${MASTER_APP_BASE_PATH}" \
npm run build

echo "[3/6] publish master dist -> ${MASTER_WEB_DIR}"
rm -rf "${MASTER_WEB_DIR:?}/"*
cp -a "${REPO_ROOT}/apps/secretary-web/dist/." "${MASTER_WEB_DIR}/"

echo "[4/6] build secretary-web for slave"
VITE_WEBPTY_BASE="${SLAVE_WEBPTY_BASE}" \
VITE_WEBPTY_HUB_PATH="${SLAVE_HUB_PATH}" \
VITE_APP_BASE_PATH="${SLAVE_APP_BASE_PATH}" \
npm run build

echo "[5/6] publish slave dist -> ${SLAVE_WEB_DIR}"
rm -rf "${SLAVE_WEB_DIR:?}/"*
cp -a "${REPO_ROOT}/apps/secretary-web/dist/." "${SLAVE_WEB_DIR}/"

echo "[6/6] reload and restart services"
systemctl daemon-reload
systemctl enable "${MASTER_SERVICE}" "${SLAVE_SERVICE}"
systemctl restart "${MASTER_SERVICE}" "${SLAVE_SERVICE}"

sleep 2
echo "--- master service status ---"
systemctl --no-pager --lines=20 status "${MASTER_SERVICE}" || true
echo "--- slave service status ---"
systemctl --no-pager --lines=20 status "${SLAVE_SERVICE}" || true

echo "cluster deploy done"
