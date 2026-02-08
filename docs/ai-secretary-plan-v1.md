# 秘书轻量化 + CLI Worker（Plan/Exec分离）V1 计划

## Summary
目标是把伪终端项目建设为“秘书式”AI 助手中枢：秘书只做任务与信息中介，不执行任务；执行由 CLI Worker 完成，并由 Orchestrator 统一调度、监控与评估。

复杂任务采用 Plan/Exec 分离：里程碑拆解由 CLI Worker 的 Plan 模式执行，执行阶段切换到新的 Worker，以确保任务独立、输入输出明确、便于总结汇报。

## 目标与范围

### 范围内
1. 多 CLI 进程管理：启动、停止、重试、超时、并发控制、会话追踪。
2. 多轮交互：老板 <-> 秘书助手 <-> CLI 的上下文连续对话。
3. 任务编排：混合模式（简单任务直派，复杂任务先 Plan 再 Execute）。
4. 状态监控：实时状态机、关键事件流、进度可视化、CLI 切换。
5. 评估与纠偏：规则+状态评估，偏航时自动重规划并记录“有效失败路径”。
6. 知识沉淀：多层存储与索引（结构化记录 + 文本 + 向量检索）。
7. Web 控制台：任务下达、进展查看、CLI 切换、告警与汇总。
8. Claude Code 启动配置：模型/env 注入、系统提示词（若支持）、原生参数透传。

### 范围外（V1 不做）
1. 多租户账号与权限中心。
2. 完整插件市场。
3. 全容器级隔离（先采用同机独立工作目录）。

## 角色边界（最终版）

### 秘书（不执行任务）
1. 接收老板任务与补充指令。
2. 结构化任务（仅结构化，不改写意图）。
3. 派发给 Orchestrator。
4. 收集过程信息与最终结果。
5. 向老板做事件驱动+定时摘要汇报。

### Orchestrator（集中式调度中心）
1. 识别新任务/后续任务（当前默认全自动语义关联）。
2. 判定简单/复杂任务并选择路由。
3. 触发 Planner Worker（Plan 模式）并校验计划。
4. 分配 Executor Worker（Execute 模式）并监督执行。
5. 维护全局状态机、重试、并发与超时。

### CLI Worker（执行实体）
1. Plan 模式：拆解里程碑、定义输入输出、验收标准、风险与依赖。
2. Execute 模式：按计划执行，产出交付物与中间结果。
3. Plan 与 Execute 默认分离 Worker（不同 session）。

### Evaluator + PDCA Engine
1. Plan：基于目标/约束进行计划生成或更新。
2. Do：驱动执行。
3. Check：规则评估（超时、停滞、错误密度、目标偏离）。
4. Act：自动重规划，记录失败路径与修正动作。

## 核心流程（确定版）
1. 老板下达任务 -> 秘书结构化 -> Orchestrator 入队。
2. Orchestrator 判断任务复杂度。
3. 简单任务：直接分配 Executor Worker 执行。
4. 复杂任务：
   - 分配 Planner Worker 进入 Plan 模式。
   - 产出 PlanArtifact（里程碑、IO、验收标准、风险）。
   - Orchestrator 做计划规则校验。
   - 创建 ExecutionHandoff。
   - 分配新的 Executor Worker 执行。
5. 执行中实时产生日志/事件，Web UI 可切换查看不同 CLI。
6. 检测偏航时触发重规划，告知老板“已验证无效路径并改道”。
7. 收尾：秘书汇总阶段成果、风险和下一步决策项。

## 数据模型与接口

### 关键实体
1. `Task`: `task_id`, `title`, `intent`, `constraints`, `status`, `priority`, `created_at`, `updated_at`
2. `TaskLink`: `source_input_id`, `task_id`, `link_type(new|follow_up)`, `confidence`, `reason`
3. `PlanArtifact`: `plan_id`, `task_id`, `planner_session_id`, `milestones`, `io_contracts`, `acceptance_criteria`, `risks`
4. `ExecutionHandoff`: `handoff_id`, `task_id`, `from_plan_id`, `executor_session_id`, `handoff_checklist`, `context_bundle_ref`
5. `ExecutionSession`: `session_id`, `task_id`, `cli_type`, `workdir`, `env_profile`, `status`
6. `ProgressEvent`: `event_id`, `task_id`, `session_id`, `event_type`, `severity`, `payload`, `timestamp`
7. `KnowledgeItem`: `item_id`, `task_id`, `kind(summary|lesson|failure_path)`, `content`, `embedding_ref`, `tags`

### 建议 API
1. `POST /tasks` 创建任务
2. `GET /tasks/:id` 获取任务详情
3. `POST /tasks/:id/decision` 提交老板决策（纠偏/资源）
4. `POST /sessions` 启动 CLI 会话
5. `POST /sessions/:id/input` 向 CLI 多轮输入
6. `GET /events/stream` 订阅实时事件（SSE/WebSocket）
7. `GET /reports/progress` 查看周期汇总/即时汇总
8. `GET /knowledge/search` 文本+向量检索

## 配置策略（当前默认）
1. 模型与 token：统一大预算运行，但设置任务上限与日总量硬阈值。
2. 任务关联：全自动语义关联（后续可升级为显式优先+自动补充）。
3. 隔离策略：每任务独立工作目录与环境变量。
4. 扩展策略：V1 透传 CLI 原生 tools/mcp/skills 参数，不做统一插件抽象。
5. 汇报策略：关键事件即时通知 + 固定周期摘要（建议 45 分钟）。

## 测试与验收

### 单元测试
1. 状态机流转正确：`queued -> planning -> handoff -> running -> done/failed`。
2. 偏航规则触发准确：超时、无输出、错误激增、里程碑逾期。
3. 简单/复杂任务路由正确。

### 集成测试
1. 秘书模块不能触发任何执行命令。
2. Plan/Exec 必须是不同 `session_id`。
3. 无 `PlanArtifact + ExecutionHandoff` 禁止进入执行。
4. CLI 会话生命周期可控：启动、交互、异常退出、恢复。
5. 事件流可断线重连。

### 端到端场景
1. “帮我做一个 xxx 系统”：计划拆解 -> 执行 -> 偏航纠偏 -> 交付汇总。
2. “把最近做的事情汇总”：跨任务聚合、进展摘要、风险清单。

### 验收标准
1. 任务状态到 UI 的可见延迟 < 3 秒。
2. 偏航后 1 个检测周期内触发重规划并形成记录。
3. 每个里程碑有明确输入/输出，可自动生成阶段汇报。

## 实施里程碑
1. M1：任务模型、状态机、PTY 会话管理、事件总线、最小 Web 看板。
2. M2：秘书工作台、复杂任务 Plan/Exec 分离、CLI 切换与实时监控。
3. M3：PDCA 纠偏、自动重规划、失败路径记录。
4. M4：知识库多层索引（结构化+文本+向量）与汇报体验优化。

## 假设与默认值
1. 单机单实例部署。
2. 首批支持 Claude Code CLI 与 Codex CLI。
3. 秘书不执行任务，仅做收发与汇报。
4. 复杂任务默认由 Planner Worker 与 Executor Worker 分离执行。
