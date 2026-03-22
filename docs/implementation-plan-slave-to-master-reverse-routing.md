# Master/Slave 终端模式补齐方案

## Summary
- 目标是把现有“master 视角可操作 slave”的能力，补成“master/slave 两侧前端都能以同一集群视角工作”，并把节点状态、实例归属、终端事件同步、双套本地部署、测试覆盖一起补完整。
- 本期锁定范围：
  - `slave` 前端做到 Desktop Terminal 页全一致。
  - 本地双套环境按 `systemd + nginx` 方式落地，两套后端、两套前端、两套配置。
- 默认行为：
  - 节点下拉显示全部节点，`offline` 不隐藏。
  - 节点刷新按钮主动刷新“节点 + 实例”，避免状态和实例列表脱节。
  - `slave` 页面中选择 `master` 节点后，创建的实例真实归属仍是 `master`，后续连接、输入、resize、sync、关闭都按真实归属路由。

## Key Changes
- 前端节点与实例视图补齐：
  - Desktop Terminal 页节点下拉展示全量节点，不按 `node_online` 过滤。
  - 选中节点离线时仍允许查看该节点与其历史实例，但新建终端动作需阻止并给出明确提示。
  - 在左侧“目标节点”区域新增“刷新节点状态”按钮，执行 `fetchNodes + fetchInstances`，刷新后重新校正 `createNodeId`、当前可见实例、选中实例、右侧文件树目标节点。
  - `slave` 页面默认节点选择策略改为：优先当前选中节点，否则优先在线 `master`，否则首个节点。
- slave 侧补齐“集群视角读取 + 反向命令”能力：
  - `GET /api/v2/nodes` 在 slave 模式下优先向 master 请求集群节点视图；master 不可达时退回仅本机节点并标记连接异常。
  - `GET /api/v2/instances` 在 slave 模式下优先向 master 请求全量实例视图；master 不可达时退回本地实例。
  - 复用并补全 `ClusterHub.RequestCommand`，让 slave 通过已注册 cluster 连接请求 master 本机执行 `instance.create/input/resize/sync/terminate`。
  - 在 `SlaveClusterBridgeService` 中增加查询 master 节点、查询 master 实例、向 master 请求实例命令的方法封装，供 slave 模式下的 HTTP API 与 Hub 路由复用。
- TerminalHub 与实例归属保持单一真相：
  - `JoinInstance`、`SendInput`、`RequestResize`、`RequestSync` 继续只按 `instance_id -> node_id` 路由，不区分实例最初由谁发起创建。
  - `RemoteInstanceRegistry` 继续作为实例归属单一真相源；反向创建 master 实例成功后，在 slave 侧也登记 `instance_id -> master-node`，terminate/exit 后两侧都清理映射。
  - `term.snapshot/raw/exit/...` 中的 `node_id/node_name` 必须始终是 PTY 实际所在节点，不能写成请求发起的 slave。
- Desktop Terminal 页的 node-aware 一致行为：
  - slave 页面选中 master 节点或连接到 master 实例后，文件列表、读写、上传、建目录都按目标节点路由到 master。
  - 不允许静默回退成 slave 本地文件系统。
  - 配方仍可保持前端本地存储，但“执行配方创建终端”必须落到当前目标节点。
- 本地双套部署脚本：
  - 新增一套本地部署脚本，沿用现有 `release-local.sh` / `release-frontend-local.sh` 风格。
  - 脚本文件：
    - `deploy/release-cluster-local.sh`
    - `deploy/release-cluster-frontend-local.sh`
    - `deploy/verify-cluster-local.sh`
  - 后端生成两套 service，例如 `terminal-gateway-master.service`、`terminal-gateway-slave.service`。
  - 前端发布到两个 web 目录，例如 `pty-agent-web-master`、`pty-agent-web-slave`。
  - 分别注入 `GATEWAY_ROLE`、`PORT`、`NODE_ID`、`NODE_NAME`、`MASTER_URL`、`CLUSTER_TOKEN`、日志路径。
- nginx 同源接入策略：
  - `master` 前端入口始终走本机同源 `/web-pty` 到 master gateway。
  - `slave` 前端入口始终走本机同源 `/web-pty` 到 slave gateway。
  - 浏览器始终连当前页面所属后端，由后端完成 cluster 转发，不引入浏览器跨域直连 master。

## Public API / Interface Changes
- `GET /api/v2/nodes`
  - master 模式返回本机 + 已注册远端节点。
  - slave 模式优先返回 master 视角的聚合节点视图；master 不可达时返回降级结果。
- `GET /api/v2/instances`
  - master 模式返回本机 + 已登记远端实例。
  - slave 模式优先返回 master 视角的聚合实例视图；master 不可达时返回降级结果。
