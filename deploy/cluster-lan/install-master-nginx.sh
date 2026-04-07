#!/usr/bin/env bash
set -euo pipefail

SERVER_NAME="${SERVER_NAME:-pty.addai.vip}"
SITE_ROOT="${SITE_ROOT:-/www/wwwroot/pty-agent-web}"
GATEWAY_PORT="${GATEWAY_PORT:-7310}"
NGINX_VHOST_DIR="${NGINX_VHOST_DIR:-/www/server/panel/vhost/nginx}"
CONF_NAME="${CONF_NAME:-pty.addai.vip.conf}"
CONF_PATH="${NGINX_VHOST_DIR}/${CONF_NAME}"
LOG_DIR="${LOG_DIR:-/www/wwwlogs}"
SSL_CERT="${SSL_CERT:-}"
SSL_CERT_KEY="${SSL_CERT_KEY:-}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "please run as root"
  exit 1
fi

mkdir -p "${NGINX_VHOST_DIR}" "${LOG_DIR}" "${SITE_ROOT}"

if [[ -n "${SSL_CERT}" && -n "${SSL_CERT_KEY}" ]]; then
  cat > "${CONF_PATH}" <<CONF
server {
    listen 80;
    server_name ${SERVER_NAME};
    return 301 https://\$host\$request_uri;
}

server {
    listen 443 ssl;
    http2 on;
    server_name ${SERVER_NAME};

    ssl_certificate ${SSL_CERT};
    ssl_certificate_key ${SSL_CERT_KEY};

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
else
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
fi

nginx -t
nginx -s reload

echo "cluster master nginx installed -> ${CONF_PATH}"
