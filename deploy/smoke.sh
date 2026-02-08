#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:8080}"

echo "[1] health check"
curl -fsS "${BASE_URL}/healthz" >/dev/null

echo "[2] create task"
TASK_JSON=$(curl -fsS -X POST "${BASE_URL}/api/tasks" \
  -H 'content-type: application/json' \
  -d '{"title":"smoke task","intent":"run smoke flow","isComplex":false,"cliType":"codex"}')

TASK_ID=$(printf '%s' "$TASK_JSON" | sed -n 's/.*"taskId":"\([^"]*\)".*/\1/p' | head -n1)
if [[ -z "$TASK_ID" ]]; then
  echo "task id parse failed"
  exit 1
fi

echo "task id: $TASK_ID"
echo "[3] fetch timeline"
curl -fsS "${BASE_URL}/api/tasks/${TASK_ID}/timeline" >/dev/null

echo "smoke passed"
