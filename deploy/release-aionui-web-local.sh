#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
AIONUI_DIR="${AIONUI_DIR:-${REPO_ROOT}/apps/AionUi}"
AIONUI_WEB_DIR="${AIONUI_WEB_DIR:-/www/wwwroot/aionui-web}"
AIONUI_ENV_FILE="${AIONUI_ENV_FILE:-${AIONUI_DIR}/.env.local}"
AIONUI_GATEWAY_URL="${AIONUI_GATEWAY_URL:-http://127.0.0.1:7300}"
AIONUI_GATEWAY_TOKEN="${AIONUI_GATEWAY_TOKEN:-dev-terminal-token}"

if [[ ! -f "${AIONUI_DIR}/package.json" ]]; then
  echo "missing AionUi package.json under ${AIONUI_DIR}"
  exit 1
fi

echo "[1/4] write AionUi local env -> ${AIONUI_ENV_FILE}"
cat > "${AIONUI_ENV_FILE}" <<ENVFILE
AGENT_GATEWAY_ENABLED=1
AIONUI_AGENT_GATEWAY_URL=${AIONUI_GATEWAY_URL}
AIONUI_AGENT_GATEWAY_TOKEN=${AIONUI_GATEWAY_TOKEN}
ENVFILE

echo "[2/4] install AionUi dependencies"
cd "${AIONUI_DIR}"
npm install

echo "[3/4] build AionUi web renderer"
npm run build:renderer:web

echo "[4/4] publish renderer dist -> ${AIONUI_WEB_DIR}"
mkdir -p "${AIONUI_WEB_DIR}"
if [[ -d "${AIONUI_DIR}/dist/renderer" ]]; then
  cp -a "${AIONUI_DIR}/dist/renderer/." "${AIONUI_WEB_DIR}/"
elif [[ -d "${AIONUI_DIR}/dist" ]]; then
  cp -a "${AIONUI_DIR}/dist/." "${AIONUI_WEB_DIR}/"
else
  echo "AionUi build output not found under ${AIONUI_DIR}/dist"
  exit 1
fi

echo "AionUi web deploy done"
