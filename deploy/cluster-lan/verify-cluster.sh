#!/usr/bin/env bash
set -euo pipefail

MASTER_BASE_URL="${MASTER_BASE_URL:-https://pty.addai.vip}"
MASTER_HOST="${MASTER_HOST:-pty.addai.vip}"
MASTER_IP="${MASTER_IP:-127.0.0.1}"
SLAVE_BASE_URL="${SLAVE_BASE_URL:-http://127.0.0.1:7320}"
EXPECTED_MASTER_NODE_ID="${EXPECTED_MASTER_NODE_ID:-linux-master}"
EXPECTED_SLAVE_NODE_ID="${EXPECTED_SLAVE_NODE_ID:-linux-local-slave}"

echo "[1/3] master health"
curl -k -fsS --resolve "${MASTER_HOST}:443:${MASTER_IP}" "${MASTER_BASE_URL}/api/health" >/dev/null

echo "[2/3] slave health"
curl -fsS "${SLAVE_BASE_URL}/api/health" >/dev/null

echo "[3/3] cluster nodes"
MASTER_NODES_JSON=$(curl -k -fsS --resolve "${MASTER_HOST}:443:${MASTER_IP}" "${MASTER_BASE_URL}/api/nodes")
if [[ "${MASTER_NODES_JSON}" != *"\"node_id\":\"${EXPECTED_MASTER_NODE_ID}\""* ]]; then
  echo "master nodes missing ${EXPECTED_MASTER_NODE_ID}: ${MASTER_NODES_JSON}"
  exit 1
fi
if [[ "${MASTER_NODES_JSON}" != *"\"node_id\":\"${EXPECTED_SLAVE_NODE_ID}\""* ]]; then
  echo "master nodes missing ${EXPECTED_SLAVE_NODE_ID}: ${MASTER_NODES_JSON}"
  exit 1
fi

echo "cluster verify passed"
