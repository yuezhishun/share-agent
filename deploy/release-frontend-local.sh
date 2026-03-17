#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB_DIR="${WEB_DIR:-/www/wwwroot/pty-agent-web}"

echo "[1/2] build secretary-web"
cd "${REPO_ROOT}/apps/secretary-web"
npm run build

echo "[2/2] publish dist -> ${WEB_DIR}"
mkdir -p "${WEB_DIR}"
cp -a "${REPO_ROOT}/apps/secretary-web/dist/." "${WEB_DIR}/"

echo "frontend deploy done"
