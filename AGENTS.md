# Repository Guidelines

## Project Structure & Module Organization

This repository is a multi-app monorepo:

- `apps/terminal-gateway-dotnet/TerminalGateway.Api`: .NET terminal gateway API and SignalR hubs.
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`: xUnit tests for gateway API, sync, ownership, and process behavior.
- `apps/secretary-web`: Vue 3 + Vite console (`src/components`, `src/views`, `src/stores`, `e2e`).
- `deploy/`: Docker Compose, release scripts, verify scripts, and Nginx examples.
- `docs/`: runtime, deployment, process, and Nginx documentation.

The removed `apps/recipe-runner-next` app is no longer part of the active repository layout and should not be referenced in new docs or changes.

## Build, Test, and Development Commands

- `dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj`: run dotnet terminal gateway.
- `dotnet test apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/TerminalGateway.Api.Tests.csproj -v minimal`: run dotnet gateway tests.
- `dotnet test apps/terminal-gateway-dotnet/TerminalGateway.sln -v minimal`: run the full .NET solution test suite.
- `cd apps/secretary-web && npm install && npm run dev`: run web console in dev mode.
- `cd apps/secretary-web && npm run build`: production build for Vue app.
- `cd apps/secretary-web && npm run test:e2e:install`: install Playwright Chromium locally.
- `cd apps/secretary-web && npm run test:e2e`: run the default E2E suite.
- `cd apps/secretary-web && npm run test:e2e:integration`: run integration-focused Playwright coverage.
- `cd apps/secretary-web && npm run test:e2e:cluster`: run cluster-focused Playwright coverage.
- `cd deploy && docker compose up --build`: start full stack.
- `cd deploy && ./smoke.sh`: basic post-deploy health/task flow check.
- `cd deploy && ./verify-local.sh`: verify a local single-node deployment.
- `cd deploy && ./verify-cluster-local.sh`: verify a local master/slave deployment.

## Coding Style & Naming Conventions

- Follow existing formatting per language: C# uses 4-space indentation; JS/Vue uses 2 spaces.
- Use `PascalCase` for C# types/methods, `camelCase` for JS variables/functions, and kebab-case file names in JS where already used (for example `pty-manager.js`).
- Keep API/event naming consistent with current terminal protocol patterns (for example `term.snapshot`, `term.patch`, `term.exit`).
- Prefer small, focused modules; colocate tests with the owning app.

## Testing Guidelines

- .NET tests use xUnit with `[Fact]` and method names like `Feature_ShouldExpectedBehavior`.
- Frontend E2E tests live under `apps/secretary-web/e2e` and use Playwright configs in the app root.
- Add or update tests for behavior changes in the same app you modify.
- Run targeted tests first, then the broader suite that matches the area you touched.

## Commit & Pull Request Guidelines

- Current history uses concise imperative commit subjects (for example `Initial commit`). Continue with short, action-first messages.
- PRs should include: purpose, scope, affected apps (`terminal-gateway-dotnet`, `secretary-web`), test evidence, and config changes.
- Link related issues/tasks and include UI screenshots for web-console changes.

## Security & Configuration Tips

- Review `apps/terminal-gateway-dotnet/TerminalGateway.Api/appsettings.json` and env vars for runtime settings.
- Nginx main config for this project is at `/www/server/panel/vhost/nginx/*.conf`.
- Do not commit secrets or environment-specific tokens. Prefer environment variables and local overrides.
