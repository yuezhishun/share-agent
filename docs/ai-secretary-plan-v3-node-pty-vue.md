# AI秘书系统 V3 设计（MAF Orchestrator + Node-PTY Gateway + Vue Web）

## 1. 设计目标
1. 将终端执行能力从 .NET 进程剥离，改为 `node-pty` 独立网关，解决 Linux 下 PTY 稳定性风险。
2. 保持现有秘书与编排能力不变：秘书不执行任务，仅收发与汇报；执行由 CLI Worker 完成。
3. 提供可远程访问、支持手机端、多终端窗口切换的交互式 Web 控制台（Vue）。
4. 通过清晰边界把系统拆分为：
   - `Agent Orchestrator`（.NET + Microsoft Agent Framework）
   - `Terminal Gateway`（Node.js + node-pty）
   - `Web Console`（Vue 3 + xterm.js）

## 2. 最终系统边界

### 2.1 Agent Orchestrator（现有项目主干）
职责：
1. 任务接收、结构化、状态机管理。
2. MAF 工作流（intake/classify/plan/handoff/review/replan）。
3. HITL（老板决策挂起/恢复）。
4. 评估与 PDCA。
5. 通过标准接口调用 Terminal Gateway 创建/操作会话。

不负责：
1. 直接 PTY 原生调用。
2. 终端流渲染。

### 2.2 Terminal Gateway（新项目）
职责：
1. 使用 `node-pty` 管理 PTY 生命周期（spawn/write/resize/kill）。
2. 将终端输出以流方式推送（WebSocket）。
3. 维护会话注册表（sessionId -> pty 实例）。
4. 提供 API 给 Orchestrator 和 Web Console。

不负责：
1. 任务编排。
2. 计划拆解和业务评估。

### 2.3 Web Console（Vue）
职责：
1. 任务看板、时间线、汇报面板。
2. 多终端标签页（会话切换、输入输出交互）。
3. 移动端适配（响应式布局）。
4. 与 Orchestrator + Terminal Gateway 双连接：
   - 业务状态来自 Orchestrator
   - 终端流来自 Gateway

## 3. 技术栈决策
1. Orchestrator：`ASP.NET Core` + `SignalR` + `SQLite` + `MAF`（保持现状）。
2. Gateway：`Node.js 22 LTS` + `Fastify`（或 Express）+ `ws` + `node-pty`。
3. Web：`Vue 3` + `Vite` + `Pinia` + `Vue Router` + `xterm.js` + `@xterm/addon-fit`。
4. 反向代理：`Nginx`（统一域名，按路径转发）。

默认建议：
1. 生产先 Linux 优先。
2. Gateway 与 Orchestrator 分别容器化，Web 静态资源由 Nginx 托管。

## 4. 关键接口契约（决策完成）

### 4.1 Orchestrator -> Gateway（服务间 REST）
Base URL：`http://terminal-gateway:7300`

1. `POST /internal/sessions`
- 用途：创建 PTY 会话。
- 请求：
```json
{
  "sessionId": "guid-from-orchestrator",
  "taskId": "guid",
  "cliType": "codex|claude_code|custom",
  "mode": "plan|execute",
  "shell": "/bin/bash",
  "cwd": "/workspace/tasks/...",
  "command": "...",
  "env": {
    "ANTHROPIC_MODEL": "claude-sonnet-4-5",
    "OPENAI_MODEL": "gpt-5-codex"
  },
  "cols": 160,
  "rows": 40
}
```
- 响应：
```json
{
  "sessionId": "...",
  "pid": 12345,
  "status": "running",
  "backend": "node-pty"
}
```

2. `POST /internal/sessions/{sessionId}/input`
- 请求：`{ "data": "user input\r" }`

3. `POST /internal/sessions/{sessionId}/resize`
- 请求：`{ "cols": 120, "rows": 36 }`

4. `POST /internal/sessions/{sessionId}/terminate`
- 请求：`{ "signal": "SIGTERM" }`

5. `GET /internal/sessions/{sessionId}`
- 返回会话状态、pid、启动时间、最近活动时间。

鉴权：
1. 服务间使用 `X-Internal-Token` 共享密钥。
2. 仅内网可访问 `/internal/*`。

### 4.2 Gateway 流事件（WebSocket）
Endpoint：`/ws/terminal?sessionId=...&token=...`

上行消息（客户端 -> Gateway）：
```json
{ "type": "input", "data": "ls -la\r" }
{ "type": "resize", "cols": 120, "rows": 36 }
{ "type": "ping", "ts": 1730000000 }
```

下行消息（Gateway -> 客户端）：
```json
{ "type": "ready", "sessionId": "...", "pid": 12345 }
{ "type": "output", "sessionId": "...", "stream": "stdout", "data": "..." }
{ "type": "exit", "sessionId": "...", "exitCode": 0, "signal": null }
{ "type": "error", "sessionId": "...", "code": "PTY_SPAWN_FAILED", "message": "..." }
```

说明：
1. 终端输出使用 chunk，不强制按行。
2. Orchestrator 侧如需行级事件，可在适配层增量切行。

### 4.3 Web Console -> Orchestrator（业务 API）
保持现有：
1. `POST /api/tasks`
2. `GET /api/tasks/{id}`
3. `GET /api/tasks/{id}/timeline`
4. `POST /api/tasks/{id}/decision`
5. `GET /api/reports/progress`

新增建议：
1. `GET /api/tasks/{id}/sessions`（返回该任务关联会话列表，便于前端标签渲染）
2. `POST /api/sessions/{id}/attach-token`（签发短期 ws token 给前端连 Gateway）

