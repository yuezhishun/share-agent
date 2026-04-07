#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://pty.addai.vip}"
HOST="${HOST:-pty.addai.vip}"
IP="${IP:-127.0.0.1}"

echo "[master] health"
curl -k -fsS --resolve "${HOST}:443:${IP}" "${BASE_URL}/api/health" >/dev/null

echo "[master] frontend"
curl -k -fsS --resolve "${HOST}:443:${IP}" "${BASE_URL}/" >/dev/null

echo "[master] nodes"
curl -k -fsS --resolve "${HOST}:443:${IP}" "${BASE_URL}/api/nodes" >/dev/null

echo "cluster master verify passed"
