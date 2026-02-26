# 终端交互与 Master/Slave 同步改造计划（SignalR 主链路）

## 摘要
- 目标 1：优化 Web 终端交互与同步体验，重点解决“切换实例慢（秒级）”、粘贴与图片上传、快捷键后焦点回归、以及核心指令端到端回归。
- 目标 2：新增单主多从架构。Web 只连接 master；master 统一管理与代理 slave 终端；支持多 slave 清晰区分；本期不做自动切主。
- 已锁定决策：
1. 同步架构：单主多从复制。
2. 前端通道：保留 SignalR。
3. 一致性：有序至少一次。
4. 故障策略：不做切主。
5. 图片上传：上传为文件并插入路径。
6. 服务间通道：SignalR/gRPC 流优先，先落地 SignalR。
7. 测试范围：核心指令回归，覆盖 mobile 快捷键。

## 实施范围
- `apps/secretary-web`：终端交互、实例切换性能、节点标识、上传能力、e2e 扩展。
- `apps/terminal-gateway-dotnet/TerminalGateway.Api`：master/slave 模式、节点管理、实例代理、事件复制、协议扩展。
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`：集成测试扩展（双后端场景）。
- `apps/secretary-web/e2e`：新增 master/slave 与交互细节端到端用例。

## 公开接口与类型变更
- 新增后端运行模式配置：
1. `GATEWAY_ROLE=master|slave`（默认 `master`）。
2. `MASTER_URL`（slave 连接 master）。
3. `NODE_ID`、`NODE_NAME`、`NODE_LABEL`（slave 身份）。
4. `CLUSTER_TOKEN`（master/slave 鉴权）。
- 扩展实例摘要（`/api/instances` 返回）：
1. `node_id`
2. `node_name`
3. `node_role`（`master|slave`）
4. `node_online`
- 新增 master 节点管理 API：
1. `GET /api/nodes`：节点列表与在线状态。
2. `POST /api/nodes/{nodeId}/instances`：在指定节点创建实例。
3. `POST /api/nodes/{nodeId}/instances/{instanceId}/input`
4. `POST /api/nodes/{nodeId}/instances/{instanceId}/resize`
5. `DELETE /api/nodes/{nodeId}/instances/{instanceId}`
6. `POST /api/nodes/{nodeId}/files/upload`：上传文件到目标节点。
- 扩展 Hub 事件 `TerminalEvent` 载荷：
1. 所有 `term.*` 事件附加 `node_id`、`node_name`。
2. 新增 `term.route`（可选）用于前端显示“当前正在操作哪个节点实例”。
- 新增集群通道（SignalR）：
1. `ClusterHub`：slave -> master 注册、心跳、事件上报、RPC 请求响应。
2. 事件 envelope：`event_id`、`node_id`、`instance_id`、`seq`、`ts`、`type`、`payload`。

## 方案细节（可直接实现）
### A. Web 终端交互与同步优化
1. 重构前端实例切换流程为“单连接多实例切换”，禁止每次切换时 `disconnect/start`。
2. 实现 `JoinInstance(new)` + `LeaveInstance(old)` 快速切换，保持同一 SignalR 连接。
3. 在前端缓存每个实例最近 `snapshot/patch` 状态，切换时先秒开本地缓存，再异步 `RequestSync(screen)` 校准。
4. 优化渲染器：移除每次 `term.patch` 的全屏 `clear + full render`，改为行级增量写入。
5. 增加粘贴能力：
- 支持 `Ctrl/Cmd+V` 与右键粘贴。
- 采用 bracketed paste（`\x1b[200~...\x1b[201~`）包裹，防止多行粘贴误触发。
6. 增加图片上传：
- Web 选择文件 -> 调用 upload API 到目标节点 -> 将返回路径插入命令行（可配置“仅插入”或“插入并回车”）。
7. 焦点与快捷键细节：
- 点击快捷键（mobile）或工具栏动作后，自动 `term.focus()`。
- 切换实例、上传完成、粘贴后保持终端光标可输入。
8. 节点可视化：
- 实例列表显示 `node_name/node_id`、`master|slave` 标签、在线状态。
- 当前连接头部显示“正在操作：{node_name}/{instance_id}”。

### B. Master/Slave 同步机制
1. 同一后端程序支持两种角色：
- master：对外提供 Web API + Hub，维护节点注册表与代理路由。
- slave：本地维护 `InstanceManager`，主动连接 master 的 `ClusterHub`。
2. slave 注册与保活：
- 启动后注册节点元数据。
- 5s 心跳上报负载、实例数、last_seen。
3. 操作代理：
- Web 发起到 master。
- master 根据 `node_id` 路由到本地实例或远端 slave。
- slave 执行并返回 ack/错误。
4. 事件复制（有序至少一次）：
- slave 侧每实例单调 `seq`。
- master 侧按 `(node_id, instance_id, seq)` 去重。
- 检测缺口时触发 `RequestSync(screen)` 拉平状态。
5. 多 slave 区分：
- 节点唯一键 `node_id`，显示名 `node_name`，可选分组字段 `node_label`（如机房/区域）。
6. 本期不实现：
- 自动选主、自动切换、跨 master 一致性。

## 性能与验收指标
- 终端切换耗时（本地）`p95 <= 300ms`。
- 终端切换耗时（跨公网到 slave）`p95 <= 800ms`。
- 切换后首屏可见（缓存命中）`<= 120ms`。
- 快捷键操作后焦点回归成功率 `100%`（e2e 场景）。

## 测试计划
- 后端单元/集成：
1. 节点注册、心跳超时、在线状态切换。
2. master 代理到 slave 的创建/输入/resize/终止。
3. 事件去重与乱序补偿（重复 seq、缺 seq）。
4. slave 离线时错误返回与前端可见状态。
- 前端 e2e（Playwright）：
1. 快速切换多个实例，验证状态与耗时门限。
2. mobile 快捷键全覆盖：`Esc/Tab/Enter/Ctrl+C/方向键`，并验证焦点回终端。
3. 粘贴多行命令，验证完整输入与执行结果。
4. 图片上传后路径插入，目标实例能读取该文件。
5. master + 2 slave 场景下，实例列表可区分节点并可正确路由操作。
6. 对核心指令流做回归：创建、连接、输入、重同步、终止、切换节点实例。

## 实施顺序
1. 后端基础：角色配置、节点注册、ClusterHub 通道。
2. 代理能力：master 路由 CRUD/IO 到 slave。
3. 事件同步：seq envelope、去重、缺口补偿。
4. 前端改造：快速切换、节点标识、焦点策略、粘贴与上传。
5. 测试补齐：双后端集成测试 + Playwright 场景扩展。
6. 性能压测与阈值校准。

## 假设与默认值
- 默认 master 对外地址固定，slave 可主动连通 master（NAT 场景允许出站连接）。
- 默认继续使用 SignalR；MQTT 暂不引入。
- 默认图片上传大小限制 10MB，类型白名单 `png/jpg/jpeg/webp/gif`。
- 默认上传目录为目标实例 `cwd` 下 `.webcli-uploads/`。
- 默认仅 master 提供 Web 前端与公开 API，slave 不直接暴露给 Web。
