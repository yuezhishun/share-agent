# pty-agent

AI秘书式多CLI编排系统，当前采用多应用架构：
1. `Agent Orchestrator`：`.NET + Microsoft Agent Framework`（任务编排/HITL/评估）
2. `Terminal Gateway (Node)`：`Node.js + node-pty`（现网默认交互式终端会话）
3. `Terminal Gateway (Dotnet)`：`.NET Minimal API + WebSocket`（新实现，对等联调中）
4. `Web Console`：`Vue 3 + xterm.js`（任务看板+多终端标签）

## 目录

- `apps/orchestrator/src/PtyAgent.Api`：Orchestrator API
- `apps/terminal-gateway`：Node PTY 网关
- `apps/terminal-gateway-dotnet/TerminalGateway.Api`：Dotnet PTY 网关
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`：Dotnet 网关测试
- `apps/secretary-web`：Vue 前端
- `docs/ai-secretary-plan-v3-node-pty-vue.md`：V3 设计文档
- `docs/terminal-gateway-dotnet.md`：Dotnet 网关运行与测试说明
- `deploy/`：Docker Compose + Nginx + smoke 脚本

## 本地开发

### 1) Orchestrator

```bash
dotnet run --project apps/orchestrator/src/PtyAgent.Api/PtyAgent.Api.csproj
```

### 2) Terminal Gateway

```bash
cd apps/terminal-gateway
npm install
npm start
```

### 3) Terminal Gateway Dotnet

```bash
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

### 4) Vue Console

```bash
cd apps/secretary-web
npm install
npm run dev
```

## 测试

### .NET

```bash
dotnet test apps/orchestrator/src/PtyAgent.slnx -v minimal
```

### Terminal Gateway

```bash
cd apps/terminal-gateway
npm test
```

### Terminal Gateway Dotnet

```bash
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/TerminalGateway.Api.Tests.csproj -v minimal
```

### Vue Build

```bash
cd apps/secretary-web
npm run build
```

## 关键配置

`apps/orchestrator/src/PtyAgent.Api/appsettings.json`

- `Runtime:TerminalBackend`: `auto | nodepty | process`
- `Runtime:TerminalGatewayBaseUrl`
- `Runtime:TerminalGatewayToken`
- `Runtime:TerminalGatewayTimeoutMs`

默认行为：
- Linux/macOS 下 `auto` 优先 `nodepty`，失败回退 `process`
- Windows 下 `auto` 优先 `nodepty`，失败回退 `process`

说明：
- 当前 orchestrator 默认仍使用 Node 网关（`apps/terminal-gateway`）。
- Dotnet 网关（`apps/terminal-gateway-dotnet`）用于对等联调与逐步切换验证。

## Nginx 配置位置

当前项目涉及 3 个关键 Nginx 配置文件：
- 主配置（线上实际入口）：`/www/server/nginx/conf/nginx.conf`
- 站点配置（线上实际生效）：`/www/server/panel/vhost/nginx/pty-agent-web.conf`、`/www/server/panel/vhost/nginx/pyt.addai.vip.conf`
- 仓库部署参考配置：`deploy/nginx.conf`

详细说明见：`docs/nginx-config-paths.md`

## 一键部署（开发环境）

```bash
cd deploy
docker compose up --build
```

访问：
- 网关入口：`http://localhost:8080`
- 健康检查：`http://localhost:8080/healthz`

部署后可执行 smoke：

```bash
cd deploy
./smoke.sh
```

## 已知问题（暂不修复）

1. `apps/terminal-gateway/src/server.js` 的 `/ws/terminal` WebSocket 连接当前尚未强制校验 `TERMINAL_WS_TOKEN`，存在未鉴权附着会话风险。
2. `apps/secretary-web/src/views/TerminalWorkspace.vue` 打开 `/terminal?taskId=...` 时当前未按 `taskId` 过滤会话，可能自动选中非目标任务的终端。
