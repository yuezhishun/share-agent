# Repository Guidelines

## Project Structure & Module Organization
This repository is a multi-app monorepo:
- `apps/terminal-gateway`: Fastify + `node-pty` terminal gateway (`src/`, `test/`).
- `apps/terminal-gateway-dotnet/TerminalGateway.Api`: .NET terminal gateway API.
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`: xUnit tests for dotnet terminal gateway.
- `apps/secretary-web`: Vue 3 + Vite console (`src/components`, `src/views`, `src/stores`).
- `deploy/`: Docker Compose, Nginx config, and `smoke.sh` verification script.
- `docs/`: architecture and implementation plans.

## Build, Test, and Development Commands
- `cd apps/terminal-gateway && npm install && npm start`: run terminal gateway.
- `cd apps/terminal-gateway && npm test`: run Node test suite (`node --test`).
- `dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj`: run dotnet terminal gateway.
- `dotnet test apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/TerminalGateway.Api.Tests.csproj -v minimal`: run dotnet gateway tests.
- `cd apps/secretary-web && npm install && npm run dev`: run web console in dev mode.
- `cd apps/secretary-web && npm run build`: production build for Vue app.
- `cd deploy && docker compose up --build`: start full stack.
- `cd deploy && ./smoke.sh`: basic post-deploy health/task flow check.

## Coding Style & Naming Conventions
- Follow existing formatting per language: C# uses 4-space indentation; JS/Vue uses 2 spaces.
- Use `PascalCase` for C# types/methods, `camelCase` for JS variables/functions, and kebab-case file names in JS where already used (for example `pty-manager.js`).
- Keep API/event naming consistent with current terminal protocol patterns (for example `term.snapshot`, `term.patch`, `term.exit`).
- Prefer small, focused modules; colocate tests with the owning app.

## Testing Guidelines
- .NET tests use xUnit with `[Fact]` and method names like `Feature_ShouldExpectedBehavior`.
- Gateway tests use `node:test` with descriptive sentence-style names.
- Add or update tests for behavior changes in the same app you modify.
- Run targeted tests before PR, then run full .NET and gateway suites.

## Commit & Pull Request Guidelines
- Current history uses concise imperative commit subjects (for example `Initial commit`). Continue with short, action-first messages.
- PRs should include: purpose, scope, affected apps (`terminal-gateway`, `terminal-gateway-dotnet`, `secretary-web`), test evidence, and config changes.
- Link related issues/tasks and include UI screenshots for web-console changes.

## Security & Configuration Tips
- Review `apps/terminal-gateway-dotnet/TerminalGateway.Api/appsettings.json` and env vars for runtime settings.
- Nginx main config for this project is at `/www/server/nginx/conf/nginx.conf`.
- Do not commit secrets or environment-specific tokens. Prefer environment variables and local overrides.
