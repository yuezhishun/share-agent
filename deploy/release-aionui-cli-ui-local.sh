#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
AIONUI_DIR="${AIONUI_DIR:-${REPO_ROOT}/apps/AionUi}"
SITE_ROOT="${SITE_ROOT:-/www/wwwroot/pty.addai.vip}"
CLI_UI_SUBDIR="${CLI_UI_SUBDIR:-cli-ui}"
CLI_UI_DIR="${CLI_UI_DIR:-${SITE_ROOT%/}/${CLI_UI_SUBDIR}}"
CLI_UI_PATH="${CLI_UI_PATH:-/${CLI_UI_SUBDIR}}"
AIONUI_ENV_FILE="${AIONUI_ENV_FILE:-${AIONUI_DIR}/.env.local}"
AIONUI_GATEWAY_URL="${AIONUI_GATEWAY_URL:-https://pty.addai.vip}"
AIONUI_HUB_URL="${AIONUI_HUB_URL:-${AIONUI_GATEWAY_URL%/}/hubs/terminal}"
AIONUI_DEFAULT_NODE_ID="${AIONUI_DEFAULT_NODE_ID:-local}"
AIONUI_ENABLE_MCP_UI="${AIONUI_ENABLE_MCP_UI:-true}"
NODE_HEAP_MB="${NODE_HEAP_MB:-4096}"
NODE_OPTIONS="${NODE_OPTIONS:---max-old-space-size=${NODE_HEAP_MB}}"
NGINX_CONF_PATH="${NGINX_CONF_PATH:-/www/server/panel/vhost/nginx/pty.addai.vip.conf}"
CONFIGURE_NGINX="${CONFIGURE_NGINX:-1}"

if [[ ! -f "${AIONUI_DIR}/package.json" ]]; then
  echo "missing AionUi package.json under ${AIONUI_DIR}"
  exit 1
fi

echo "[1/5] write AionUi local env -> ${AIONUI_ENV_FILE}"
cat > "${AIONUI_ENV_FILE}" <<ENVFILE
VITE_GATEWAY_BASE_URL=${AIONUI_GATEWAY_URL}
VITE_GATEWAY_HUB_URL=${AIONUI_HUB_URL}
VITE_DEFAULT_NODE_ID=${AIONUI_DEFAULT_NODE_ID}
VITE_ENABLE_MCP_UI=${AIONUI_ENABLE_MCP_UI}
ENVFILE

echo "[2/5] ensure AionUi dependencies"
cd "${AIONUI_DIR}"
if [[ ! -d node_modules ]]; then
  npm install
else
  echo "node_modules already exists, skip npm install"
fi

echo "[3/5] build AionUi web renderer for /${CLI_UI_SUBDIR}"
export NODE_OPTIONS
npm run build

if [[ ! -f "${AIONUI_DIR}/out/renderer/index.html" ]]; then
  echo "AionUi build output not found under ${AIONUI_DIR}/out/renderer"
  exit 1
fi

echo "[4/5] publish renderer -> ${CLI_UI_DIR}"
mkdir -p "${CLI_UI_DIR}"
rm -rf "${CLI_UI_DIR:?}/"*
cp -a "${AIONUI_DIR}/out/renderer/." "${CLI_UI_DIR}/"

if [[ "${CONFIGURE_NGINX}" == "1" ]]; then
  echo "[5/6] ensure nginx route for ${CLI_UI_PATH}"
  if [[ "${EUID}" -ne 0 ]]; then
    echo "skip nginx update: please run as root to update ${NGINX_CONF_PATH}"
  elif [[ ! -f "${NGINX_CONF_PATH}" ]]; then
    echo "skip nginx update: config not found at ${NGINX_CONF_PATH}"
  else
    NGINX_CONF_BACKUP="${NGINX_CONF_PATH}.$(date +%Y%m%d%H%M%S).bak.cli-ui"
    cp "${NGINX_CONF_PATH}" "${NGINX_CONF_BACKUP}"

    python3 - "${NGINX_CONF_PATH}" "${CLI_UI_PATH}" "${CLI_UI_DIR}" <<'PY'
from pathlib import Path
import re
import sys

conf_path = Path(sys.argv[1])
cli_ui_path = sys.argv[2].rstrip("/")
cli_ui_dir = sys.argv[3].rstrip("/")
marker_start = "    # aionui-cli-ui-start"
marker_end = "    # aionui-cli-ui-end"

managed_block = f"""{marker_start}
    location = {cli_ui_path} {{
        return 302 {cli_ui_path}/;
    }}

    location ^~ {cli_ui_path}/ {{
        alias {cli_ui_dir}/;
        try_files $uri $uri/ {cli_ui_path}/index.html;
    }}
{marker_end}
"""

text = conf_path.read_text()
text = re.sub(rf"\n{re.escape(marker_start)}.*?{re.escape(marker_end)}\n", "\n", text, flags=re.S)

insert_anchor = "    location / {\n"
if insert_anchor not in text:
    raise SystemExit(f"failed to locate insertion anchor in {conf_path}")

text = text.replace(insert_anchor, managed_block + "\n" + insert_anchor, 1)
conf_path.write_text(text)
PY

    nginx -t
    nginx -s reload
  fi
else
  echo "[5/6] skip nginx update"
fi

echo "[6/6] deploy summary"
echo "AionUi web-only cli-ui deploy done"
echo "site root: ${SITE_ROOT}"
echo "frontend path: https://pty.addai.vip/${CLI_UI_SUBDIR}"
echo "publish dir: ${CLI_UI_DIR}"
echo "gateway base: ${AIONUI_GATEWAY_URL}"
echo "hub url: ${AIONUI_HUB_URL}"
