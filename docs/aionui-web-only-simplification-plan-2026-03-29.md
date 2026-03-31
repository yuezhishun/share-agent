# AionUi Web-Only Simplification to .NET Gateway

## Summary

把 `apps/AionUi` 从“自带 Electron + Node/WebServer + 登录 + 扩展/渠道/多 agent 后端”的应用，收敛为一个只保留 Web 版的纯前端壳，唯一后端改为仓库内现有的 `apps/terminal-gateway-dotnet/TerminalGateway.Api`。

本次保留范围固定为：

- CLI Agent / CLI Template 配置
- MCP 配置
- 会话列表、会话详情、终端交互
- 与 .NET gateway 的 REST + SignalR 连接

本次删除或硬禁用范围固定为：

- Electron 入口、preload、桌面桥接、打包链路
- AionUi 自带 `src/server.ts`、`src/process/webserver/*`、登录鉴权、WebUI 启停
- AionUi 自带 conversation DB / auth / extension / channel / cron / preview / document / remote agent / built-in gemini / openclaw / nanobot / codex runtime
- 所有 Telegram / Lark / DingTalk / 微信 / Slack / Discord / skills hub / assistants / display / about / system 等非核心页面与初始化链路

## Implementation Changes

### 1. Build and runtime shape

- `apps/AionUi` 改为纯前端构建，只保留 Vite renderer 构建入口。
- 删除或停用 `electron.vite.config.ts` 相关主进程产物；`package.json` 仅保留 Web 开发、Web 构建、测试脚本。
- 运行方式固定为静态前端，由 Nginx/静态站点托管；API Base 和 Hub Base 通过环境变量注入。
- 前端默认通过：
  - REST: `VITE_GATEWAY_BASE_URL`
  - SignalR Hub: `VITE_GATEWAY_HUB_URL`
- 不再保留任何 AionUi 内置 Node server fallback，也不再代理 Vite dev server 到自带 Express。

### 2. Frontend information architecture

前端路由收敛为 3 个一级能力：

- `/sessions`
  - 会话列表
  - 新建会话
  - 删除会话
  - 清理已退出会话
- `/sessions/:sessionId`
  - 终端画面
  - 输入 / resize / terminate
  - 快照 / history / replay
- `/settings/cli`
  - CLI Templates 管理
  - MCP 配置管理
  - 与 gateway 相关的最小连接配置展示

其余原有路由全部删除或重定向到 `/sessions`：

- `/login`
- `/guid`
- `/conversation/:id`
- `/settings/gemini`
- `/settings/model`
- `/settings/assistants`
- `/settings/agent`
- `/settings/skills-hub`
- `/settings/display`
- `/settings/webui`
- `/settings/system`
- `/settings/about`
- `/settings/tools`
- `/settings/ext/:tabId`

设置导航只保留一个简化版 Sider/TopNav：

- `Sessions`
- `CLI / MCP`

### 3. Data model and API mapping

前端状态全部切到 .NET `TerminalGateway.Api`，不再读写 AionUi 本地数据库。

固定映射如下：

- 会话列表：
  - `GET /sessions`
- 新建会话：
  - `POST /sessions`
- 删除会话：
  - `DELETE /sessions/{sessionId}`
- 终止会话：
  - `POST /sessions/{sessionId}/terminate`
- 清理已退出：
  - `POST /sessions/prune-exited`
- 会话快照：
  - `GET /sessions/{sessionId}/snapshot`
- 会话历史：
  - `GET /sessions/{sessionId}/history`

CLI 配置改用 .NET CLI template/process 接口：

- `GET /api/nodes/{nodeId}/cli/templates`
- `POST /api/nodes/{nodeId}/cli/templates`
- `PUT /api/nodes/{nodeId}/cli/templates/{templateId}`
- `DELETE /api/nodes/{nodeId}/cli/templates/{templateId}`
- `POST /api/nodes/{nodeId}/cli/processes`
- `GET /api/nodes/{nodeId}/cli/processes`
- `GET /api/nodes/{nodeId}/cli/processes/{processId}`
- `GET /api/nodes/{nodeId}/cli/processes/{processId}/output`
- `POST /api/nodes/{nodeId}/cli/processes/{processId}/wait`
- `POST /api/nodes/{nodeId}/cli/processes/{processId}/stop`
- `DELETE /api/nodes/{nodeId}/cli/processes/{processId}`

终端实时链路固定走 .NET Hub：

