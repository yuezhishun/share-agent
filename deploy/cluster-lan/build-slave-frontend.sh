#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WEB_DIR="${WEB_DIR:-/www/wwwroot/pty-agent-slave-web}"

echo "[1/2] build secretary-web for slave"
cd "${REPO_ROOT}/apps/secretary-web"
VITE_WEBPTY_BASE="" \
VITE_WEBPTY_HUB_PATH="/hubs/terminal" \
VITE_APP_BASE_PATH="/" \
npm run build

echo "[2/2] publish dist -> ${WEB_DIR}"
mkdir -p "${WEB_DIR}"
rm -rf "${WEB_DIR:?}/"*
cp -a "${REPO_ROOT}/apps/secretary-web/dist/." "${WEB_DIR}/"

echo "cluster-lan slave frontend build done"
