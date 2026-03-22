#!/usr/bin/env bash
set -euo pipefail

MASTER_BASE_URL="${MASTER_BASE_URL:-http://127.0.0.1:7310}"
SLAVE_BASE_URL="${SLAVE_BASE_URL:-http://127.0.0.1:7320}"
API_PREFIX="${API_PREFIX:-}"
EXPECTED_MASTER_NODE_ID="${EXPECTED_MASTER_NODE_ID:-master-local}"
EXPECTED_SLAVE_NODE_ID="${EXPECTED_SLAVE_NODE_ID:-slave-local}"

verify_health() {
  local name="$1"
  local base_url="$2"

  echo "[${name}] health"
  curl -fsS "${base_url}${API_PREFIX}/api/health" >/dev/null
}

verify_instance_flow() {
  local name="$1"
  local base_url="$2"

  echo "[${name}] files/list"
  local files_json
  files_json=$(curl -fsS "${base_url}${API_PREFIX}/api/files/list")
  local cwd
  cwd=$(printf '%s' "${files_json}" | sed -n 's/.*"base":"\([^"]*\)".*/\1/p' | head -n1)
  if [[ -z "${cwd}" ]]; then
    echo "[${name}] failed to resolve cwd from files/list: ${files_json}"
    exit 1
  fi

  echo "[${name}] create instance"
  local create_payload
  create_payload=$(printf '{"command":"bash","args":["-i"],"cwd":"%s","cols":80,"rows":24}' "${cwd}")
  local instance_json
  instance_json=$(curl -fsS -X POST "${base_url}${API_PREFIX}/api/instances" \
    -H 'content-type: application/json' \
    -d "${create_payload}")
  local instance_id
  instance_id=$(printf '%s' "${instance_json}" | sed -n 's/.*"instance_id":"\([^"]*\)".*/\1/p' | head -n1)
  if [[ -z "${instance_id}" ]]; then
    echo "[${name}] create instance failed: ${instance_json}"
    exit 1
  fi

  echo "[${name}] terminate instance ${instance_id}"
  curl -fsS -X DELETE "${base_url}${API_PREFIX}/api/instances/${instance_id}" >/dev/null
}

echo "[1/5] master/slave health"
verify_health "master" "${MASTER_BASE_URL}"
verify_health "slave" "${SLAVE_BASE_URL}"

echo "[2/5] master instance flow"
verify_instance_flow "master" "${MASTER_BASE_URL}"

echo "[3/5] slave instance flow"
verify_instance_flow "slave" "${SLAVE_BASE_URL}"

echo "[4/5] master nodes contains slave"
MASTER_NODES_JSON=$(curl -fsS "${MASTER_BASE_URL}${API_PREFIX}/api/nodes")
if [[ "${MASTER_NODES_JSON}" != *"\"node_id\":\"${EXPECTED_MASTER_NODE_ID}\""* ]]; then
  echo "master nodes missing expected master node id ${EXPECTED_MASTER_NODE_ID}: ${MASTER_NODES_JSON}"
  exit 1
fi
if [[ "${MASTER_NODES_JSON}" != *"\"node_id\":\"${EXPECTED_SLAVE_NODE_ID}\""* ]]; then
  echo "master nodes missing expected slave node id ${EXPECTED_SLAVE_NODE_ID}: ${MASTER_NODES_JSON}"
  exit 1
fi

echo "[5/5] slave nodes contains master"
SLAVE_NODES_JSON=$(curl -fsS "${SLAVE_BASE_URL}${API_PREFIX}/api/nodes")
if [[ "${SLAVE_NODES_JSON}" != *"\"node_id\":\"${EXPECTED_MASTER_NODE_ID}\""* ]]; then
  echo "slave nodes missing expected master node id ${EXPECTED_MASTER_NODE_ID}: ${SLAVE_NODES_JSON}"
  exit 1
fi

echo "cluster verify passed"
