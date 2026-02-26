#!/usr/bin/env bash
set -euo pipefail

HOST="${HOST:-pyt.addai.vip}"
IP="${IP:-127.0.0.1}"
BASE="https://${HOST}"

echo "[1/4] gateway health"
curl -k -fsS --resolve "${HOST}:443:${IP}" "${BASE}/web-pty/api/health"
echo

echo "[2/4] frontend bundle"
curl -k -fsS --resolve "${HOST}:443:${IP}" "${BASE}/" | grep -Eo 'index-[A-Za-z0-9_-]+\.js' | head -n1

echo "[3/4] create instance"
INSTANCE_JSON=$(curl -k -fsS --resolve "${HOST}:443:${IP}" -X POST "${BASE}/web-pty/api/instances" \
  -H 'content-type: application/json' \
  -d '{"command":"bash","args":["-i"],"cwd":"/home/yueyuan","cols":80,"rows":24}')
INSTANCE_ID=$(printf '%s' "${INSTANCE_JSON}" | sed -n 's/.*"instance_id":"\([^"]*\)".*/\1/p' | head -n1)
if [[ -z "${INSTANCE_ID}" ]]; then
  echo "create instance failed: ${INSTANCE_JSON}"
  exit 1
fi
echo "instance id: ${INSTANCE_ID}"

echo "[4/4] list and terminate"
curl -k -fsS --resolve "${HOST}:443:${IP}" "${BASE}/web-pty/api/instances" >/dev/null
curl -k -fsS --resolve "${HOST}:443:${IP}" -X DELETE "${BASE}/web-pty/api/instances/${INSTANCE_ID}" >/dev/null

echo "verify passed"
