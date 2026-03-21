# TODO

## First implementation targets

- create backend project shell under `backend/`
- define persistence format for node-local recipes
- define persistence format for recent runs
- implement `POST /api/v3/runs` first
- implement `GET /api/v3/runs/{runId}` next
- build a minimal recipes + runs web flow under `web/`

## Guardrails

- no edits to `apps/secretary-web` for new product logic
- no edits to `apps/terminal-gateway-dotnet` except future compatibility adapters if absolutely required
- no new terminal-first feature path

