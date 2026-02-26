# [已废弃] crosspty-plan

> 状态：已废弃
>
> 废弃日期：2026-02-25
>
> 原因：文档中的协议、架构或实现路径与当前仓库代码差异过大。
>
> 当前实现以 apps 下源码为准：前端与 dotnet gateway 已切换到 SignalR Hub /hubs/terminal。
>
> 建议参考：README.md、docs/terminal-gateway-dotnet.md、docs/nginx-config-paths.md。

# C# 跨平台 PTY 封装新项目方案（独立于当前 pty-agent）

## Summary
目标是新建一个独立项目 `CrossPty`，提供 `NuGet SDK + Demo Host + 完整教程/交互示例`。  
不修改当前 `pty-agent` 的默认后端，不替换 `nodepty`，零发布风险接入现有系统。

## 1. Project Scope
1. 新仓库（建议名：`crosspty`），不改动现有 monorepo 运行路径。
2. 交付物包含：
   1. `CrossPty`（C# SDK，NuGet 包）
   2. `CrossPty.Native`（平台原生 shim：Windows/Linux/macOS）
   3. `CrossPty.DemoHost`（交互示例程序）
   4. `docs/`（快速开始、API 教程、故障排查、交互示例）
3. 非目标：
   1. 不替换当前 `nodepty`
   2. 不变更 `pty-agent` 线上配置
   3. 不承诺首版支持所有 shell 特性（先聚焦稳定 I/O 与生命周期）

## 2. Architecture Decisions
1. 采用 `C# + Native Shim`。
2. Linux/macOS：`openpty + posix_spawn`（避免在托管进程里执行 `fork` 后托管逻辑）。
3. Windows：ConPTY（`CreatePseudoConsole` 路径）。
4. C# 层仅做：
   1. 统一 API
   2. 安全句柄管理（`SafeHandle`）
   3. 异步读写与事件分发
   4. 退出与资源回收状态机
5. 设计原则：
   1. 输出以 chunk 为主，SDK 提供可选行切分器
   2. 边界错误标准化（EIO/ESRCH/EOF/PTY closed）
   3. 所有终止路径幂等（多次 terminate/dispose 不抛未处理异常）

## 3. Public APIs / Interfaces / Types
1. `IPtyBackend`  
   `Task<IPtySession> StartAsync(PtyStartOptions options, CancellationToken ct)`
2. `IPtySession : IAsyncDisposable`
   1. `int Pid`
   2. `string PlatformBackend`（`conpty|posix`）
   3. `Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)`
   4. `Task ResizeAsync(int cols, int rows, CancellationToken ct)`
   5. `Task<TerminateResult> TerminateAsync(TerminateSignal signal, CancellationToken ct)`
   6. `IAsyncEnumerable<PtyOutputChunk> ReadOutputAsync(CancellationToken ct)`
   7. `event EventHandler<PtyExitedEventArgs> Exited`
3. `PtyStartOptions`
   1. `string FileName`
   2. `IReadOnlyList<string> Args`
   3. `string? WorkingDirectory`
   4. `IReadOnlyDictionary<string,string?> Environment`
   5. `int Cols`（default 160）
   6. `int Rows`（default 40）
   7. `bool MergeStdErr`（default true）
4. `PtyException : Exception`
   1. `PtyErrorCode`（`SpawnFailed|ExecFailed|PtyClosed|InvalidResize|PermissionDenied|Unknown`）
   2. `int? NativeError`
5. `CrossPtyTutorial` 示例 API（Demo 层）
   1. `run-shell`
   2. `interactive-chat`
   3. `resize-watch`
   4. `graceful-terminate`

## 4. Native Shim Contract
1. 导出函数（每平台一致）：
   1. `pty_create(pty_start_spec*, pty_handle**)`
   2. `pty_write(pty_handle*, uint8_t*, size_t, size_t* written)`
   3. `pty_read(pty_handle*, uint8_t*, size_t, size_t* read)`
   4. `pty_resize(pty_handle*, int cols, int rows)`
   5. `pty_terminate(pty_handle*, int signal_like)`
   6. `pty_get_exit(pty_handle*, int* has_exit, int* code)`
   7. `pty_close(pty_handle*)`
   8. `pty_last_error(pty_error*)`
