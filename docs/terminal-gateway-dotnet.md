# terminal-gateway-dotnet 运行与接口说明

## 1. 适用范围

本文档描述当前仓库中实际启用的 terminal gateway 能力，基于以下源码：

- `apps/terminal-gateway-dotnet/TerminalGateway.Api`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`

当前公开入口只有三类：

- REST API：`/api/*`
- 终端 Hub：`/hubs/terminal`
- 集群 Hub：`/hubs/cluster`

旧版 WebSocket 路由代码仍保留在仓库里作为历史兼容实现，但当前 `Program.cs` 没有注册，不属于预览版对外接口。

HTTP API 的 JSON 字段统一使用 `snake_case`。

## 2. 本地运行

### 2.1 单机模式

```bash
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

默认监听：

- `HOST=0.0.0.0`
- `PORT=8080`
- `GATEWAY_ROLE=master`

### 2.2 显式启动 master

```bash
GATEWAY_ROLE=master \
PORT=7300 \
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

### 2.3 启动 slave

```bash
GATEWAY_ROLE=slave \
HOST=127.0.0.1 \
PORT=7320 \
NODE_ID=slave-local \
NODE_NAME="Slave Local" \
MASTER_URL=http://127.0.0.1:7310 \
CLUSTER_TOKEN=dev-cluster-token \
FILES_BASE_PATH=/home/yueyuan/gitlab \
TERMINAL_SETTINGS_STORE_FILE=/tmp/pty-agent-terminal-settings-slave.json \
TERMINAL_PROFILE_STORE_FILE=/tmp/pty-agent-terminal-profiles-slave.json \
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

关键约束：

- `slave` 必须能访问 `MASTER_URL`
- `CLUSTER_TOKEN` 必须与 master 一致
- master/slave 需要使用不同的 `PORT`、`NODE_ID` 和本地存储文件

## 3. 主要环境变量

- `HOST`：监听地址
- `PORT`：监听端口
- `GATEWAY_ROLE`：`master` 或 `slave`
- `NODE_ID`：节点唯一标识
- `NODE_NAME`：节点展示名称
- `MASTER_URL`：slave 连接的 master 地址
- `CLUSTER_TOKEN`：集群鉴权 token
- `FILES_BASE_PATH`：文件 API 允许访问的根目录
- `TERMINAL_SETTINGS_STORE_FILE`：设置存储文件
- `TERMINAL_PROFILE_STORE_FILE`：配置档存储文件

## 4. REST API

### 4.1 健康与节点

- `GET /api/health`
  返回网关健康状态、实例数和运行指标。

- `GET /api/nodes`
  返回当前可见节点列表。slave 模式下优先返回 master 聚合视图；失败时可能返回 `degraded=true`。

示例响应字段：

- `items[].node_id`
- `items[].node_name`
- `items[].node_role`
- `items[].is_current`
- `items[].node_online`
- `items[].instance_count`
- `items[].last_seen_at`

### 4.2 终端实例

- `GET /api/instances`
  返回当前可见实例列表。

- `POST /api/instances`
  在当前节点创建实例。

请求体：

```json
{
  "command": "bash",
  "args": ["-lc", "pwd"],
  "cwd": "/home/yueyuan",
  "cols": 120,
  "rows": 30,
  "env": {
    "TERM": "xterm-256color"
  }
}
```

- `DELETE /api/instances/{id}`
  终止当前节点上的实例。

- `POST /api/nodes/{nodeId}/instances`
  在指定节点创建实例。

- `POST /api/nodes/{nodeId}/instances/{instanceId}/input`
  向指定节点实例写入标准输入。

请求体：

```json
{
  "data": "ls -la\n"
}
```

- `POST /api/nodes/{nodeId}/instances/{instanceId}/resize`
  调整指定节点实例尺寸。

请求体：

```json
{
  "cols": 140,
  "rows": 40
}
```

- `DELETE /api/nodes/{nodeId}/instances/{instanceId}`
  终止指定节点实例。

实例列表常用字段：

- `items[].id`
- `items[].command`
- `items[].cwd`
- `items[].cols`
- `items[].rows`
- `items[].created_at`
- `items[].status`
- `items[].clients`
- `items[].node_id`
- `items[].node_name`
- `items[].node_role`
- `items[].node_online`

### 4.3 进程 API

本地与按节点代理各有一组接口。

本地接口：

- `POST /api/processes/run`
- `POST /api/processes`
- `GET /api/processes`
- `GET /api/processes/{processId}`
- `GET /api/processes/{processId}/output`
- `POST /api/processes/{processId}/wait?timeout_ms=30000`
- `POST /api/processes/{processId}/stop`
- `DELETE /api/processes/{processId}`

按节点代理接口：

- `POST /api/nodes/{nodeId}/processes/run`
- `POST /api/nodes/{nodeId}/processes`
- `GET /api/nodes/{nodeId}/processes`
- `GET /api/nodes/{nodeId}/processes/{processId}`
- `GET /api/nodes/{nodeId}/processes/{processId}/output`
- `POST /api/nodes/{nodeId}/processes/{processId}/wait?timeout_ms=30000`
- `POST /api/nodes/{nodeId}/processes/{processId}/stop`
- `DELETE /api/nodes/{nodeId}/processes/{processId}`

`RunProcessRequest` 请求体：

```json
{
  "file": "bash",
  "args": ["-lc", "echo hello"],
  "cwd": "/home/yueyuan/pty-agent",
  "stdin": "",
  "timeout_ms": 30000,
  "allow_non_zero_exit_code": false,
  "env": {},
  "pipeline": [],
  "metadata": {}
}
```

停止请求体：

```json
{
  "force": false
}
```

### 4.4 文件与项目 API

项目：

- `GET /api/projects`

本地文件接口：

- `GET /api/files/list`
- `GET /api/files/read`
- `POST /api/files/write`
- `POST /api/files/upload`
- `POST /api/files/mkdir`
- `POST /api/files/rename`
- `DELETE /api/files/remove`
- `GET /api/files/download`

按节点代理文件接口：

- `GET /api/nodes/{nodeId}/files/list`
- `GET /api/nodes/{nodeId}/files/read`
- `POST /api/nodes/{nodeId}/files/write`
- `POST /api/nodes/{nodeId}/files/upload`
- `POST /api/nodes/{nodeId}/files/mkdir`
- `GET /api/nodes/{nodeId}/files/download`

常用查询参数：

- `path`
- `show_hidden`
- `max_lines`
- `chunk_bytes`
- `line_offset`
- `direction`
- `mode`

`GET /api/files/read` 的 `mode`：

- `preview`：轻量预览，可能截断
- `edit`：文本编辑模式，小文件直接返回完整文本
- `progressive`：大文件分块读取

写文件请求体：

```json
{
  "path": "/home/yueyuan/pty-agent/README.md",
  "content": "new content"
}
```

新建目录请求体：

```json
{
  "path": "/home/yueyuan/pty-agent/docs",
  "name": "draft"
}
```

重命名请求体：

```json
{
  "path": "/home/yueyuan/pty-agent/docs/old.md",
  "new_name": "new.md"
}
```

上传约束：

- 图片上传目录为目标路径下的 `.webcli-uploads/`
- 常规图片白名单：`.png`、`.jpg`、`.jpeg`、`.webp`、`.gif`
- 图片默认大小限制 10 MiB

## 5. Terminal Hub

连接地址：

- `/hubs/terminal`

客户端调用的方法：

- `JoinInstance`
- `LeaveInstance`
- `SendInput`
- `RequestResize`
- `RequestSync`

请求体：

```json
{ "instanceId": "xxx" }
```

```json
{ "instanceId": "xxx", "data": "ls -la\n" }
```

```json
{ "instanceId": "xxx", "cols": 140, "rows": 40, "reqId": "resize-1" }
```

```json
{ "instanceId": "xxx", "type": "screen", "reqId": "sync-1", "sinceSeq": 0, "before": "h-1", "limit": 200 }
```

服务端事件名固定为 `TerminalEvent`，常见 `type`：

- `term.snapshot`
- `term.raw`
- `term.patch`
- `term.history.chunk`
- `term.resize.ack`
- `term.sync.complete`
- `term.sync.required`
- `term.owner.changed`
- `term.exit`

事件公共字段通常包含：

- `v`
- `type`
- `instance_id`
- `node_id`
- `node_name`
- `ts`

说明：

- 当前前端主链路依赖 `term.snapshot + term.raw + term.sync.complete`
- `term.patch` 仍可能出现，但预览版文档不建议新客户端依赖其作为唯一同步来源

## 6. Cluster Hub

连接地址：

- `/hubs/cluster`

该 Hub 仅用于 master/slave 内部通信，不作为浏览器对外接口。

主要方法：

- `RegisterNode`
- `Heartbeat`
- `SubmitCommandResult`
- `RequestCommand`
- `PublishTerminalEvent`
- `SubscribeInstanceEvents`
- `UnsubscribeInstanceEvents`
- `SyncNodeInstances`

鉴权方式：

- 如果配置了 `CLUSTER_TOKEN`，调用方必须在请求体中携带相同 token

## 7. 部署脚本

单机 master：

```bash
bash deploy/single-master/build-frontend.sh
sudo bash deploy/single-master/install-service.sh
sudo bash deploy/single-master/install-nginx.sh
bash deploy/single-master/verify.sh
```

生产 master + 局域网 slave：

```bash
bash deploy/cluster-lan/build-master-frontend.sh
sudo bash deploy/cluster-lan/install-master-service.sh
sudo bash deploy/cluster-lan/install-master-nginx.sh

bash deploy/cluster-lan/build-slave-frontend.sh
sudo bash deploy/cluster-lan/install-slave-service.sh
sudo bash deploy/cluster-lan/install-slave-nginx.sh

bash deploy/cluster-lan/verify-cluster.sh
```

## 8. 测试

运行全部 .NET 测试：

```bash
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.sln -v minimal
```

只跑网关测试项目：

```bash
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/TerminalGateway.Api.Tests.csproj -v minimal
```
