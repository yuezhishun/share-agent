# 终端抗污染与轻量回放实施方案（服务端单一真相 + 条件回放 + 增量演进）

## Summary
目标是同时解决三件事：
1. 降低“终端被污染/显示不一致”的概率。
2. 避免切换会话时每次全量回放（最多 8MB）造成带宽和卡顿。
3. 保持实现复杂度可控，先快速上线，再平滑演进到增量加载。

采用两阶段方案：
- **Phase A（立即可落地）**：服务端为真相源，前端无持久本地历史缓存，基于 `ready` 元数据做**条件 replay**。
- **Phase B（演进）**：引入 `sinceSeq` 增量协议 + 分页历史拉取，支持上滚按需加载。

---

## 1. 目标与验收标准

### 1.1 目标
- 切换会话时默认不全量拉取历史，仅在必要时 replay。
- 重连后内容最终一致，不出现重复堆叠、旧内容残留。
- 出现截断时 UI 明确提示，不误导为“完整历史”。
- 支持低成本定位 ANSI 兼容问题（可选日志开关）。

### 1.2 验收标准
- 会话切换 50 次，默认路径下 replay 触发率 < 20%（仅首次/落后/截断时触发）。
- 断网 10 秒恢复后，终端内容可恢复且无重复块。
- 当 `outputTruncated=true` 时，UI 100% 显示“历史已截断”提示。
- 在 WebGL 异常场景可自动降级 DOM，终端继续可读。
- 全量回放数据量相比“每次切换全量 replay”下降至少 60%（压测样本：5 个活跃会话）。

---

## 2. 设计原则与边界

1. **服务端单一真相**：会话历史以 gateway 内存 `outputBuffer` 为准。  
2. **前端仅保留视图态**：不做长期本地历史镜像（避免双份状态漂移）。  
3. **最终一致优先**：允许短暂空白/重建，不允许长期错乱。  
4. **边界明确**：  
   - 超过 buffer 上限的历史不可恢复。  
   - gateway 重启后历史不可恢复（现有约束）。  

---

## 3. Phase A（立即落地）详细方案

## 3.1 协议与字段（保持兼容）
复用现有字段，不新增破坏性 API：
- `ready` 帧已有：`outputBytes`、`outputTruncated`、`maxOutputBufferBytes`
- `output` 帧已有：`replay=true` 标记
- `GET /sessions/:id/snapshot` 已有：`truncated`、`maxOutputBufferBytes`

无需改动后端接口路径，仅调整前端状态机和触发条件。

## 3.2 前端状态机（`apps/secretary-web`）

### 3.2.1 会话运行态字段
在 store 的 runtime entry（内存态）保留：
- `serverOutputBytesLastReady: number`
- `needsReplayHydration: boolean`
- `replayHydrated: boolean`
- `manualStop: boolean`（已有）
- `connectionStatus`（已有）

### 3.2.2 切换/连接策略
- **on openSession(sessionId)**：
  - 先建立 WS（`replay=0`）。
  - 收到 `ready` 后比较：
    - `outputTruncated=true` -> `needsReplayHydration=true`
    - 首次打开该会话 -> `needsReplayHydration=true`
    - 否则默认 `false`
  - 若 `needsReplayHydration=true`：立即触发一次 `reconnectNow({ replay:true })`

### 3.2.3 replay 去污染规则
- 收到第一个 `msg.type='output' && msg.replay===true` 时：
  - 执行 `term.reset()`
  - 标记 `replayHydrated=true`
  - 再写入 replay 数据
- 后续 `replay=true` 分片仅 append，不再 reset。

### 3.2.4 自动重连策略
- 非手动断线统一 `requestReplay=true`，避免断线窗口数据丢失。
- 若 `manualStop=true`，不自动重连。

### 3.2.5 UI 提示
- 当 `session.outputTruncated=true`：显示“历史已截断，仅保留最近 N MB”
- 当 `reconnecting`：显示轻量状态提示，不阻塞输入区渲染。

## 3.3 渲染稳定性（抗“显示污染”）
- 保持 `ResizeObserver + fit + sendResize`（容器变化也触发）。
- 保持 WebGL context loss 自动降级 DOM。
- 默认 `keepScrollbackOnAltScreen=false`（减少备用屏冲突）。

## 3.4 后端（`apps/terminal-gateway`）
- 保持当前实现：`maxOutputBufferBytes` 可配置，默认 8MB。
- 保持 `outputTruncated`/`outputBytes` 在 `ready/list/snapshot` 透出。
- 不做行为破坏性变更。

---

## 4. Phase B（增量协议）详细方案

