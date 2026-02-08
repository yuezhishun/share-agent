# Repository Guidelines

## Project Structure & Module Organization
This repository is a multi-app monorepo:
- `apps/orchestrator/src/PtyAgent.Api`: .NET API and orchestration runtime.
- `apps/orchestrator/tests/PtyAgent.Api.Tests`: xUnit integration/unit tests.
- `apps/terminal-gateway`: Fastify + `node-pty` terminal gateway (`src/`, `test/`).
- `apps/secretary-web`: Vue 3 + Vite console (`src/components`, `src/views`, `src/stores`).
- `deploy/`: Docker Compose, Nginx config, and `smoke.sh` verification script.
- `docs/`: architecture and implementation plans.

## Build, Test, and Development Commands
- `dotnet run --project apps/orchestrator/src/PtyAgent.Api/PtyAgent.Api.csproj`: run orchestrator locally.
- `dotnet test apps/orchestrator/src/PtyAgent.slnx -v minimal`: run all .NET tests.
- `cd apps/terminal-gateway && npm install && npm start`: run terminal gateway.
- `cd apps/terminal-gateway && npm test`: run Node test suite (`node --test`).
- `cd apps/secretary-web && npm install && npm run dev`: run web console in dev mode.
- `cd apps/secretary-web && npm run build`: production build for Vue app.
- `cd deploy && docker compose up --build`: start full stack.
- `cd deploy && ./smoke.sh`: basic post-deploy health/task flow check.

## Coding Style & Naming Conventions
- Follow existing formatting per language: C# uses 4-space indentation; JS/Vue uses 2 spaces.
- Use `PascalCase` for C# types/methods, `camelCase` for JS variables/functions, and kebab-case file names in JS where already used (for example `pty-manager.js`).
- Keep API/event naming consistent with current patterns (for example `task_done`, `hitl_waiting`).
- Prefer small, focused modules; colocate tests with the owning app.

## Testing Guidelines
- .NET tests use xUnit with `[Fact]` and method names like `Feature_ShouldExpectedBehavior`.
- Gateway tests use `node:test` with descriptive sentence-style names.
- Add or update tests for behavior changes in the same app you modify.
- Run targeted tests before PR, then run full .NET and gateway suites.

## Commit & Pull Request Guidelines
- Current history uses concise imperative commit subjects (for example `Initial commit`). Continue with short, action-first messages.
- PRs should include: purpose, scope, affected apps (`orchestrator`, `terminal-gateway`, `secretary-web`), test evidence, and config changes.
- Link related issues/tasks and include UI screenshots for web-console changes.

## Security & Configuration Tips
- Review `apps/orchestrator/src/PtyAgent.Api/appsettings.json` for runtime settings (`Runtime:TerminalBackend`, gateway URL/token/timeouts).
- Nginx main config for this project is at `/www/server/nginx/conf/nginx.conf`.
- Do not commit secrets or environment-specific tokens. Prefer environment variables and local overrides.