2. 错误模型：
   1. native 永不抛跨边界异常，只返回错误码
   2. C# 统一映射为 `PtyException`
3. Linux/macOS 特别处理：
   1. `read -> EIO` 且子进程已退出时，转为 EOF 语义
   2. `kill -> ESRCH` 视为已终止成功（幂等）

## 5. Data Flow
1. `StartAsync`：
   1. 校验参数
   2. 调 native `pty_create`
   3. 返回 `PtySession` 并启动后台读取循环
2. `ReadOutputAsync`：
   1. raw chunk 推送 channel
   2. 可选 `LineReader` 扩展做增量切行
3. `TerminateAsync`：
   1. 先 soft（SIGTERM/CTRL_BREAK）
   2. 超时后 hard（SIGKILL/TerminateProcess）
   3. 最终发布 `Exited` 事件
4. `DisposeAsync`：
   1. 停止读取循环
   2. 安全关闭句柄
   3. 二次调用无副作用

## 6. Repo Layout
1. `src/CrossPty/`（SDK）
2. `src/CrossPty.Native/`
   1. `windows/`（ConPTY）
   2. `linux/`（openpty+posix_spawn）
   3. `macos/`（openpty+posix_spawn）
3. `src/CrossPty.DemoHost/`
4. `tests/CrossPty.Tests/`（托管单测）
5. `tests/CrossPty.IntegrationTests/`（跨平台集成）
6. `docs/`
   1. `quickstart.md`
   2. `interactive-examples.md`
   3. `architecture.md`
   4. `troubleshooting.md`

## 7. Testing & Acceptance
1. 单元测试：
   1. 参数校验
   2. 错误码映射
   3. terminate/dispose 幂等
   4. chunk->line 增量切分
2. 集成测试（Linux/macOS/Windows）：
   1. 启动 shell 并回显命令
   2. 大输出压力（持续 5 分钟）
   3. resize 生效（`stty size`/等价校验）
   4. 子进程自然退出事件
   5. 强制终止路径
   6. 关闭边界（EIO/ESRCH）不冒泡为未处理异常
3. 验收标准：
   1. 24h soak test 无崩溃
   2. 100 次连续 spawn/terminate 无句柄泄漏
   3. Windows + Linux 两端交互示例可复现
   4. 教程按步骤可在干净机器跑通

## 8. Rollout Plan
1. Phase A：接口与错误模型冻结（先不实现全部 native）
2. Phase B：Linux native shim + SDK 打通
3. Phase C：Windows ConPTY 接入
4. Phase D：macOS 接入与统一 CI
5. Phase E：DemoHost + 教程 + 发布 NuGet 预览版
6. Phase F：可选提供 `pty-agent` 适配示例（仅文档示例，不改默认后端）

## 9. Tutorial & Interactive Examples（你要求的完整教程）
1. Quickstart：
   1. 安装 NuGet
   2. 启动 `bash/pwsh`
   3. 发送输入并读输出
2. 交互示例：
   1. 实时输入透传（键盘 -> PTY）
   2. 动态窗口 resize
   3. 优雅退出与强制退出
3. 故障示例：
   1. 可执行文件不存在
   2. 工作目录无权限
   3. 进程提前退出
4. 每个示例提供：
   1. 最小可运行代码
   2. 预期输出
   3. 常见错误与修复

## 10. Assumptions & Defaults
1. 默认目标框架：`.NET 8`（LTS）。
2. 默认编码：UTF-8。
3. 默认 shell：
   1. Linux/macOS：`/bin/bash -l`
   2. Windows：`pwsh -NoLogo`
4. 首版不支持：
   1. GPU/图形程序
   2. OSC 高级特性完整仿真
   3. 复杂终端复用（tmux-like）内置能力
5. 当前 `pty-agent` 保持现状，`nodepty` 不受影响。