## 4.1 新增协议（向后兼容）
### 4.1.1 WS 查询参数
- `sinceSeq`（可选，整数）
- `replayMode`（可选，`none|tail|full`，默认 `none`）

### 4.1.2 WS 消息扩展
- `output` 增加：
  - `seqStart`
  - `seqEnd`
  - `truncatedSince`（当客户端请求的 `sinceSeq` 已被截断时）
- `ready` 增加：
  - `headSeq`
  - `tailSeq`
  - `canDeltaReplay=true|false`

### 4.1.3 新增历史分页接口（可选 HTTP）
- `GET /sessions/:id/history?beforeSeq=...&limitBytes=...`
- 返回倒序窗口，供“上滚加载更多”使用。

## 4.2 服务端数据结构调整
- `outputBuffer` 改成 ring buffer + seq 元信息：
  - `headSeq`, `tailSeq`
  - chunk 列表（每块带 seq 区间）
- `appendOutputBuffer` 返回新增 seq 区间和截断信息。

## 4.3 前端加载策略
- 激活会话：`replayMode=none` + `sinceSeq=lastSeenSeq`
- 若收到 `truncatedSince=true`：自动降级到 `replayMode=tail`
- 用户上滚触底：调用 `history` 接口继续向前拉分页块

---

## 5. 文件级实施清单

## 5.1 `apps/secretary-web/src/stores/terminal.js`
- 调整连接状态机：先 `replay=0`，条件触发 `replay=1`
- 增加 runtime 字段与 replay 决策逻辑
- 保留断线自动 replay
- 清理本地历史缓存依赖（仅保留最小视图态）

## 5.2 `apps/secretary-web/src/components/TerminalTab.vue`
- 保留 replay 首包 `term.reset()` 逻辑
- 仅从 WS 消息驱动视图，不从本地缓存回填整段历史
- 保留 ResizeObserver/WebGL 降级
- 截断提示与状态提示 UI

## 5.3 `apps/secretary-web/src/views/TerminalWorkspace.vue`
- 设置项文案与默认值校准：
  - replay 行为说明改为“必要时回放”
  - 备用屏默认关闭说明

## 5.4 `apps/terminal-gateway/src/pty-manager.js`
- Phase A：维持现状（8MB + truncated 元数据）
- Phase B：引入 seq/ring buffer（新增结构，不破坏旧字段）

## 5.5 `apps/terminal-gateway/src/server.js`
- Phase A：无接口破坏
- Phase B：支持 `sinceSeq/replayMode` 与 history 分页 API

## 5.6 `apps/terminal-gateway/test/gateway.test.js`
新增/更新测试：
- 条件 replay：切换会话默认不 replay（无落后时）
- 断线后自动 replay 恢复
- replay 首包 reset 后无重复
- truncated 提示链路（ready/list/snapshot 一致）
- Phase B：delta replay 正确性、truncatedSince 回退路径

---

## 6. 测试计划

1. **单元/接口测试（gateway）**
- `appendOutputBuffer` 截断行为
- `ready/list/snapshot` 元数据一致性
- replay/delta 路径正确性

2. **前端集成测试（secretary-web）**
- 会话切换：默认不全量 replay
- 必要条件触发 replay（首次/截断/落后）
- replay 首包 reset 去重
- 渲染降级与 resize 稳定性

3. **手工验证**
- 多会话快速切换
- 断网恢复
- 大输出截断后提示
- WebGL 异常模拟

---

## 7. 回滚与发布策略

1. 灰度开关（建议）
- `VITE_TERMINAL_CONDITIONAL_REPLAY=1`
- `TERMINAL_ENABLE_DELTA_REPLAY=0`（Phase B 预留）

2. 发布顺序
- 先发 gateway（字段向后兼容）
- 再发 secretary-web（使用新策略）
- 最后观测 replay 流量与错误率

3. 回滚
- 前端回退到“始终 replay”策略
- 后端保持字段兼容，不影响旧客户端

---

## 8. 监控与可观测性

新增指标（日志或埋点）：
- `ws_replay_full_count`
- `ws_replay_delta_count`（Phase B）
- `session_output_truncated_count`
- `terminal_reset_on_replay_count`
- `renderer_fallback_to_dom_count`
- 平均单次会话切换传输字节

---

## 9. Assumptions & Defaults

1. 会话历史仍是 gateway 内存态，不跨重启恢复。  
2. 默认 buffer 上限 `8MB`，可通过 `TERMINAL_MAX_OUTPUT_BUFFER_BYTES` 调整。  
3. Phase A 默认开启，Phase B 在完成测试后再灰度。  
4. 目标是“最终一致 + 低带宽”，不追求每帧绝对一致。  
