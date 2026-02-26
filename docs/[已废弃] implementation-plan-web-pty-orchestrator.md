# [已废弃] implementation-plan-web-pty-orchestrator

> 状态：已废弃
>
> 废弃日期：2026-02-25
>
> 原因：文档中的协议、架构或实现路径与当前仓库代码差异过大。
>
> 当前实现以 apps 下源码为准：前端与 dotnet gateway 已切换到 SignalR Hub /hubs/terminal。
>
> 建议参考：README.md、docs/terminal-gateway-dotnet.md、docs/nginx-config-paths.md。

# Web + PTY Gateway + Orchestrator 实施计划

## 背景
当前系统已具备 Orchestrator、Terminal Gateway、Web Console 的基础能力，但仍缺以下关键闭环：
1. Web 远程连接服务器 PTY 的稳定重连能力。
2. Web 侧老板发布任务入口，并由 Orchestrator 接收处理并反馈状态。

本计划按两步实施，优先交付可用性与链路闭环。

## Step 1：Web 与伪终端网关（断线可重连）

### 目标
1. Web 可连接指定 `sessionId` 的远程 PTY。
2. WS 断开后自动重连同一 `sessionId`，若 PTY 仍在运行可继续交互。
3. 若 PTY 已退出，前端明确展示退出状态并停止盲重连。

### 约束与决策
1. 当前版本不做 WS 鉴权（先实现功能，后续接入 Orchestrator 签发鉴权）。
2. 重连语义采用“会话保持”，不自动新建 PTY。
3. 断线期间输出不做补偿回放，重连后继续接收新输出。

### 网关改造（apps/terminal-gateway）
1. `/ws/terminal` 仅校验 `sessionId` 参数必填，不校验 token。
2. 增加心跳消息：
1. 客户端上行：`{ "type": "ping", "ts": 1730000000 }`
2. 网关下行：`{ "type": "pong", "ts": 1730000000 }`
3. `attach(sessionId)` 行为：
1. 会话 `running`：发送 `ready`（含 `status`）并保持连接。
2. 会话 `exited`：立即发送 `exit` 并关闭连接。
4. 保持 PTY 生命周期与订阅生命周期解耦：断线只 `detach`，不终止 PTY。

### 前端改造（apps/secretary-web）
1. `terminal` store 增加会话连接状态机（`connecting/connected/reconnecting/exited/error`）。
2. 自动重连策略：指数退避（1s、2s、4s、8s、15s 上限）。
3. `TerminalTab` 增加状态提示与手动“重连”动作。
4. `TerminalWorkspace` 保持多标签切换不销毁会话状态。

### Step 1 验收标准
1. 人为断网后恢复网络，前端可在 30 秒内重连。
2. 重连后继续向同一 PTY 输入命令并得到回显。
3. PTY 退出后前端展示退出，不再持续重连。

## Step 2：Web 老板任务入口（Orchestrator 接收并处理）

### 目标
1. Web 可发布任务到 Orchestrator。
2. Web 可查看任务列表、任务时间线、阶段汇报。
3. 任务创建后可快速定位任务并观察执行状态变化。

### 范围
1. 本轮仅覆盖“创建任务 + 看状态”。
2. HITL 决策入口、自动 attach-token、会话自动签发鉴权不在本轮。

### 后端改造（apps/orchestrator）
1. 新增 `GET /api/tasks?limit=50`，返回最近任务列表（倒序）。
2. 保持现有接口：
1. `POST /api/tasks`
2. `GET /api/tasks/{taskId}/timeline`
3. `GET /api/reports/progress`
3. （可选）新增 `GET /api/tasks/{taskId}/sessions`，供前端任务到终端的会话联动。

### 前端改造（apps/secretary-web）
1. `TaskBoard` 页面补齐老板发布任务表单字段与提交反馈。
2. 增加任务列表加载（页面初始化 + 提交后刷新）。
3. 选中任务后拉取 timeline 与 report，显示关键状态。
4. 任务详情区域展示 `plannerSessionId/executorSessionId`（若有）用于人工跳转终端。

### Step 2 验收标准
1. 前端发布任务成功后可在列表中看到新任务。
2. 任务时间线可反映状态推进（如 `planning/executing/done`）。
3. 阶段报告接口可稳定返回并展示。

## 实施顺序
1. 先完成 Step 1 网关协议与前端重连状态机。
2. 完成 Step 1 联调与网关/前端测试。
3. 再完成 Step 2 后端任务列表接口与前端任务入口。
4. 最后回归全链路测试（.NET tests、Gateway tests、Web build）。

## 测试清单
1. Gateway：spawn -> output -> ws断线 -> 重连 -> input -> exit。
2. Web：连接状态流转、自动重连退避、退出后停止重连。
3. Orchestrator：任务创建、任务列表、时间线查询、报告查询。
4. E2E：Web 发任务 -> Orchestrator 执行 -> Web 看状态与日志。

## 已知风险
1. 无鉴权模式仅适用于内网受控环境，生产必须补鉴权。
2. 断线输出不回放可能造成观感缺口，后续可加 ring buffer 回放。
3. 高并发重连可能放大网关压力，需通过退避和重试上限控制。
