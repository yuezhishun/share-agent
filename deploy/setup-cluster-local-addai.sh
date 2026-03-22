#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

MASTER_SERVER_NAME="${MASTER_SERVER_NAME:-pty.addai.vip}"
MASTER_SSL_CERT="${MASTER_SSL_CERT:-/home/addai.vip_nginx_ssl/pty.addai.vip_bundle.pem}"
MASTER_SSL_CERT_KEY="${MASTER_SSL_CERT_KEY:-/home/addai.vip_nginx_ssl/pty.addai.vip.key}"

MASTER_CONF_PATH="${MASTER_CONF_PATH:-/www/server/panel/vhost/nginx/pty.addai.vip.conf}"
MASTER_WEB_DIR="${MASTER_WEB_DIR:-/www/wwwroot/pty-agent-web-master}"
SLAVE_WEB_DIR="${SLAVE_WEB_DIR:-/www/wwwroot/pty-agent-web-slave}"
SLAVE_PATH_PREFIX="${SLAVE_PATH_PREFIX:-/slave}"
SLAVE_APP_BASE_PATH="${SLAVE_APP_BASE_PATH:-${SLAVE_PATH_PREFIX%/}/}"
SLAVE_WEBPTY_BASE="${SLAVE_WEBPTY_BASE:-${SLAVE_PATH_PREFIX%/}/web-pty}"
SLAVE_HUB_BASE="${SLAVE_HUB_BASE:-${SLAVE_PATH_PREFIX%/}/hubs}"

MASTER_GATEWAY_PORT="${MASTER_GATEWAY_PORT:-7310}"
SLAVE_GATEWAY_PORT="${SLAVE_GATEWAY_PORT:-7320}"

MASTER_SERVICE="${MASTER_SERVICE:-terminal-gateway-master.service}"
SLAVE_SERVICE="${SLAVE_SERVICE:-terminal-gateway-slave.service}"

MASTER_NODE_ID="${MASTER_NODE_ID:-master-local}"
MASTER_NODE_NAME="${MASTER_NODE_NAME:-Master Local}"
SLAVE_NODE_ID="${SLAVE_NODE_ID:-slave-local}"
SLAVE_NODE_NAME="${SLAVE_NODE_NAME:-Slave Local}"

CLUSTER_TOKEN="${CLUSTER_TOKEN:-dev-cluster-token}"
MASTER_FILES_BASE_PATH="${MASTER_FILES_BASE_PATH:-${FILES_BASE_PATH:-/home/yueyuan}}"
SLAVE_FILES_BASE_PATH="${SLAVE_FILES_BASE_PATH:-${FILES_BASE_PATH:-/home/yueyuan/gitlab}}"
LOG_DIR="${LOG_DIR:-/www/wwwlogs}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "please run as root"
  exit 1
fi

if [[ ! -f "${MASTER_SSL_CERT}" ]]; then
  echo "master ssl cert not found: ${MASTER_SSL_CERT}"
  exit 1
fi

if [[ ! -f "${MASTER_SSL_CERT_KEY}" ]]; then
  echo "master ssl cert key not found: ${MASTER_SSL_CERT_KEY}"
  exit 1
fi

if [[ ! -f "${MASTER_CONF_PATH}" ]]; then
  echo "master nginx conf not found: ${MASTER_CONF_PATH}"
  exit 1
fi

mkdir -p "${MASTER_WEB_DIR}" "${SLAVE_WEB_DIR}" "${LOG_DIR}"

MASTER_CONF_BACKUP="${MASTER_CONF_PATH}.$(date +%Y%m%d%H%M%S).bak.cluster"
cp "${MASTER_CONF_PATH}" "${MASTER_CONF_BACKUP}"

python3 - "${MASTER_CONF_PATH}" "${MASTER_SERVER_NAME}" "${MASTER_WEB_DIR}" "${MASTER_GATEWAY_PORT}" <<'PY'
from pathlib import Path
import re
import sys

conf_path = Path(sys.argv[1])
server_name = sys.argv[2]
root_dir = sys.argv[3]
gateway_port = sys.argv[4]
text = conf_path.read_text()
updated = text

updated = re.sub(r'(^\s*server_name\s+)[^;]+;', rf'\1{server_name};', updated, count=2, flags=re.M)
updated = re.sub(r'(^\s*root\s+)[^;]+;', rf'\1{root_dir};', updated, count=1, flags=re.M)
updated = re.sub(r'proxy_pass http://127\.0\.0\.1:\d+/api/;', f'proxy_pass http://127.0.0.1:{gateway_port}/api/;', updated)
updated = re.sub(r'proxy_pass http://127\.0\.0\.1:\d+/hubs/;', f'proxy_pass http://127.0.0.1:{gateway_port}/hubs/;', updated)

