# 支持 Slave -> Master 反向调度的改造清单

## Summary
- 在现有“`Master -> Slave` 单向代理”基础上，补齐“`Slave -> Master` 反向创建与交互”能力，但仍保持 `master` 作为集群控制中心和 Web 统一入口。
- 实现目标是：slave 上的调用方可以通过已有集群链路请求 master 创建 PTY、输入、resize、sync、terminate，并复用现有 `TerminalHub` 事件分发与实例路由能力。

## Key Changes
- 扩展集群命令模型，区分“命令目标节点”和“命令发起节点”。
  - 在 `ClusterCommandEnvelope` / `ClusterCommandResult` 中新增 `source_node_id`、`target_node_id`、可选 `reply_to_node_id`。
  - 保留当前 `node_id` 兼容期映射到 `target_node_id`，避免一次性打断现有 `Master -> Slave` 路径。
- 让 `ClusterCommandBroker` 支持向任意已连接远端节点下发命令，而不是默认只服务 master 对 slave。
  - `SendAsync` 改为显式接收 `targetNodeId` 和 `sourceNodeId`。
  - 超时、结果匹配仍按 `command_id` 关联，但完成结果时校验 `source/target`，避免串包。
- 在 `ClusterHub` 中加入“反向命令入口”。
  - 新增一个仅供 slave 调用的方法，例如 `RequestClusterCommand` 或复用 `SubmitCommandResult` 旁路入口，不通过 HTTP 暴露。
  - master 收到 slave 请求后，如果 `target_node_id` 是 master 本机，则直接调用本地 `InstanceManager` / `FileApiService` 执行；如果未来允许 slave->slave，则再交给 broker 转发。
  - 反向创建成功后，把 `instance_id -> master-node` 写入 `RemoteInstanceRegistry`，使后续 `TerminalHub.SendInput/RequestResize/RequestSync` 能路由到 master 本机或远端节点。
- 为 slave 增加“远端 master 实例代理器”。
  - 在 `SlaveClusterBridgeService` 增加主动请求 master 创建/输入/resize/sync/terminate 的客户端方法封装，供 slave 侧 HTTP API 或内部调用复用。
  - 如果当前需求包含“调用 slave 的本地 HTTP API 来操作 master 实例”，则补充 slave 模式下的节点 API：`POST /api/nodes/{nodeId}/instances`、`input`、`resize`、`delete`，当 `nodeId` 指向 master 时走 cluster 反向请求而不是本地执行。
- 补齐 master 本机实例的远端可见性。
  - `GET /api/nodes` 继续返回 master 节点；新增或复用实例摘要中的 `node_id/node_name/node_role/node_online`，确保 slave 发起后也能标识实例实际归属是 master。
  - 若 slave 侧需要列出 master 实例，补一个最小只读接口或 cluster 查询命令；否则只要求已知 `instance_id` 后可继续交互。
- 保持 `TerminalHub` 单一行为。
  - `JoinInstance/SendInput/RequestResize/RequestSync` 不区分实例最初是由 master 还是 slave 发起创建，只按 `instance_id -> node_id` 路由。
  - `term.*` 事件继续携带真实归属节点 `node_id/node_name`，避免前端误判为 slave 本地实例。
- 安全约束。
  - 反向命令必须继续校验 `CLUSTER_TOKEN`。
  - master 仅接受来自已注册 slave 连接的反向请求。
  - 默认不开放 slave 对 master 的任意文件系统能力，只开放与 master 本机实例绑定的 `files.upload`。

## Public API / Interface Changes
- `ClusterContracts`
  - `ClusterCommandEnvelope`: 新增 `SourceNodeId`、`TargetNodeId`。
  - `ClusterCommandResult`: 新增 `SourceNodeId`、`TargetNodeId`。
  - 可新增 `ClusterProxyRequest`，用于 slave 请求 master 执行本机命令。
- `ClusterHub`
  - 新增 slave -> master 命令请求方法，例如 `RequestCommand(ClusterCommandEnvelope request)`。
  - 现有 `ClusterCommand` 事件继续保留，供 master -> slave 下发使用。
- `ApiRoutes`
  - 若要求从 slave 的 HTTP 入口操作 master，保持现有节点 API 路径不变，只增加 slave 模式下对 master 节点的转发支持。
  - 若不要求开放 slave HTTP 入口，则无需新增公开 HTTP 路径，只补 cluster 内部接口。
- `RemoteInstanceRegistry`
  - 继续作为唯一实例归属来源，需保证 master 本机实例和 slave 实例都可被远端创建流程正确登记/清理。

## Test Plan
- 后端集成测试新增：
  - `Slave_Node_Should_Request_Master_Create_Instance_Through_ClusterHub`
    - 模拟 slave 连接 master，请求在 master 本机创建实例，断言返回 `instance_id` 且 registry 记录归属为 master。
  - `Slave_Node_Should_Request_Master_Input_Resize_And_Terminate_On_Master_Instance`
    - 对反向创建的 master 实例执行输入、resize、terminate，断言命令成功且本地实例状态变化正确。
  - `TerminalHub_Should_Route_Remote_Instance_Operations_To_Master_For_SlaveCreatedMasterInstance`
    - Web 连接 master 的 `TerminalHub`，对该实例执行 `JoinInstance/SendInput/RequestSync`，断言可以收到 master 实际 PTY 的 `term.snapshot/term.raw/term.resize.ack`。
  - `Cluster_PublishTerminalEvent_Should_Preserve_Master_Node_Metadata_For_SlaveRequestedMasterInstance`
    - 确认事件中的 `node_id/node_name` 是 master，而不是发起请求的 slave。
  - `Reverse_Cluster_Command_Should_Reject_Unregistered_Or_Unauthorized_Slave`
    - 校验 token 和连接绑定。
  - `RemoteInstanceRegistry_Should_Remove_MasterOwnedInstance_When_Terminated_From_SlaveFlow`
    - 断言 terminate 后映射清理。
- 回归测试必须继续通过：
  - 现有 `Master -> Slave` 注册/心跳/代理 CRUD/IO。
  - 现有 `term.patch` 去重与 seq gap 处理。
- 前端或 e2e（如果要暴露 slave HTTP/UI 入口）：
  - 增加一个真实场景：请求落到 slave 的入口，但实例实际创建在 master，随后输入输出正常。
  - 若前端仍只连 master，则无需额外 UI 改造，只做回归确认实例元数据展示正确。

## Assumptions
- 默认继续保持“Web 只连 master”，不让浏览器直接连 slave。
- 默认只实现 `Slave -> Master`，不扩展为通用 `Slave -> Slave` 任意转发；协议设计预留 `target_node_id` 即可。
- 默认 slave 反向操作 master 的入口优先走 cluster 内部 RPC，不新增新的公网 API。
- 默认实例真实归属节点始终由执行 PTY 的节点决定；因此 `node_id/node_name` 必须显示 master，而不是请求发起的 slave。
- 默认兼容现有单向实现，先做协议字段向后兼容，再补测试，最后再考虑清理旧的 `node_id` 单字段语义。
