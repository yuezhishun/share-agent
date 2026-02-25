下面给你一份可直接执行的改造线路图，目标是把 `Porta.Pty` 演进到你刚才的 `crosspty-plan` 版本。

## 改造目标
1. 保留 `Porta.Pty` 已有优点（跨平台、ConPTY、native shim）。
2. 补齐你 plan 的目标形态：`IPtyBackend/IPtySession`、统一 native 句柄契约、DemoHost、教程、稳定性验收矩阵。
3. 全程独立项目落地，不影响现有 `pty-agent/nodepty`。

## 关键差距（先锁定）
1. API 形态差距：当前是 `IPtyConnection + Stream`，计划是 `IPtySession + async/chunk`。
2. Native ABI 差距：当前 `pty_spawn/resize/kill/waitpid`，计划是 `pty_create/write/read/resize/terminate/get_exit/close`。
3. 可靠性差距：缺 soak/leak/异常边界的系统化验收。
4. 交付物差距：缺 `DemoHost` 与完整教程体系。

## 实施路线图（8 个阶段）

1. **Phase 0: 冻结规格（1-2 天）**  
产出：`docs/spec-v1.md`，明确 C# API、native ABI、错误码表。  
验收：实现人员不需要二次决策即可编码。

2. **Phase 1: 新 API 外壳并行接入（2-3 天）**  
改动：新增 `CrossPty` 命名空间与 `IPtyBackend/IPtySession/PtyStartOptions/PtyException`。  
策略：保留现有 `PtyProvider`，先做 adapter（旧接口转新接口）。  
验收：旧测试继续通过，新 API 可跑最小启动-读写-退出链路。

3. **Phase 2: 重构 Native ABI（4-6 天）**  
改动：在 `Porta.Pty.Native` 增加句柄式导出函数，不立即删旧函数。  
策略：双 ABI 并存一段时间，C# 新层走新 ABI，旧层继续走旧 ABI。  
验收：Linux/macOS/Windows 都能通过 `create->write/read->terminate->close`。

4. **Phase 3: Linux/macOS 进程路径升级（3-5 天）**  
改动：将 Unix 路径从 `forkpty` 评估迁移为 `openpty + posix_spawn`（符合你的 plan）。  
补充：明确 `EIO -> EOF`、`ESRCH -> 幂等成功` 处理。  
验收：边界测试稳定，不再把这些情况当未处理错误。

5. **Phase 4: 会话生命周期状态机（2-3 天）**  
改动：`Running/Exiting/Exited/Disposed` 明确状态与原子切换。  
补充：`TerminateAsync` 支持 soft->timeout->hard。  
验收：重复 terminate/dispose 不抛异常，行为一致。

6. **Phase 5: DemoHost + 交互示例（2-4 天）**  
新增：`CrossPty.DemoHost`。  
命令：`run-shell`、`interactive-chat`、`resize-watch`、`graceful-terminate`。  
验收：3 平台可演示基本交互与 resize/退出路径。

7. **Phase 6: 测试矩阵补齐（4-6 天）**  
新增：`CrossPty.Tests` + `CrossPty.IntegrationTests` + CI matrix。  
覆盖：参数校验、错误映射、异常边界、5 分钟压力、100 次 spawn/terminate 泄漏检查。  
验收：CI 全绿，失败可定位到平台和场景。

8. **Phase 7: 文档与发布（2-3 天）**  
新增：`quickstart.md`、`interactive-examples.md`、`architecture.md`、`troubleshooting.md`。  
发布：NuGet 预览版（含 native 资产打包说明）。  
验收：干净环境按文档可跑通。

## 里程碑建议（现实排期）
1. M1（第 1 周末）：Phase 0-2 完成，新 API 可用。  
2. M2（第 2 周末）：Phase 3-5 完成，Demo 可演示。  
3. M3（第 3 周末）：Phase 6-7 完成，可发预览版。

## 风险与控制
1. `posix_spawn` 迁移风险：先双路径开关，保留 `forkpty` 作为临时回退。  
2. ABI 破坏风险：先并存旧 ABI，等新 API 稳定后再弃用。  
3. 跨平台一致性风险：所有行为定义以集成测试断言为准，不靠文档约定。

如果你愿意，我下一步可以直接给出 `spec-v1.md` 的完整初稿（含 C# 接口签名和 native 函数签名），你可以直接发给实现同学开工。