required_tokens = [
    f"server_name {server_name};",
    f"root {root_dir};",
    f"proxy_pass http://127.0.0.1:{gateway_port}/api/;",
    f"proxy_pass http://127.0.0.1:{gateway_port}/hubs/;",
]

if not all(token in updated for token in required_tokens):
    raise SystemExit("master nginx conf update check failed; required routes not found after rewrite")

conf_path.write_text(updated)
PY

python3 - "${MASTER_CONF_PATH}" "${SLAVE_APP_BASE_PATH}" "${SLAVE_WEBPTY_BASE}" "${SLAVE_HUB_BASE}" "${SLAVE_WEB_DIR}" "${SLAVE_GATEWAY_PORT}" "${LOG_DIR}" <<'PY'
from pathlib import Path
import re
import sys

conf_path = Path(sys.argv[1])
slave_app_base_path = sys.argv[2]
slave_webpty_base = sys.argv[3].rstrip("/")
slave_hub_base = sys.argv[4].rstrip("/")
slave_root = sys.argv[5]
slave_gateway_port = sys.argv[6]
log_dir = sys.argv[7]
marker_start = "    # codex-cluster-slave-start"
marker_end = "    # codex-cluster-slave-end"

if not slave_app_base_path.startswith("/"):
    raise SystemExit("slave app base path must start with /")
if not slave_app_base_path.endswith("/"):
    slave_app_base_path += "/"
slave_app_base_no_slash = slave_app_base_path.rstrip("/")

managed_block = f"""{marker_start}
    location ^~ {slave_webpty_base}/api/ {{
        proxy_pass http://127.0.0.1:{slave_gateway_port}/api/;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }}

    location ^~ {slave_hub_base}/ {{
        proxy_pass http://127.0.0.1:{slave_gateway_port}/hubs/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
        proxy_buffering off;
    }}

    location = {slave_app_base_no_slash} {{
        return 302 {slave_app_base_path};
    }}

    location ^~ {slave_app_base_path} {{
        alias {slave_root}/;
        try_files $uri $uri/ {slave_app_base_path}index.html;
    }}
{marker_end}
"""

text = conf_path.read_text()
pattern = re.compile(rf"\n{re.escape(marker_start)}.*?{re.escape(marker_end)}\n", re.S)
text = re.sub(pattern, "\n", text)

server_pattern = re.compile(r"(server\s*\{.*?listen\s+443 ssl;.*?)(\n\s*access_log\s+.*?\n\s*error_log\s+.*?\n\})", re.S)
match = server_pattern.search(text)
if not match:
    raise SystemExit("failed to locate https server block in master nginx conf")

updated = match.group(1).rstrip() + "\n\n" + managed_block + "\n" + match.group(2)
text = text[:match.start()] + updated + text[match.end():]
conf_path.write_text(text)
PY

nginx -t
nginx -s reload

MASTER_WEB_DIR="${MASTER_WEB_DIR}" \
SLAVE_WEB_DIR="${SLAVE_WEB_DIR}" \
MASTER_PORT="${MASTER_GATEWAY_PORT}" \
SLAVE_PORT="${SLAVE_GATEWAY_PORT}" \
MASTER_WEBPTY_BASE="/web-pty" \
MASTER_HUB_PATH="/hubs/terminal" \
MASTER_APP_BASE_PATH="/" \
SLAVE_WEBPTY_BASE="${SLAVE_WEBPTY_BASE}" \
SLAVE_HUB_PATH="${SLAVE_HUB_BASE}/terminal" \
SLAVE_APP_BASE_PATH="${SLAVE_APP_BASE_PATH}" \
MASTER_SERVICE="${MASTER_SERVICE}" \
SLAVE_SERVICE="${SLAVE_SERVICE}" \
MASTER_NODE_ID="${MASTER_NODE_ID}" \
MASTER_NODE_NAME="${MASTER_NODE_NAME}" \
SLAVE_NODE_ID="${SLAVE_NODE_ID}" \
SLAVE_NODE_NAME="${SLAVE_NODE_NAME}" \
CLUSTER_TOKEN="${CLUSTER_TOKEN}" \
MASTER_FILES_BASE_PATH="${MASTER_FILES_BASE_PATH}" \
SLAVE_FILES_BASE_PATH="${SLAVE_FILES_BASE_PATH}" \
"${REPO_ROOT}/deploy/release-cluster-local.sh"

echo "master nginx backup: ${MASTER_CONF_BACKUP}"
echo "master url: https://${MASTER_SERVER_NAME}"
echo "slave url:  https://${MASTER_SERVER_NAME}${SLAVE_APP_BASE_PATH}"
echo "verify master: curl -k https://${MASTER_SERVER_NAME}/web-pty/api/health"
echo "verify slave:  curl -k https://${MASTER_SERVER_NAME}${SLAVE_WEBPTY_BASE}/api/health"
