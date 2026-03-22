# terminal-gateway 进程执行能力说明

## 1. 适用范围

本文档说明当前仓库中“命令执行 / 托管进程”这部分能力的实际落点，而不是独立的通用 `ProcessRunner` 组件文档。

当前实现位于：

- `apps/terminal-gateway-dotnet/TerminalGateway.Api/ProcessRunner`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api/Services/ProcessApiService.cs`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api/Endpoints/ProcessEndpoints.cs`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/ProcessRunnerTests.cs`

这部分能力已经内嵌到 `terminal-gateway-dotnet` 中，对外主要通过 HTTP API 提供。

## 2. 能力概览

当前项目里的进程执行能力分成两类：

- 同步执行：提交一个命令，等待执行完成后直接返回结果
- 托管执行：先启动进程，再通过查询、等待、停止等接口管理其生命周期

支持的关键特性：

- 单命令执行
- 管道命令执行
- 设置工作目录、环境变量、标准输入、超时
- 保留标准输出与标准错误
- 托管进程列表、状态查询、输出历史读取
- 可配置最大并发数

## 3. 运行约束

### 3.1 工作目录限制

`cwd` 会经过服务端校验，必须位于 `FILES_BASE_PATH` 之内；否则接口会返回 403。

这条限制由 `ProcessApiService.ResolveWithinBase()` 实现，用于避免命令在授权目录之外执行。

### 3.2 并发限制

托管进程由 `ProcessManager` 统一管理，最大并发数来自网关配置：

- `TERMINAL_PROCESS_MANAGER_MAX_CONCURRENCY`

服务启动时会确保并发数至少为 1。

## 4. 本地接口

`ProcessEndpoints` 当前注册了以下接口：

- `POST /api/processes/run`
- `POST /api/processes`
- `GET /api/processes`
- `GET /api/processes/{processId}`
- `GET /api/processes/{processId}/output`
- `POST /api/processes/{processId}/wait?timeout_ms=30000`
- `POST /api/processes/{processId}/stop`
- `DELETE /api/processes/{processId}`

接口职责：

- `POST /api/processes/run`：同步执行命令，直接返回结果
- `POST /api/processes`：注册并启动托管进程
- `GET /api/processes`：列出当前托管进程
- `GET /api/processes/{processId}`：读取托管进程详情
- `GET /api/processes/{processId}/output`：读取输出历史
- `POST /api/processes/{processId}/wait`：等待进程结束，可设置超时
- `POST /api/processes/{processId}/stop`：停止进程
- `DELETE /api/processes/{processId}`：从管理器中移除已记录进程

## 5. 请求体

进程接口使用 `RunProcessRequest`。常用字段如下：

```json
{
  "file": "bash",
  "args": ["-lc", "pwd"],
  "cwd": "/home/yueyuan/pty-agent",
  "stdin": "",
  "timeout_ms": 30000,
  "allow_non_zero_exit_code": false,
  "env": {
    "TERM": "xterm-256color"
  },
  "pipeline": [],
  "metadata": {
    "source": "manual"
  }
}
```

字段说明：

- `file`：主命令
- `args`：主命令参数
- `cwd`：工作目录，必须在 `FILES_BASE_PATH` 下
- `stdin`：写入标准输入的文本
- `timeout_ms`：超时时间，单位毫秒
- `allow_non_zero_exit_code`：为 `true` 时允许非零退出码
- `env`：附加环境变量
- `pipeline`：管道中的后续命令数组
- `metadata`：托管进程附带元数据

## 6. 管道执行

如果请求里提供了 `pipeline`，服务端会把 `file + args` 作为第一段命令，再按顺序拼接后续命令，最终形成一条管道链。

示例：

```json
{
  "file": "find",
  "args": [".", "-name", "*.cs"],
  "cwd": "/home/yueyuan/pty-agent",
  "pipeline": [
    {
      "file": "wc",
      "args": ["-l"]
    }
  ]
}
```

## 7. 返回结果

同步执行成功后，返回的核心字段包括：

- `processId`
- `exitCode`
- `standardOutput`
- `standardError`
- `completionTime`
- `durationMs`
- `isSuccess`
- `isTimedOut`
- `command`
- `workingDirectory`

托管进程详情额外包含：

- `status`
- `startTime`
- `endTime`
- `outputCount`
- `metadata`
- `result`

输出历史项包含：

- `timestamp`
- `processId`
- `outputType`
- `content`

## 8. 使用示例

### 8.1 同步执行

```bash
curl -X POST http://127.0.0.1:8080/api/processes/run \
  -H 'Content-Type: application/json' \
  -d '{
    "file": "bash",
    "args": ["-lc", "echo hello from gateway"],
    "cwd": "/home/yueyuan/pty-agent"
  }'
```

### 8.2 启动托管进程

```bash
curl -X POST http://127.0.0.1:8080/api/processes \
  -H 'Content-Type: application/json' \
  -d '{
    "file": "bash",
    "args": ["-lc", "sleep 10 && echo done"],
    "cwd": "/home/yueyuan/pty-agent",
    "metadata": {
      "type": "demo"
    }
  }'
```

### 8.3 等待进程完成

```bash
curl -X POST "http://127.0.0.1:8080/api/processes/{processId}/wait?timeout_ms=5000"
```

### 8.4 停止进程

```bash
curl -X POST http://127.0.0.1:8080/api/processes/{processId}/stop \
  -H 'Content-Type: application/json' \
  -d '{
    "force": false
  }'
```

## 9. 测试建议

涉及该能力的改动，至少应检查：

- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/ProcessRunnerTests.cs`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/InstanceManagerLaunchArgsTests.cs`

建议执行：

```bash
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/TerminalGateway.Api.Tests.csproj -v minimal
```

## 10. 与其他文档的关系

- `docs/terminal-gateway-dotnet.md`：覆盖网关整体运行方式和完整接口面
- 本文档：只聚焦进程执行与托管进程能力
- `docs/terminal-gateway-deploy.md`：覆盖部署与发布，不展开接口细节
