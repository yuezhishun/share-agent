#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:8080}"
API_PREFIX="${API_PREFIX:-}"

echo "[1] health check"
if ! curl -fsS "${BASE_URL}/healthz" >/dev/null 2>&1; then
  curl -fsS "${BASE_URL}${API_PREFIX}/api/health" >/dev/null
fi

FILES_JSON=$(curl -fsS "${BASE_URL}${API_PREFIX}/api/files/list")
CWD=$(printf '%s' "${FILES_JSON}" | sed -n 's/.*"base":"\([^"]*\)".*/\1/p' | head -n1)
if [[ -z "${CWD}" ]]; then
  echo "failed to resolve cwd from files/list"
  exit 1
fi
echo "cwd: ${CWD}"

echo "[2] create terminal instance"
CREATE_PAYLOAD=$(printf '{"command":"bash","args":["-i"],"cwd":"%s","cols":80,"rows":24}' "${CWD}")
INSTANCE_JSON=$(curl -fsS -X POST "${BASE_URL}${API_PREFIX}/api/instances" \
  -H 'content-type: application/json' \
  -d "${CREATE_PAYLOAD}")

INSTANCE_ID=$(printf '%s' "${INSTANCE_JSON}" | sed -n 's/.*"instance_id":"\([^"]*\)".*/\1/p' | head -n1)
if [[ -z "${INSTANCE_ID}" ]]; then
  echo "instance id parse failed"
  exit 1
fi

echo "instance id: ${INSTANCE_ID}"
echo "[3] check instances"
curl -fsS "${BASE_URL}${API_PREFIX}/api/instances" >/dev/null

echo "[4] terminate instance"
curl -fsS -X DELETE "${BASE_URL}${API_PREFIX}/api/instances/${INSTANCE_ID}" >/dev/null

echo "smoke passed"
