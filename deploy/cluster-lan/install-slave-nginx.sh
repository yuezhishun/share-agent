#!/usr/bin/env bash
set -euo pipefail

SERVER_NAME="${SERVER_NAME:-slave.local}"
SITE_ROOT="${SITE_ROOT:-/www/wwwroot/pty-agent-slave-web}"
GATEWAY_PORT="${GATEWAY_PORT:-7320}"
NGINX_VHOST_DIR="${NGINX_VHOST_DIR:-/www/server/panel/vhost/nginx}"
CONF_NAME="${CONF_NAME:-pty-agent-slave.conf}"
CONF_PATH="${NGINX_VHOST_DIR}/${CONF_NAME}"
LOG_DIR="${LOG_DIR:-/www/wwwlogs}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "please run as root"
  exit 1
fi

mkdir -p "${NGINX_VHOST_DIR}" "${LOG_DIR}" "${SITE_ROOT}"

cat > "${CONF_PATH}" <<CONF
server {
    listen 80;
    server_name ${SERVER_NAME};

    root ${SITE_ROOT};
    index index.html;

    location = /healthz {
        return 200 "ok";
    }

    location ^~ /api/ {
        proxy_pass http://127.0.0.1:${GATEWAY_PORT}/api/;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location ^~ /hubs/ {
        proxy_pass http://127.0.0.1:${GATEWAY_PORT}/hubs/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
        proxy_buffering off;
    }

    location / {
        try_files \$uri \$uri/ /index.html;
    }

    access_log ${LOG_DIR}/${CONF_NAME}.access.log;
    error_log ${LOG_DIR}/${CONF_NAME}.error.log;
}
CONF

nginx -t
nginx -s reload

echo "cluster slave nginx installed -> ${CONF_PATH}"
