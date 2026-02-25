# terminal-gateway-dotnet 运行与测试说明

## 1. 目录
- `apps/terminal-gateway-dotnet/TerminalGateway.Api`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`

## 2. 本地运行
```bash
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

默认监听：
- `HOST=0.0.0.0`
- `PORT=7300`

## 3. 测试
```bash
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/TerminalGateway.Api.Tests.csproj -v minimal
```

说明：
- WebSocket 用例已切换为 Kestrel 真 socket 集成测试（不再依赖 TestServer WebSocket 时序）。
- 核心能力（会话创建、WS 输出、ping/pong、writeToken 写权限、snapshot/history、profiles/settings、fs/projects、internal 鉴权）已覆盖并通过。

## 4. 环境变量
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
- `TERMINAL_FS_ALLOWED_ROOTS`

## 5. 联调状态
- Node 网关 `apps/terminal-gateway` 继续保留并作为默认后端。
- Dotnet 网关用于对等联调和逐步替换验证。