- `TerminalHub` at `/hubs/terminal`
- 前端只依赖：
  - `term.snapshot`
  - `term.raw`
  - `term.sync.complete`
  - `term.resize.ack`
  - `term.sync.required`
  - `term.owner.changed`
  - `term.exit`

MCP 处理固定为前端配置面，不再依赖 AionUi 的 agent sync/runtime bridge：

- 本期若 .NET 尚无 MCP 持久化接口，前端先使用浏览器本地存储保存 MCP server 配置，并只作为 CLI 配置 UI 展示。
- 不再尝试通过 AionUi `mcpBridge` 同步到本地 agent 进程。
- MCP 不参与本期会话运行链路的后端执行控制。

### 4. Codebase restructuring

保留：

- `src/renderer/*` 中与基础布局、通用 UI、会话终端展示直接相关的最小子集
- 与 `.NET gateway` 对接的新 API client、SignalR client、session/cli hooks

新增或重写的核心前端层：

- gateway API client
- terminal hub client
- `useSessions`
- `useSessionTerminal`
- `useCliTemplates`
- `useCliProcesses`
- `useMcpConfig`
- 精简版 router/layout

删除或停用的 AionUi 代码面：

- `src/index.ts`
- `src/preload.ts`
- `src/server.ts`
- `src/process/**`
- `src/common/adapter/*` 中 Electron / standalone bridge 适配层
- 所有依赖 `ipcBridge`、`window.electronAPI`、AionUi WebSocket bridge 的页面与 hooks

前端所有原先通过 `ipcBridge` 获取数据的地方，统一替换为：

- `fetch` / typed REST client
- `@microsoft/signalr` client 或现有等价 SignalR JS 客户端

### 5. Package and dependency cleanup

移除依赖类别：

- `electron`
- `electron-vite`
- `electron-builder`
- `@sentry/electron`
- `express`
- `ws`（若仅用于内置 webserver）
- `better-sqlite3`
- Node-side bridge/runtime 相关依赖
- 渠道插件依赖
- 内置 agent/runtime 相关依赖

保留依赖类别：

- React
- Vite
- `react-router-dom`
- `swr`
- `@arco-design/web-react`
- 终端渲染必需库
- SignalR JS client
- 与纯前端 MCP 配置表单有关的最小依赖

## Public APIs / Interfaces

前端新增固定环境变量接口：

- `VITE_GATEWAY_BASE_URL`
- `VITE_GATEWAY_HUB_URL`
- `VITE_DEFAULT_NODE_ID`
- `VITE_ENABLE_MCP_UI=true|false`

前端内部类型固定新增：

- `GatewaySessionSummary`
- `GatewaySessionDetail`
- `GatewayTerminalEvent`
- `CliTemplateRecord`
- `CliProcessRecord`
- `McpServerDraft`

前端内部废弃：

- 所有 `ipcBridge.*`
- 所有 `electronAPI.*`
- 所有 AionUi conversation/auth/extension/channel bridge contract

## Test Plan

必须覆盖：

- 路由只剩 `/sessions`、`/sessions/:sessionId`、`/settings/cli`
- 访问旧路由统一跳转到 `/sessions`
- 新建会话后能拉起 .NET session 并进入详情页
- 终端页能接收 `term.snapshot`、`term.raw`、`term.exit`
- resize 后能收到 `term.resize.ack`
- CLI templates 的增删改查
- CLI process 的启动、列表、详情、停止、删除
- MCP 配置页的本地增删改查
- 无登录态下直接进入应用可工作
- 构建产物不再包含 Electron main/preload/server bundle

建议验证顺序：

1. `vite build` 产出纯静态前端
2. 对接本地 `dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj`
3. 跑前端单测
4. 跑 1 条集成/E2E：创建 session -> 接收 terminal event -> terminate -> 删除 session

## Assumptions and Defaults

- `AionUi` 原有登录体系完全删除；`.NET gateway` 当前不引入替代登录。
- “只保留 web 版”解释为：`AionUi` 不再包含任何 Electron/Node 自带 server 运行形态。
- “server 部分只有 .NET”解释为：所有运行时后端能力唯一来源是 `TerminalGateway.Api`。
- 扩展、渠道、内置 agent、预览、文档处理、计划任务、远程 agent、本地数据库均不做兼容保留。
- MCP 本期仅保留“配置 UI”能力；若 .NET 端暂无对应接口，则前端本地存储作为临时实现，不反向恢复 AionUi 旧 runtime。
- 这是一次 in-place 收敛，不新建第二个替代应用目录；目标是直接把 `apps/AionUi` 改成轻量 Web 前端。
