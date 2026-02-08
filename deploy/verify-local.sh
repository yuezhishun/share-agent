#!/usr/bin/env bash
set -euo pipefail

HOST="${HOST:-pyt.addai.vip}"
IP="${IP:-127.0.0.1}"
BASE="https://${HOST}"

echo "[1/4] gateway health"
curl -k -fsS --resolve "${HOST}:443:${IP}" "${BASE}/gateway-healthz"
echo

echo "[2/4] frontend bundle"
curl -k -fsS --resolve "${HOST}:443:${IP}" "${BASE}/" | grep -Eo 'index-[A-Za-z0-9_-]+\.js' | head -n1

echo "[3/4] create task"
TASK_JSON=$(curl -k -fsS --resolve "${HOST}:443:${IP}" -X POST "${BASE}/api/tasks" \
  -H 'content-type: application/json' \
  -d '{"title":"deploy verify task","intent":"verify deploy","isComplex":false,"cliType":"codex"}')
TASK_ID=$(printf '%s' "${TASK_JSON}" | sed -n 's/.*"taskId":"\([^"]*\)".*/\1/p' | head -n1)
if [[ -z "${TASK_ID}" ]]; then
  echo "create task failed: ${TASK_JSON}"
  exit 1
fi
echo "task id: ${TASK_ID}"

echo "[4/4] timeline"
curl -k -fsS --resolve "${HOST}:443:${IP}" "${BASE}/api/tasks/${TASK_ID}/timeline" >/dev/null

echo "verify passed"
