# Web 终端全链路稳定性测试补强计划

## 1. 目标与范围

### 1.1 目标
- 建立覆盖 Web 终端主要使用场景的自动化测试体系，显著降低回归风险与线上故障率。
- 将“协议正确性 + 前端交互稳定性 + 主从路由一致性”纳入同一套可持续执行的验证框架。

### 1.2 覆盖范围
- 前端：`apps/secretary-web`（Playwright E2E + mocked 交互验证）。
- 后端：`apps/terminal-gateway-dotnet/TerminalGateway.Api`（xUnit 单元/集成）。
- 链路：SignalR `TerminalHub`、Cluster `ClusterHub`、主从事件转发、重连与重同步。

### 1.3 不在本期范围
- 像素级渲染一致性（不做 screenshot diff 基线）。
- 全量 ANSI 控制码穷举验证（改为高频子集 + 回归增量扩展）。

## 2. 对标来源与迁移原则

### 2.1 对标开源项目
- xterm.js Playwright 集成测试（输入处理、解析器、鼠标、字符宽度、模式与事件）  
  https://github.com/xtermjs/xterm.js/tree/master/test/playwright
- webssh2 测试体系（认证、重连、terminal size replay、控制通道、背压）  
  https://github.com/billchurch/webssh2/tree/master/tests

### 2.2 迁移原则
- 不直接复制上游用例代码，只迁移“场景模型 + 断言模式”。
- 所有用例以当前仓库协议为准：`term.snapshot/term.patch/term.history.chunk/term.route/term.resize.ack`。
- 先覆盖真实业务高频路径，再向边缘协议扩展。

## 3. 测试分层设计

### 3.1 L1：前端 Mock E2E（快）
- 目的：验证 UI 状态机、按钮行为、重连回调、输入/粘贴/焦点逻辑。
- 现有文件：`apps/secretary-web/e2e/app.spec.js`
- 新增文件：
  1. `apps/secretary-web/e2e/terminal-reconnect.spec.js`
  2. `apps/secretary-web/e2e/terminal-shortcuts.spec.js`
  3. `apps/secretary-web/e2e/terminal-resize-scroll.spec.js`
  4. `apps/secretary-web/e2e/terminal-unicode-width.spec.js`

### 3.2 L2：前端 Integration E2E（中）
- 目的：验证真实网关联通、实例生命周期、主从路由、同步一致性。
- 现有文件：
  - `apps/secretary-web/e2e/integration.spec.js`
  - `apps/secretary-web/e2e/cluster.spec.js`
- 扩展方向：
  1. 重连后自动同步与继续输入
  2. seq gap 触发自动 resync
  3. resize 与 snapshot/ack 尺寸一致性

### 3.3 L3：后端单元 + 集成（稳）
- 目的：验证协议语义、序号/历史游标、主从 dedup/gap、错误处理、背压策略。
- 文件：
  - 扩展 `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/GatewayApiTests.cs`
  - 新增
    1. `TerminalStateBufferTests.cs`
    2. `GatewaySyncConsistencyTests.cs`
    3. `GatewayBackpressureAndOrderTests.cs`

## 4. 场景矩阵与验收断言

### 4.1 重连场景（高优先级）
1. `onreconnecting -> onreconnected` 流程
- 断言：状态显示 `Reconnecting...` 后恢复为 `Connected: {instanceId}`。
- 断言：自动调用 `JoinInstance` 与 `RequestSync(screen)`。

2. 重连后继续输入
- 操作：重连成功后执行 `echo reconnect-ok`。
- 断言：终端输出包含 `reconnect-ok`。

3. 重连期间切换实例
- 断言：仅最终实例收到后续 snapshot/patch。

4. 重连后的缺口自愈
- 操作：注入跳号 `term.patch`。
- 断言：触发 `RequestSync(screen)`，状态显示 resync 触发信息。

5. 重连 + resize
- 断言：`term.resize.ack.size` 与后续 `term.snapshot.size` 完全一致。

### 4.2 快捷按钮场景（高优先级）
1. 键值映射正确
- `Esc/Tab/Enter/Ctrl+C/↑/↓/←/→` 必须映射为预期控制序列。

2. 焦点回归
- 每次点击快捷按钮后 `document.activeElement` 必须是 `.xterm-helper-textarea`。

