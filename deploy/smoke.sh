#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:8080}"

echo "[1] health check"
curl -fsS "${BASE_URL}/healthz" >/dev/null

echo "[2] create terminal instance"
INSTANCE_JSON=$(curl -fsS -X POST "${BASE_URL}/web-pty/api/instances" \
  -H 'content-type: application/json' \
  -d '{"command":"bash","args":["-i"],"cwd":"/home/yueyuan","cols":80,"rows":24}')

INSTANCE_ID=$(printf '%s' "$INSTANCE_JSON" | sed -n 's/.*"instance_id":"\([^"]*\)".*/\1/p' | head -n1)
if [[ -z "$INSTANCE_ID" ]]; then
  echo "instance id parse failed"
  exit 1
fi

echo "instance id: $INSTANCE_ID"
echo "[3] check instances"
curl -fsS "${BASE_URL}/web-pty/api/instances" >/dev/null

echo "[4] terminate instance"
curl -fsS -X DELETE "${BASE_URL}/web-pty/api/instances/${INSTANCE_ID}" >/dev/null

echo "smoke passed"
