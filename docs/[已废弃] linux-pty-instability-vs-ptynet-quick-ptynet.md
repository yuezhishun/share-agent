# [已废弃] linux-pty-instability-vs-ptynet-quick-ptynet

> 状态：已废弃
>
> 废弃日期：2026-02-25
>
> 原因：文档中的协议、架构或实现路径与当前仓库代码差异过大。
>
> 当前实现以 apps 下源码为准：前端与 dotnet gateway 已切换到 SignalR Hub /hubs/terminal。
>
> 建议参考：README.md、docs/terminal-gateway-dotnet.md、docs/nginx-config-paths.md。

# vs-pty.net 与 Quick.PtyNet 在 Linux 下不稳定原因总结

## 背景
本项目在 Linux 环境评估 .NET 伪终端方案时，分别验证了：
1. `microsoft/vs-pty.net`（引入其 `src/Pty.Net` 源码）
2. `Quick.PtyNet`（NuGet + GitHub）

目标是用于多会话、长生命周期、可交互的 CLI Worker 运行时。

## 结论
两者在 Linux 下都存在生产级稳定性风险，核心问题是同源的：
- `forkpty` 路径与 CLR 进程模型耦合过深
- 子进程分支仍执行托管代码
- Unix PTY 边界状态（EIO/进程已退出）处理不完整

因此不建议作为本项目 Linux 默认终端后端。

## 主要不稳定原因

### 1. forkpty 后子进程仍运行托管代码（高风险根因）
Linux 路径均采用 `forkpty`。在 `pid == 0` 分支里，仍有 C# 逻辑（例如：设置当前目录、处理环境变量）后再 `execvp`。

这在多线程 CLR 进程里风险很高：
- `fork` 后只保留调用线程，托管运行时状态可能不一致
- 若 `exec` 前触发托管路径，容易出现 native 层异常甚至崩溃

本会话最小复现中出现直接 `SIGSEGV`（退出码 139），符合该风险模型。

### 2. 对 .NET 运行时行为存在敏感依赖（Quick.PtyNet）
`Quick.PtyNet` 在 Linux/.NET 7+ 环境下要求显式设置：
- `DOTNET_EnableWriteXorExecute=0`

未设置会直接抛异常拒绝启动。该要求说明其稳定运行依赖特定运行时开关，不具备“默认稳态”特征。

### 3. Unix PTY 关闭边界处理不完善
实际运行中出现：
1. 读流阶段 `System.IO.IOException: Input/output error`
- 常见于 PTY slave 关闭后 master read 返回 EIO
- 库层未完整吞并/转义为 EOF 语义

2. 释放阶段 `Killing terminal failed with error 3`
- 对应子进程已退出（ESRCH）仍执行 kill
- Dispose 过程抛异常，影响上层稳定性

### 4. Quick.PtyNet 与 vs-pty.net 同源实现路径
`Quick.PtyNet` 是从同类实现 fork，Linux 核心路径仍是同一模式：
- `forkpty + 托管子进程分支 + execvp`

因此其问题不是 isolated bug，而是架构路径级问题，稳定性特征与 `vs-pty.net` 同类。

## 本会话观测到的具体现象
1. 使用 `vs-pty.net` Linux 路径时，最小复现程序在 `SpawnAsync` 后进程直接崩溃（139）。
2. 使用 `Quick.PtyNet`：
- 未设 `DOTNET_EnableWriteXorExecute=0` 时直接异常
- 设定后可跑出输出，但在读流结束/Dispose 阶段仍出现异常

## 对本项目的影响
本项目目标包含：
- 多 CLI 会话并发
- 长任务执行
- 实时交互
- 可恢复与可观测

上述特性要求终端层具备稳定的长期运行行为。当前两者在 Linux 下均不满足该标准。

## 架构决策建议
1. Linux 默认后端改为 `node-pty` sidecar（独立 Node 进程管理 PTY）。
2. .NET Orchestrator 保持编排与状态管理，不直接承担 Linux PTY native 风险。
3. `.NET process backend` 保留为兜底路径。
4. `vs-pty.net/Quick.PtyNet` 仅用于实验或 Windows 特定场景，不作为 Linux 主路径。

## 备注
该结论基于本会话中的源码检查、最小复现与集成验证结果；后续若上游库对 Linux 实现做了结构性改造（例如将 fork/exec 下沉到纯 native shim），可重新评估。
