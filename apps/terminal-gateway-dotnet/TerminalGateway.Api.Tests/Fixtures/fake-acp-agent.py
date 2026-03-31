#!/usr/bin/env python3
import json
import sys


def send(obj):
    sys.stdout.write(json.dumps(obj) + "\n")
    sys.stdout.flush()


for raw in sys.stdin:
    raw = raw.strip()
    if not raw:
        continue

    msg = json.loads(raw)
    req_id = msg.get("id")
    method = msg.get("method")
    params = msg.get("params") or {}

    if "result" in msg and req_id in (9001, "perm-9001"):
        send(
            {
                "jsonrpc": "2.0",
                "method": "session/update",
                "params": {"update": {"type": "agent_message_chunk", "content": {"type": "text", "text": "permission-ok"}}},
            }
        )
        send({"jsonrpc": "2.0", "method": "session/update", "params": {"update": {"type": "end_turn"}}})
        continue

    if method == "initialize":
        send({"jsonrpc": "2.0", "id": req_id, "result": {"protocolVersion": 1}})
    elif method == "session/new":
        send(
            {
                "jsonrpc": "2.0",
                "id": req_id,
                "result": {
                    "sessionId": "fake-session",
                    "models": [{"id": "fake-model", "name": "Fake Model"}],
                    "configOptions": [{"id": "theme", "label": "Theme"}],
                },
            }
        )
    elif method == "session/load":
        send({"jsonrpc": "2.0", "id": req_id, "result": {"models": [], "configOptions": []}})
    elif method == "session/prompt":
        send({"jsonrpc": "2.0", "id": req_id, "result": {"accepted": True}})
        prompt = params.get("prompt") or []
        text = ""
        if prompt and isinstance(prompt, list):
            text = (prompt[0] or {}).get("text", "")
        if "permission" in text:
            permission_id = "perm-9001" if "string permission" in text else 9001
            send(
                {
                    "jsonrpc": "2.0",
                    "id": permission_id,
                    "method": "session/request_permission",
                    "params": {
                        "toolCall": {"toolCallId": "tool-1", "title": "Permission Required"},
                        "options": [{"id": "allow", "label": "Allow"}],
                    },
                }
            )
        else:
            send(
                {
                    "jsonrpc": "2.0",
                    "method": "session/update",
                    "params": {"update": {"type": "agent_message_chunk", "content": {"type": "text", "text": f"echo:{text}"}}},
                }
            )
            send({"jsonrpc": "2.0", "method": "session/update", "params": {"update": {"type": "end_turn"}}})
    elif method in ("session/set_mode", "session/set_model", "session/set_config_option", "session/cancel"):
        send({"jsonrpc": "2.0", "id": req_id, "result": {"ok": True}})
    else:
        send({"jsonrpc": "2.0", "id": req_id, "result": {}})