## 5. 代码层改造影响矩阵（当前仓库）

### 高影响
1. `apps/orchestrator/src/PtyAgent.Api/Runtime/CliSessionManager.cs`
- 从直接使用 `PtyProvider/Process` 改为调用 `ITerminalBackend`。
- 新增 `NodePtyBackendClient`（HTTP 调 Gateway）。
- 保持公开方法不变：`StartAsync/SendInputAsync/TerminateAsync`。

2. `apps/orchestrator/src/PtyAgent.Api/Infrastructure/RuntimeOptions.cs`
- 扩展配置：
  - `TerminalBackend: process|nodepty|auto`
  - `TerminalGatewayBaseUrl`
  - `TerminalGatewayToken`
  - `TerminalGatewayTimeoutMs`

### 中影响
1. `apps/orchestrator/src/PtyAgent.Api/Program.cs`
- 注册 `HttpClient<NodePtyBackendClient>`。
- 注入 `ITerminalBackend`。

2. `README.md`
- 更新运行架构和部署说明，移除 `ptynet` 默认路径描述。

### 低影响
1. 编排层：
- `DefaultOrchestrationEngine`、`MafCompatibleOrchestrationEngine` 基本不改。

2. 测试层：
- `ApiFlowTests` 保持工作流断言，增加 Gateway 模拟或测试容器。

## 6. 新增项目结构建议

```text
/
  src/
    orchestrator/src/PtyAgent.Api # .NET Orchestrator
  services/
    terminal-gateway/             # Node + node-pty
      src/
        app.ts
        routes/internal-sessions.ts
        ws/terminal-ws.ts
        pty/pty-manager.ts
      package.json
  web/
    secretary-console-vue/        # Vue 3 前端
      src/
        views/TaskBoard.vue
        views/TerminalWorkspace.vue
        components/TerminalTab.vue
        stores/task.ts
        stores/terminal.ts
```

## 7. Vue Web 交互设计（V1）

### 页面
1. `TaskBoard`
- 左侧：任务列表（状态、优先级、最新事件）。
- 右侧：任务详情+时间线+决策按钮。

2. `TerminalWorkspace`
- 顶部：会话标签（可新增/关闭/切换）。
- 中部：xterm.js 终端窗口。
- 底部：输入框与快捷动作（重连、终止、清屏）。

### 交互规则
1. 一个任务可挂多个会话（plan/executor/replan）。
2. 切换标签不销毁 PTY，只暂停前端渲染订阅。
3. 移动端采用单列布局：任务列表/终端窗口抽屉切换。

### 移动端约束
1. 最小支持宽度 375px。
2. 输入区固定底部，避免虚拟键盘遮挡。
3. 默认字体 >= 14px，按钮触控区 >= 40px。

## 8. 安全与治理
1. Gateway 仅暴露 `/ws/terminal` 与必要公开健康检查。
2. `/internal/*` 仅内网 + token 鉴权。
3. WebSocket token 使用短期 JWT（5分钟）+ 单 session 绑定。
4. 会话命令审计：记录 `sessionId/taskId/cliType/cwd/启动参数摘要`。
5. 资源限制：每用户最大并发会话、每会话最大空闲时长、最大输出速率告警。

## 9. 分阶段实施（PDCA，每阶段都测试）

### Phase A：后端抽象与兼容层
Plan：引入 `ITerminalBackend` 抽象。
Do：保留 `process` 后端，实现 `nodepty` 客户端空壳。
Check：现有 `dotnet test` 全通过；API 行为不变。
Act：修正接口差异后冻结契约。

### Phase B：Terminal Gateway（Node）
Plan：先做最小四能力：spawn/input/resize/terminate。
Do：接入 node-pty，提供内部 REST + WS。
Check：Node 侧集成测试（spawn + 回显 + exit）。
Act：补充异常码和重连语义。

### Phase C：Orchestrator 接 Gateway
Plan：替换 `CliSessionManager` 启停逻辑。
Do：`.NET` 通过 HTTP 驱动 Gateway。
Check：`ApiFlowTests` + 新增会话生命周期集成测试。
Act：修复状态同步和边界错误。

### Phase D：Vue Console
Plan：先做任务看板 + 单终端。
Do：接入 xterm.js 与 WS。
Check：E2E（任务创建、会话连接、输入输出、切换标签）。
Act：移动端适配与性能优化。

### Phase E：生产化
Plan：Nginx 统一入口、容器化、健康检查与监控。
Do：部署脚本与告警规则。
Check：长稳压测（24h、并发 20 会话）。
Act：参数调优并形成运行手册。

## 10. 测试清单（必须通过）
1. 单元测试
- Orchestrator：状态机、HITL、replan。
- Gateway：会话注册表、异常映射。

2. 集成测试
- spawn -> output -> input -> terminate 全链路。
- session crash 后状态一致性。
- ws 断线重连恢复。

3. E2E
- 复杂任务 Plan/Exec 双会话可切换。
- 老板决策触发 replan 并继续执行。
- 手机端（375x812）可完成创建任务和终端交互。

4. 验收指标
- 终端首屏输出延迟 < 1s（局域网）。
- 任务状态更新到前端 < 3s。
- 24h 压测会话异常退出率 < 1%。

## 11. 默认值与假设
1. 默认只支持单租户管理员。
2. Gateway 与 Orchestrator 同一私网。
3. 首批 CLI：`codex`、`claude code`。
4. Vue 控制台先实现中文界面。
5. 先不做秘书池，保持单秘书待命模式。
