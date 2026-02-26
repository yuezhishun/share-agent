## XTerm.Net 测试 Oracle 接入计划（仅测试层，生产零侵入）

### Summary
将 `XTerm.Net` 作为 .NET 测试工程的“参考终端状态机（oracle）”，用于校验服务端快照/状态语义；不进入 `TerminalGateway.Api` 运行时路径。  
首期只覆盖后端单元与少量集成断言，采用结构化宽松匹配，保证稳定性与可维护性。  
依赖策略：NuGet 固定版本；并同步推进 `Porta.Pty` 使用 NuGet 版本（不再依赖本地文件形态）。

### Scope
- In scope:
  - `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests` 新增 oracle 测试基础设施和用例。
  - 对 `TerminalStateBuffer` 与 `term.snapshot` 关键语义做 oracle 对照验证。
  - 在现有 `GatewayApiTests.cs` 增加 2-3 个“协议输出 vs oracle 状态”集成断言点。
- Out of scope:
  - 不改生产服务端协议与运行时状态计算路径。
  - 不在前端 Playwright 引入 oracle。
  - 不做全量 ANSI/TUI 像素级一致性。

### Public API / Interface Changes
- 外部 API：**无变化**（`term.snapshot/term.patch/term.history.chunk/term.route` 保持不变）。
- 测试工程内部新增：
  - `Oracle/XTermOracleAdapter.cs`：封装 `XTerm.Net` 无头实例输入与状态导出。
  - `Oracle/TerminalFrameNormalizer.cs`：将实际快照与 oracle 状态归一化到统一结构。
  - `Oracle/TerminalOracleAssert.cs`：宽松结构化断言（文本行、尺寸、光标）。
  - `TerminalStateBufferOracleTests.cs`：单元级对照测试。
  - `SnapshotOracleConsistencyTests.cs`：集成级快照一致性测试。

### Implementation Plan
1. **测试依赖与工程准备**
- 在 `TerminalGateway.Api.Tests.csproj` 增加 `XTerm.Net` NuGet 固定版本引用。
- 新增 `Directory.Packages.props`（若仓库未统一包管理）集中锁版本；否则按现有方式固定版本。
- 新增测试标签约定：
  - `[Trait("Category", "oracle")]` 用于后续 CI 分层。

2. **Oracle 适配层（仅测试项目）**
- `XTermOracleAdapter` 提供统一接口：
  - `Feed(string chunk)`
  - `Resize(int cols, int rows)`
  - `Export()` -> `OracleFrame { Cols, Rows, CursorX, CursorY, VisibleLines }`
- 适配器内部屏蔽 `XTerm.Net` 细节，保证后续可替换性。

3. **归一化与断言策略（结构化宽松）**
- 归一化规则固定：
  - 文本：去除行尾空白、统一 `\r\n` 为 `\n`、过滤不可见控制字符。
  - 尺寸：直接比较 `cols/rows`。
  - 光标：允许 `x/y` 在等价边界（如行尾 clamp）内比较。
- 断言优先级：
  - P0：可见行文本一致（宽松归一化后）
  - P1：光标位置一致或等价
  - P2：尺寸一致

4. **单元测试落地（优先）**
- 新增 `TerminalStateBufferOracleTests.cs` 用例矩阵：
  - `\r` 覆盖写
  - `\b` 回退
  - ANSI CSI/OSC 常见序列
  - 混合 chunk 边界（分片输入）
  - resize 前后文本与光标稳定性
- 每个用例执行：
  - 同一输入喂给 `TerminalStateBuffer` 与 `XTermOracleAdapter`
  - 对比归一化结果并输出可读 diff。

5. **集成测试补点（GatewayApiTests）**
- 在现有 SignalR 流程里增加 2-3 个 oracle 校验场景：
  - `JoinInstance + RequestSync(screen)` 后 snapshot 与 oracle 可见状态一致。
  - `RequestResize` 后 ack 尺寸与后续 snapshot 尺寸一致，并与 oracle 尺寸一致。
  - `seq_gap` 补偿后最终 snapshot 与 oracle 收敛一致（1s 内）。

6. **CI 分层**
- PR 快测：运行非 oracle + oracle smoke（1-2 个关键 oracle 用例）。
- 夜间全量：运行全部 `[Category=oracle]`。
- 失败输出要求：打印“实际归一化帧 vs oracle 归一化帧”差异摘要（前 20 行）。

7. **Porta.Pty NuGet 化协同**
- 与本计划并行，记录一条约束：`Porta.Pty` 依赖改为 NuGet 固定版本，不再依赖本地二进制布局。
- 验证项：测试与运行环境均可通过纯依赖还原启动，无本地手工拷贝步骤。

### Test Cases & Scenarios
- 单元：
  - `CarriageReturn_ShouldMatchOracleState`
  - `BackspaceSequence_ShouldMatchOracleState`
  - `AnsiControlSequence_ShouldMatchOracleState`
  - `ChunkBoundarySplit_ShouldRemainConsistent`
  - `ResizeThenWrite_ShouldKeepStableFrame`
- 集成：
  - `RequestSyncSnapshot_ShouldConvergeWithOracle`
  - `ResizeAckAndSnapshot_ShouldMatchOracleSize`
  - `SeqGapResync_ShouldConvergeWithinOneSecond`

### Acceptance Criteria
- Oracle 单元测试通过率 100%。
- Oracle 集成测试稳定通过，连续 30 次重跑无 flaky。
- 生产工程（`TerminalGateway.Api`）无 `XTerm.Net` 直接依赖。
- PR 流水线时长增量控制在可接受区间（目标 < +2 分钟，超出则仅保留 smoke 于 PR）。

### Assumptions & Defaults
- 默认采用 NuGet 固定版本管理 `XTerm.Net` 与 `Porta.Pty`。
- 默认 oracle 只在测试工程引入，不修改生产协议与事件名。
- 默认首期不做前端 oracle 对接，后续按稳定性数据再决定扩展。
- 默认以“结构化宽松匹配”为统一断言标准，避免伪失败。