3. 连点稳定性
- 每个按钮连续点击 20 次，输入无丢失、无乱序、无报错。

4. 快捷按钮 + 粘贴混合
- 断言 bracketed paste 仍正确包裹发送。

### 4.3 连接按钮状态与文案（新增强制项）
1. 重连中按钮状态
- 桌面与移动端 `Connect` 在重连中应禁用，避免无效点击。

2. 重连中文案
- 状态文案必须显示 `Reconnecting...`。

3. 重连后恢复
- `Connect` 重新可用，文案恢复 `Connect`，状态回到 `Connected: {id}`。

4. 断连后行为
- 若最终 close 且未恢复，状态需明确（如 `connect failed` 或 `disconnected`），禁止误报已连接。

### 4.4 Resize 与滚动场景
1. resize 后立即滚动
- 断言：不崩溃、不空白、不异常跳回底部。

2. 连续 resize 防抖
- 断言：服务端最终只接收防抖后的有效尺寸。

3. 远端实例 resize
- 断言：主从链路 ack/snapshot 尺寸一致。

### 4.5 同步一致性场景
1. `snapshot -> patch` 连续渲染无倒退。
2. history chunk 游标 `before/next_before/exhausted` 正确推进。
3. `term.route(reason=seq_gap)` 必须触发自动补偿。

### 4.6 TUI 兼容基线场景
1. 进入/退出 alternate buffer（`vim`/`less`）后可恢复命令行。
2. `top` 类持续刷新时保持可交互。
3. TUI 期间 resize 不导致会话异常终止。

### 4.7 Unicode/宽字符场景
1. ASCII、全角、组合字符、代理对宽度正确。
2. 宽字符换行与 patch 增量更新不出现错位。

## 5. 后端测试重点

### 5.1 TerminalStateBuffer
- `\r` 覆盖写、`\b` 回退、ANSI 过滤、混合序列解析。

### 5.2 序号与同步语义
- `RequestSync(screen)` 不应制造无意义 seq 漂移。
- gap 识别与去重行为准确（本地/集群）。

### 5.3 主从一致性
- `ClusterHub` 去重与 gap 通知可验证。
- 远端 input/resize/sync/terminate/upload 代理正确。

### 5.4 稳定性
- 高吞吐输出下不死锁，背压后可恢复。

## 6. CI 执行策略（快慢分层）

### 6.1 PR 快测（目标 <= 8 分钟）
- 前端：核心 mocked + integration 子集
  1. 重连基础链路
  2. 快捷按钮映射与焦点
  3. resize + resync 核心路径
- 后端：单元 + 核心集成（不跑长时压力）

### 6.2 夜间全量
- 全部 E2E（含 TUI 基线、unicode、连点压力、主从完整链路）。
- 后端压力与背压场景。
- 输出 flaky 报表并归档。

## 7. 实施里程碑

### M1（第 1 周）
- 新增重连与快捷按钮测试文件骨架。
- 补充连接按钮状态/文案断言。

### M2（第 2 周）
- 打通 resize/滚动、sync/gap、自愈场景。
- 后端新增 `TerminalStateBuffer` 与同步一致性测试。

### M3（第 3 周）
- TUI 基线与 unicode 覆盖。
- 集群长链路与异常恢复补齐。

### M4（第 4 周）
- 背压/高吞吐稳定性验证。
- CI 分层收口、慢测稳定性治理。

## 8. 交付清单
- 新增/扩展测试文件（前端 + 后端）。
- 场景矩阵清单（本文件）。
- PR 快测与夜间全量命令说明。
- flaky 清单与已修复回归记录模板。

## 9. 完成标准
1. 核心链路（创建/连接/输入/resize/resync/重连/终止）自动化全绿。
2. 新增重连与快捷按钮用例在连续 30 次重跑下无 flaky。
3. 重连后 1 秒内恢复可操作，且连接按钮状态/文案与连接状态一致。
4. 关键回归问题均有对应自动化用例绑定。

## 10. 默认约束与假设
- 浏览器优先 Chromium，Firefox/WebKit 纳入夜间任务。
- 测试环境可启用可测性增强开关，生产环境保持关闭。
- 允许先以结构化断言替代视觉快照，后续按需要引入截图回归。