- `ClusterContracts`
  - `ClusterCommandEnvelope` / `ClusterCommandResult` 继续使用并固化 `source_node_id`、`target_node_id` 语义。
  - 保留兼容期 `node_id -> target_node_id` 映射，避免一次性打断现有 `Master -> Slave` 路径。
- `ClusterHub`
  - `RequestCommand` 作为 slave -> master 反向命令入口继续保留并补全读写场景。
  - 现有 `ClusterCommand` 事件继续作为 master -> slave 下发通道。
- `RemoteInstanceRegistry`
  - 继续作为唯一实例归属来源，需保证 master 本机实例和 slave 实例都能被远端创建流程正确登记和清理。

## Deployment Compatibility
- 当前同机双套部署时，“同源 `/web-pty`”表示：
  - `master-ui` 的 `/web-pty` 反代到本机 master gateway。
  - `slave-ui` 的 `/web-pty` 反代到本机 slave gateway。
  - 这是前端接入策略，不代表 master/slave 后端必须部署在同一台机器。
- 未来若 master/slave 分布到两台服务器，前端策略保持不变：
  - `master` 页面仍请求自己域名下的 `/web-pty`，由 master 机器 nginx 反代到本机 master gateway。
  - `slave` 页面仍请求自己域名下的 `/web-pty`，由 slave 机器 nginx 反代到本机 slave gateway。
  - slave 页面上“查看 master 节点 / 在 master 创建实例 / 操作 master 实例”依然通过 slave gateway 和 master 之间的 cluster 通道完成，而不是让浏览器直接跨域请求 master。
- 同机与双机的实际区别：
  - 同机部署主要是本地回环端口与本地文件目录隔离。
  - 双机部署会额外引入 `MASTER_URL` 可达性、TLS、SignalR 反代、心跳超时、防火墙、域名解析等运维要求。
  - 双机场景下，`slave-ui` 可能可访问但 slave 到 master 的 cluster 链路已断开，因此节点列表、实例列表、master 实例操作需要支持“页面可打开但集群能力降级”的状态表达。
- 设计约束：
  - 这次实现不得把浏览器和 master 的直连关系写死在前端构建产物里。
  - 这次新增部署脚本和 nginx 配置应保持“同机可用、双机不推翻”的结构，只替换地址与 service 部署位置即可。

## Test Plan
- 后端 `.NET` 集成测试新增：
  - 节点列表包含 offline 节点，heartbeat 超时后保留节点记录，仅切换 `node_online=false`。
  - slave 模式下节点列表查询返回 master 视角的全量节点，而不是只返回 slave 本机。
  - slave 模式下实例列表查询返回 master 视角的全量实例。
  - slave -> master 反向 `instance.create` 成功，返回的实例摘要标识 `node_id=master`。
  - 对“由 slave 发起、实际创建在 master 的实例”执行 `JoinInstance`、`SendInput`、`RequestResize`、`RequestSync`、`Terminate`，都能成功路由。
  - master 通过该实例产生输出后，slave 页面对应链路能收到 `term.snapshot/raw`，且元数据仍是 master。
  - terminate/exit 后，主从两侧 `RemoteInstanceRegistry` 都会清理映射。
- 前端单测/组件测试：
  - 节点下拉不再过滤 offline。
  - 刷新按钮触发 `fetchNodes + fetchInstances`，并在保留当前节点选择的同时修正失效选择。
  - slave 页面默认节点优先选在线 master。
- Playwright e2e 新增真实场景：
  - master 前端可见 master/slave 节点并分别创建实例。
  - slave 前端也可见 master/slave 节点。
  - 在 slave 前端选择 master 创建实例后，master 前端实例列表能看到该实例。
  - 在 slave 前端创建的 master 实例，master 前端可连接、输入并看到输出。
  - 选中离线节点时，下拉仍可见，创建动作被阻止且提示明确。
  - 刷新按钮可把节点在线状态和实例归属刷新到最新。
- 部署验证：
  - 新脚本执行后两套 service 均可启动。
  - 访问 master/slave 两个前端入口都能正常加载并请求各自后端。
  - slave 启动后在 master `/api/nodes` 中可见。

## Assumptions
- 本期只把 Desktop Terminal 页做成 master/slave 全一致；`ProcessesView` 等其他页面暂不跟进。
- offline 节点可选但不可创建新终端；如果该节点已有实例，实例可见但交互按钮按在线状态决定是否禁用。
- 刷新按钮是手动补救入口，不替代现有自动同步；自动同步保留，手动刷新只是在用户怀疑状态过期时强制拉取。
- `GET /api/v2/nodes`、`GET /api/v2/instances` 的 slave 视角聚合优先走 master；master 不可达时返回降级结果并带错误状态。
- 不做浏览器直接连接 master 的跨域方案，浏览器始终连当前页面所属后端，由后端完成 cluster 代理。
