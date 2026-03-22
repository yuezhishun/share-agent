#!/usr/bin/env bash
set -euo pipefail

MASTER_SERVER_NAME="${MASTER_SERVER_NAME:-pty-master.local}"
SLAVE_SERVER_NAME="${SLAVE_SERVER_NAME:-pty-slave.local}"

MASTER_ROOT="${MASTER_ROOT:-/www/wwwroot/pty-agent-web-master}"
SLAVE_ROOT="${SLAVE_ROOT:-/www/wwwroot/pty-agent-web-slave}"
MASTER_APP_BASE_PATH="${MASTER_APP_BASE_PATH:-/}"
SLAVE_APP_BASE_PATH="${SLAVE_APP_BASE_PATH:-/slave/}"
MASTER_WEBPTY_BASE="${MASTER_WEBPTY_BASE:-/web-pty/}"
SLAVE_WEBPTY_BASE="${SLAVE_WEBPTY_BASE:-/slave/web-pty/}"
MASTER_HUB_BASE="${MASTER_HUB_BASE:-/hubs/}"
SLAVE_HUB_BASE="${SLAVE_HUB_BASE:-/slave/hubs/}"

MASTER_GATEWAY_PORT="${MASTER_GATEWAY_PORT:-7310}"
SLAVE_GATEWAY_PORT="${SLAVE_GATEWAY_PORT:-7320}"

MASTER_SSL_CERT="${MASTER_SSL_CERT:-}"
MASTER_SSL_CERT_KEY="${MASTER_SSL_CERT_KEY:-}"
SLAVE_SSL_CERT="${SLAVE_SSL_CERT:-}"
SLAVE_SSL_CERT_KEY="${SLAVE_SSL_CERT_KEY:-}"

NGINX_VHOST_DIR="${NGINX_VHOST_DIR:-/www/server/panel/vhost/nginx}"
MASTER_CONF_NAME="${MASTER_CONF_NAME:-pty-agent-master.conf}"
SLAVE_CONF_NAME="${SLAVE_CONF_NAME:-pty-agent-slave.conf}"
MASTER_CONF_PATH="${NGINX_VHOST_DIR}/${MASTER_CONF_NAME}"
SLAVE_CONF_PATH="${NGINX_VHOST_DIR}/${SLAVE_CONF_NAME}"

LOG_DIR="${LOG_DIR:-/www/wwwlogs}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "please run as root (required to write nginx vhost files and reload nginx)"
  exit 1
fi

mkdir -p "${NGINX_VHOST_DIR}" "${LOG_DIR}"

normalize_prefix() {
  local prefix="${1:-/}"
  prefix="/${prefix#/}"
  if [[ "${prefix}" != "/" && "${prefix}" != */ ]]; then
    prefix="${prefix}/"
  fi
  printf '%s' "${prefix}"
}

