# terminal-gateway-dotnet 运行与测试说明

## 1. 目录
- `apps/terminal-gateway-dotnet/TerminalGateway.Api`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`

## 2. 当前协议与入口
- HTTP API：`/api/*`
- SignalR Hub：`/hubs/terminal`
- 说明：当前 Web 终端同步链路已使用 SignalR；旧 WebSocket 设计文档已标注为 `[已废弃]`。

## 3. 本地运行
```bash
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

默认监听：
- `HOST=0.0.0.0`
- `PORT=7300`

## 4. 测试
```bash
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/TerminalGateway.Api.Tests.csproj -v minimal
```

当前测试覆盖（SignalR 路径）：
- 健康检查与项目发现
- 创建实例 + Hub 连接
- 输入回显（SendInput）
- Resize ACK + Snapshot 同步（RequestResize）
- 手动同步（RequestSync: screen/history）
- 文件接口与实例退出回收

## 5. 环境变量
与 Node 网关对齐的主要变量：
- `PORT`
- `HOST`
- `TERMINAL_GATEWAY_TOKEN`
- `TERMINAL_WS_TOKEN`
- `TERMINAL_PROFILE_STORE_FILE`
- `TERMINAL_SETTINGS_STORE_FILE`
- `TERMINAL_MAX_OUTPUT_BUFFER_BYTES`
- `TERMINAL_CODEX_CONFIG_PATH`
- `TERMINAL_CLAUDE_CONFIG_PATH`
- `TERMINAL_PATH_PREFIXES`（逗号分隔；用于补齐 PTY 子进程 `PATH` 前缀，如 Node/Codex 安装目录）
- `TERMINAL_FS_ALLOWED_ROOTS`

## 6. 联调状态
- 当前 MVP 以 Dotnet 网关 + Web 前端为主链路。
- Web 终端通过 SignalR Hub `/hubs/terminal` 与网关交互。
- Node 网关属于历史实现，可按需保留，不在 MVP 主链路内。
