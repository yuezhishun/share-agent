#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MASTER_WEB_DIR="${MASTER_WEB_DIR:-/www/wwwroot/pty-agent-web-master}"
SLAVE_WEB_DIR="${SLAVE_WEB_DIR:-/www/wwwroot/pty-agent-web-slave}"
MASTER_WEBPTY_BASE="${MASTER_WEBPTY_BASE:-/web-pty}"
MASTER_HUB_PATH="${MASTER_HUB_PATH:-/hubs/terminal}"
MASTER_APP_BASE_PATH="${MASTER_APP_BASE_PATH:-/}"
SLAVE_WEBPTY_BASE="${SLAVE_WEBPTY_BASE:-/slave/web-pty}"
SLAVE_HUB_PATH="${SLAVE_HUB_PATH:-/slave/hubs/terminal}"
SLAVE_APP_BASE_PATH="${SLAVE_APP_BASE_PATH:-/slave/}"

echo "[1/4] build secretary-web for master"
cd "${REPO_ROOT}/apps/secretary-web"
VITE_WEBPTY_BASE="${MASTER_WEBPTY_BASE}" \
VITE_WEBPTY_HUB_PATH="${MASTER_HUB_PATH}" \
VITE_APP_BASE_PATH="${MASTER_APP_BASE_PATH}" \
npm run build

echo "[2/4] publish master dist -> ${MASTER_WEB_DIR}"
mkdir -p "${MASTER_WEB_DIR}"
rm -rf "${MASTER_WEB_DIR:?}/"*
cp -a "${REPO_ROOT}/apps/secretary-web/dist/." "${MASTER_WEB_DIR}/"

echo "[3/4] build secretary-web for slave"
VITE_WEBPTY_BASE="${SLAVE_WEBPTY_BASE}" \
VITE_WEBPTY_HUB_PATH="${SLAVE_HUB_PATH}" \
VITE_APP_BASE_PATH="${SLAVE_APP_BASE_PATH}" \
npm run build

echo "[4/4] publish slave dist -> ${SLAVE_WEB_DIR}"
mkdir -p "${SLAVE_WEB_DIR}"
rm -rf "${SLAVE_WEB_DIR:?}/"*
cp -a "${REPO_ROOT}/apps/secretary-web/dist/." "${SLAVE_WEB_DIR}/"

echo "cluster frontend deploy done"
