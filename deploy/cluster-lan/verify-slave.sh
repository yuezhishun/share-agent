#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:7320}"

echo "[slave] health"
curl -fsS "${BASE_URL}/api/health" >/dev/null

echo "[slave] files/list"
FILES_JSON=$(curl -fsS "${BASE_URL}/api/files/list")
CWD=$(printf '%s' "${FILES_JSON}" | sed -n 's/.*"base":"\([^"]*\)".*/\1/p' | head -n1)
if [[ -z "${CWD}" ]]; then
  echo "failed to resolve cwd from files/list: ${FILES_JSON}"
  exit 1
fi

echo "[slave] create instance"
CREATE_PAYLOAD=$(printf '{"command":"bash","args":["-i"],"cwd":"%s","cols":80,"rows":24}' "${CWD}")
INSTANCE_JSON=$(curl -fsS -X POST "${BASE_URL}/api/instances" \
  -H 'content-type: application/json' \
  -d "${CREATE_PAYLOAD}")
INSTANCE_ID=$(printf '%s' "${INSTANCE_JSON}" | sed -n 's/.*"instance_id":"\([^"]*\)".*/\1/p' | head -n1)
if [[ -z "${INSTANCE_ID}" ]]; then
  echo "create instance failed: ${INSTANCE_JSON}"
  exit 1
fi

echo "[slave] terminate instance ${INSTANCE_ID}"
curl -fsS -X DELETE "${BASE_URL}/api/instances/${INSTANCE_ID}" >/dev/null

echo "cluster slave verify passed"
