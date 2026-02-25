# terminal-gateway-dotnet 单项目实现方案

## 1. 方案结论
采用单项目实现，不拆多个 `.csproj`。

目标目录：`apps/terminal-gateway-dotnet/TerminalGateway.Api`

技术栈：`.NET 8 + ASP.NET Core Minimal API + 原生 WebSocket + Porta.Pty`

## 2. 单项目目录结构
```text
apps/terminal-gateway-dotnet/
  TerminalGateway.Api/
    TerminalGateway.Api.csproj
    Program.cs
    appsettings.json
    Endpoints/
      HealthEndpoints.cs
      SessionEndpoints.cs
      InternalSessionEndpoints.cs
      ProfileEndpoints.cs
      SettingsEndpoints.cs
      FsEndpoints.cs
      ProjectEndpoints.cs
      TerminalWebSocketEndpoint.cs
    Services/
      SessionManager.cs
      SessionReplayBuffer.cs
      ProfileService.cs
      SettingsService.cs
      FsBrowserService.cs
      ProjectDiscoveryService.cs
      WriteTokenService.cs
    Pty/
      IPtyEngine.cs
      PortaPtyEngine.cs
      PtyRuntimeSession.cs
    Models/
      Requests.cs
      Responses.cs
      SessionRecord.cs
      ProfileRecord.cs
      WsMessages.cs
    Infrastructure/
      GatewayOptions.cs
      ValidationHelpers.cs
      JsonOptionsSetup.cs
      TimeProvider.cs
```

## 3. 核心实现模块

### 3.1 Program 与配置
1. 读取环境变量并绑定 `GatewayOptions`。
2. 注册所有 singleton 服务。
3. 开启 WebSocket。
4. 注册 HTTP 路由与 WS 路由。

### 3.2 SessionManager（核心）
1. 会话字典：`ConcurrentDictionary<string, SessionRecord>`。
2. 创建会话：解析 launch options -> 调用 `IPtyEngine.CreateAsync`。
3. 管理订阅者集合与 writer 锁。
4. 维护 replay buffer（序号、裁剪、history）。
5. 处理会话退出广播。

### 3.3 PortaPtyEngine
1. 封装 `PtyProvider.SpawnAsync`。
2. 暴露统一方法：
1. `CreateAsync`
2. `WriteAsync`
3. `ResizeAsync`
4. `TerminateAsync`
5. `DisposeAsync`
3. 把 `ReaderStream` 转为异步后台读取循环。
4. 把 `ProcessExited` 事件转发给 `SessionManager`。

### 3.4 Replay Buffer
1. 数据结构：`List<OutputChunk>` + byte 计数器。
2. 超限裁剪：从头丢弃，更新 `headSeq`。
3. `Snapshot(limitBytes)`。
4. `History(beforeSeq, limitBytes)`。
5. `CollectDelta(sinceSeq)`（支持 `truncatedSince`）。

### 3.5 WebSocket Endpoint
1. 解析 query：`sessionId/replay/replayMode/sinceSeq/writeToken`。
2. attach 时先发 `ready`。
3. replay 模式按参数回放。
4. 处理输入消息：`input/resize/ping`。
5. close 时 detach。

### 3.6 Profiles / Settings / FS / Projects
1. Profile CRUD（含 builtin + custom）。
2. Settings 存储（quick commands + fs allowed roots）。
3. `/fs/dirs` 路径安全校验。
4. `/projects/discover` 扫描 codex/claude 配置路径。

## 4. 请求/响应与字段对齐原则
1. 字段名尽量与现有 Node 接口保持一致（含大小写与命名）。
2. 对外响应不暴露 `writeTokenHash`。
3. public create 返回 `writeToken`，list/status 不返回。
4. internal 接口统一校验 `X-Internal-Token`。

## 5. 实施阶段（建议 5 阶段）
1. Phase A：搭建项目骨架 + health + internal create/status + ws ready/output。
2. Phase B：write/input/resize/terminate + exited 生命周期。
3. Phase C：replay buffer（snapshot/history/delta/truncatedSince）。
4. Phase D：profiles/settings/fs/projects + public sessions 全量接口。
5. Phase E：测试补齐、对等联调、文档完善。

## 6. 测试策略
1. 新建 `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`。
2. 优先迁移 Node 版 `gateway.test.js` 的核心用例语义。
3. 至少覆盖：
1. 会话创建与输出
2. WS 重连
3. token 可写权限
4. replay/snapshot/history
5. profile/settings 持久化
6. terminate/remove/prune

## 7. 风险与处理
1. `Porta.Pty` 的 `Kill()` 非 signal 化：在 API 层保留 signal 参数，内部做兼容映射。
2. 读流阻塞：必须使用后台任务 + cancellation，避免阻塞请求线程。
3. 大输出内存压力：严格执行 buffer 上限裁剪。
4. 单写者一致性：writer peer 切换必须原子化。

## 8. 与现有系统协同
1. 当前 Node 版 `apps/terminal-gateway` 保留。
2. 新服务默认仅并行部署联调。
3. orchestrator 配置暂不切换默认后端。
4. 联调通过后再决定是否灰度替换。

## 9. 交付物
1. 单项目服务源码（`TerminalGateway.Api`）。
2. 测试项目（`TerminalGateway.Api.Tests`）。
3. 本方案文档 + 对等实现清单文档。