write_http_server() {
  local server_name="$1"
  local root_dir="$2"
  local gateway_port="$3"
  local app_base_path="$4"
  local webpty_base="$5"
  local hub_base="$6"
  local access_log="$7"
  local error_log="$8"
  local app_root_block

  if [[ "${app_base_path}" == "/" ]]; then
    app_root_block=$(cat <<'BLOCK'
    location / {
        try_files $uri $uri/ /index.html;
    }
BLOCK
)
  else
    app_root_block=$(cat <<BLOCK
    location = / {
        return 302 ${app_base_path};
    }

    location ${app_base_path} {
        rewrite ^${app_base_path}(.*)$ /\$1 break;
        try_files \$uri \$uri/ /index.html;
    }
BLOCK
)
  fi

  cat <<SERVER
server {
    listen 80;
    server_name ${server_name};

    root ${root_dir};
    index index.html;

    location = /healthz {
        return 200 "ok";
    }

    location ^~ ${webpty_base}api/ {
        proxy_pass http://127.0.0.1:${gateway_port}/api/;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location ^~ /api/ {
        proxy_pass http://127.0.0.1:${gateway_port}/api/;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location ^~ ${webpty_base}hubs/ {
        proxy_pass http://127.0.0.1:${gateway_port}/hubs/;
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

    location ^~ ${hub_base} {
        proxy_pass http://127.0.0.1:${gateway_port}/hubs/;
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

    location ${webpty_base} {
        rewrite ^${webpty_base}(.*)$ /\$1 break;
        try_files \$uri \$uri/ /index.html;
    }

${app_root_block}

    location ~ /\. {
        deny all;
    }

    access_log ${access_log};
    error_log ${error_log};
}
SERVER
}

write_https_pair() {
  local server_name="$1"
  local root_dir="$2"
  local gateway_port="$3"
  local app_base_path="$4"
  local webpty_base="$5"
  local hub_base="$6"
  local cert="$7"
  local cert_key="$8"
  local access_log="$9"
  local error_log="${10}"
  local app_root_block

  if [[ "${app_base_path}" == "/" ]]; then
    app_root_block=$(cat <<'BLOCK'
    location / {
        try_files $uri $uri/ /index.html;
    }
BLOCK
)
  else
    app_root_block=$(cat <<BLOCK
    location = / {
        return 302 ${app_base_path};
    }

    location ${app_base_path} {
        rewrite ^${app_base_path}(.*)$ /\$1 break;
        try_files \$uri \$uri/ /index.html;
    }
BLOCK
)
  fi

  cat <<SERVER
server {
    listen 80;
    server_name ${server_name};
    return 301 https://\$host\$request_uri;
}

server {
    listen 443 ssl;
    http2 on;
    server_name ${server_name};

    ssl_certificate ${cert};
    ssl_certificate_key ${cert_key};
    ssl_session_timeout 10m;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;

    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Content-Type-Options nosniff always;
    add_header X-Frame-Options SAMEORIGIN always;
    add_header Referrer-Policy no-referrer-when-downgrade always;

    root ${root_dir};
    index index.html;

    location = /healthz {
        return 200 "ok";
    }

    location ^~ ${webpty_base}api/ {
        proxy_pass http://127.0.0.1:${gateway_port}/api/;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location ^~ /api/ {
        proxy_pass http://127.0.0.1:${gateway_port}/api/;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }

    location ^~ ${webpty_base}hubs/ {
        proxy_pass http://127.0.0.1:${gateway_port}/hubs/;
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

    location ^~ ${hub_base} {
        proxy_pass http://127.0.0.1:${gateway_port}/hubs/;
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

    location ${webpty_base} {
        rewrite ^${webpty_base}(.*)$ /\$1 break;
        try_files \$uri \$uri/ /index.html;
    }

${app_root_block}

    location ~ /\. {
        deny all;
    }

    access_log ${access_log};
    error_log ${error_log};
}
SERVER
}

write_conf() {
  local conf_path="$1"
  local server_name="$2"
  local root_dir="$3"
  local gateway_port="$4"
  local app_base_path="$5"
  local webpty_base="$6"
  local hub_base="$7"
  local cert="$8"
  local cert_key="$9"
  local access_log="${10}"
  local error_log="${11}"

  if [[ -n "${cert}" && -n "${cert_key}" ]]; then
    write_https_pair "${server_name}" "${root_dir}" "${gateway_port}" "${app_base_path}" "${webpty_base}" "${hub_base}" "${cert}" "${cert_key}" "${access_log}" "${error_log}" > "${conf_path}"
  else
    write_http_server "${server_name}" "${root_dir}" "${gateway_port}" "${app_base_path}" "${webpty_base}" "${hub_base}" "${access_log}" "${error_log}" > "${conf_path}"
  fi
}

echo "[1/4] write nginx vhosts"
write_conf "${MASTER_CONF_PATH}" "${MASTER_SERVER_NAME}" "${MASTER_ROOT}" "${MASTER_GATEWAY_PORT}" "$(normalize_prefix "${MASTER_APP_BASE_PATH}")" "$(normalize_prefix "${MASTER_WEBPTY_BASE}")" "$(normalize_prefix "${MASTER_HUB_BASE}")" "${MASTER_SSL_CERT}" "${MASTER_SSL_CERT_KEY}" "${LOG_DIR}/${MASTER_CONF_NAME}.access.log" "${LOG_DIR}/${MASTER_CONF_NAME}.error.log"
write_conf "${SLAVE_CONF_PATH}" "${SLAVE_SERVER_NAME}" "${SLAVE_ROOT}" "${SLAVE_GATEWAY_PORT}" "$(normalize_prefix "${SLAVE_APP_BASE_PATH}")" "$(normalize_prefix "${SLAVE_WEBPTY_BASE}")" "$(normalize_prefix "${SLAVE_HUB_BASE}")" "${SLAVE_SSL_CERT}" "${SLAVE_SSL_CERT_KEY}" "${LOG_DIR}/${SLAVE_CONF_NAME}.access.log" "${LOG_DIR}/${SLAVE_CONF_NAME}.error.log"

echo "[2/4] nginx -t"
nginx -t

echo "[3/4] reload nginx"
nginx -s reload

echo "[4/4] done"
echo "master vhost: ${MASTER_CONF_PATH}"
echo "slave vhost: ${SLAVE_CONF_PATH}"
