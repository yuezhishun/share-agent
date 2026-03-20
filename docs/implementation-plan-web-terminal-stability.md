# Web 终端发布级稳定性改造计划

## 摘要
- 目标：在当前 Web PTY 架构基础上，把终端体验推进到发布级，优先解决显示一致性、重连恢复、会话治理与线上可观测性问题。
- 范围：仅改造现有 Web 终端同步与会话控制链路，不改底层会话来源。
- 已锁定决策：
1. 保留当前 `PortaPty + SignalR + term.snapshot/term.patch/term.raw` 架构。
2. 底层会话本期不改为 SSH。
3. 底层会话本期不引入 `tmux` 托管。
4. 第一优先级是显示稳定，而不是协议替换。
5. 允许为稳定性收紧交互：observer/后台页面不得修改 PTY 尺寸。

## 实施范围
- `apps/secretary-web`：终端 attach/fit/resize/connect 时序、屏幕缓存与恢复状态机、前后台页面治理。
- `apps/terminal-gateway-dotnet/TerminalGateway.Api`：实例显示 owner、geometry epoch、权威 snapshot 语义、resync 机制、观测指标。
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`：显示一致性、跨窗口尺寸竞争、重连恢复相关测试。

## 公开接口与类型变更
- 扩展 `term.snapshot`：
1. 新增 `render_epoch`，表示当前权威显示代次。
2. 保留 `size.cols/rows` 作为服务端实际 PTY 几何。
3. `snapshot` 成为唯一权威屏幕基线。
- 扩展 `term.patch`：
1. 新增 `render_epoch`。
2. 仅允许在与当前已应用基线同一 `render_epoch` 下合并。
- 收紧 `term.resize.ack`：
1. 仅表示 resize 请求被接受或拒绝。
2. 不再代表“客户端显示已稳定”。
- 新增可选事件：
1. `term.owner.changed`：当前实例控制端切换。
2. `term.viewport.rejected`：非 owner 或无效 viewport 的 resize 被拒绝。
3. `term.sync.required`：服务端或前端发现显示状态不可信，需要全量恢复。

## 方案细节（可直接实现）
### Phase 1：显示一致性止血
1. 为每个实例引入 `display owner`，只有 owner 可以触发 `RequestResize` 并真正修改 PTY 尺寸。
2. 前端增加页面可见性与 tab 门控：
- `document.hidden === true` 时禁止 resize。
- 非 `terminal` tab 禁止 resize。
- 首次未完成有效 fit 时禁止连接与 resize。
3. 首次 attach 前必须完成有效 geometry 测量：
- terminal host 可见。
- `cols/rows > 0`。
- 连续两次测量一致。
- 字体 ready 后优先使用稳定值；若超时则使用最后一次有效值。
4. resize 流程改为两段式：
- owner 发起 `RequestResize`。
- 服务端应用 PTY resize 后递增 `render_epoch` 并下发新的全量 `term.snapshot`。
5. 前端在收到 resize ack 后不得继续把旧 patch 当成稳定显示，只能等待新 `render_epoch` 的 snapshot 建立基线。
6. attach/reconnect 后第一帧必须是 snapshot；在基线未建立前，patch/raw 只缓存不渲染。
7. 任意以下情况直接触发全量 resync：
- snapshot 或 patch 的 `render_epoch` 与当前基线不一致。
- 本地 viewport 与服务端 `size` 持续不一致。
- resize 后超时未收到新 snapshot。
- 页面从后台恢复后检测到当前屏幕状态不可信。

### Phase 2：建立发布级会话稳定模型
1. 为实例显示链路建立三类状态：
- `instance_epoch`：实例生命周期代次。
- `render_epoch`：几何/显示代次。
- `seq`：输出顺序。
2. 明确协议职责：
- `term.snapshot`：权威基线。
- `term.patch`：当前基线上的增量更新。
- `term.raw`：仅用于回放、补齐、审计，不承担几何纠偏。
3. 前端建立 attach 状态机：
- `idle -> measuring -> attaching -> awaiting_snapshot -> ready -> resyncing`。
4. 服务端 `RequestSync(type=screen)` 始终返回当前权威 snapshot；`RequestSync(type=raw)` 仅用于补齐 raw gap。
5. 前端只在以下条件满足时应用 patch：
- 已存在 snapshot 基线。
- `render_epoch` 一致。
- `seq` 连续或可证明未丢帧。
6. 一旦进入不可信状态，暂停 patch 应用，直到全量 snapshot 覆盖本地 screen cache 后再恢复。
7. 服务端为每个连接维护角色：
- owner：可 resize。
- observer：只观察，不允许改 PTY 几何。

### Phase 3：产品化能力补齐
1. 认证与授权：
- 接入用户身份。
- 区分实例创建、查看、输入、resize、终止权限。
2. 审计：
- 记录会话创建、销毁、attach、detach、resize、输入来源与关键恢复事件。
- 保留关键 `term.raw` / 控制操作日志用于追查错屏。
3. 资源治理：
- 限制每用户/每节点并发会话数。
- 对慢客户端、重连风暴、重复 resync 做节流。
- 增加长时间无 owner 或无活动会话的回收策略。
4. 异常隔离：
- 单实例异常不影响整个 hub。
- 显示恢复失败时有明确错误状态，不无限重试。
5. 可观测性：
- 服务端指标：`resize_requests_total`、`resize_applied_total`、`resize_rejected_not_owner_total`、`snapshot_sent_total`、`resync_requested_total`、`resync_completed_total`、`render_epoch_mismatch_total`。
- 前端调试状态：当前 owner、server size、本地 viewport、`render_epoch`、attach state。

## 测试计划
- 后端单元/集成：
1. 非 owner resize 被拒绝，PTY 几何不变。
2. owner resize 后必须产生新的 `render_epoch` 和全量 snapshot。
3. patch 跨 `render_epoch` 被丢弃并触发 resync。
4. reconnect 后第一帧必须为 snapshot。
5. geometry 为 0 或不稳定时 attach 被阻塞。
- 前端集成：
1. 后台 tab 打开页面时不提前 connect。
2. terminal/file tab 切换不触发远端 resize。
3. 双窗口不同尺寸同时打开同一实例，只有 owner 能改变 PTY 尺寸。
4. 字体未 ready 或布局未稳定时，不产生错误首屏。
- 端到端：
1. 单实例创建、连接、输入、resize、刷新恢复。
2. 双窗口焦点切换、最小化、恢复、关闭 owner。
3. 高频输出场景下无持续错行。
4. `vim`、`less`、`top` 等基础 TUI 场景异常时能自动全量恢复。

## 实施顺序
1. 完成 Phase 1：owner + attach gating + resize 受控协商 + snapshot 基线恢复。
2. 完成 Phase 2：协议代次化、前端状态机、patch 应用收紧、resync 规则统一。
3. 完成 Phase 3：认证授权、审计、资源治理、观测与发布门槛。

## 假设与默认值
- 默认继续使用 SignalR 作为前后端终端事件通道。
- 默认保留当前 `PortaPtyEngine` 作为底层 PTY 运行时。
- 默认不在本计划内引入 SSH 会话后端。
- 默认不在本计划内引入 `tmux`、`screen` 等底层会话托管。
- 默认以“显示稳定优先于交互完全兼容”为策略，必要时收紧旧行为。
