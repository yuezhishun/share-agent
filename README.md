# pty-agent

交互式伪终端 MVP：
1. Terminal Gateway (Dotnet)：.NET + Porta.Pty + SignalR Hub
2. Web Console：Vue 3 + xterm.js

## 目录
- `apps/terminal-gateway-dotnet/TerminalGateway.Api`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`
- `apps/secretary-web`
- `deploy`
- `docs`

## 本地开发

### 1) 运行 Dotnet Terminal Gateway
```bash
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

### 1.1) 使用解决方案文件
```bash
dotnet sln apps/terminal-gateway-dotnet/TerminalGateway.sln list
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.sln -v minimal
```

### 2) 运行 Web Console
```bash
cd apps/secretary-web
npm install
npm run dev
```

默认前端通过 `/web-pty/api/*` 与 `/web-pty/hubs/terminal` 访问网关。

## 测试

### Dotnet Gateway
```bash
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.sln -v minimal
```

### Web Console
```bash
cd apps/secretary-web
npm run build
npm run test:e2e
```

## 部署
- `deploy/docker-compose.yml`：MVP 仅包含 `terminal-gateway-dotnet + secretary-web + nginx`
- `deploy/nginx.conf`：反向代理 `/web-pty/api/` 与 `/web-pty/hubs/`
- `deploy/smoke.sh`：实例创建/列表/终止冒烟

## 清理构建产物
```bash
./clean.sh
./clean.sh --dry-run
```

默认会从仓库根目录扫描并删除 `bin`、`obj`、`node_modules`、`dist`、`coverage`、`TestResults`、`test-results`、`playwright-report`、`.vite`、`.turbo` 等构建产物目录。

## 文档
- 当前有效：`docs/terminal-gateway-dotnet.md`、`docs/nginx-config-paths.md`
- 标记 `[已废弃]` 的文档仅用于历史追溯
