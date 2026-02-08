# 基于 PtyTerminal + Microsoft Agent Framework 的 AI 秘书技术实现方案（V1）

## 摘要
1. 采用 `C#/.NET` 单栈实现，`PtyTerminal` 负责跨平台 PTY 会话，`Microsoft Agent Framework`（MAF）作为默认编排引擎。
2. 秘书角色只做“收任务、结构化、派发、收集结果、汇报”，不执行任务。
3. 复杂任务由 `CLI Worker` 先进入 `Plan 模式` 拆解里程碑，再切换新的 `Execute Worker` 执行，确保任务输入输出边界清晰。
4. UI 采用 Web 控制台，终端流通过 `SignalR` 推送（你已指定替代原 WebSocket）。

## 目标与成功标准
1. 老板可持续下发任务并随时追问，系统不中断接单。
2. 任意任务可实时查看状态、日志、当前执行 CLI、下一决策点。
3. 复杂任务具备 Plan/Exec 分离与可审计交接。
4. 偏航可自动触发 PDCA 重规划并产出“失败路径记录”。
5. 系统支持多 CLI（首批 Claude Code CLI、Codex CLI）统一监管。

## 技术栈与部署边界
1. 后端：`ASP.NET Core 8` + `SignalR` + `Microsoft.Extensions.Hosting`。
2. 编排：`Microsoft Agent Framework (.NET)` 的 Agent + Workflow 能力。
3. PTY：集成 `Pty.Net`（参考 `PtyTerminal`），前端终端显示参考其 `PtyWeb` 思路。
4. 存储：`SQLite + 本地文件`。
5. 观测：`OpenTelemetry`（trace/log/metric），本地开发先 Console/OTLP。
6. 部署：单机单实例（V1）。

## 架构设计
1. `Secretary API`（秘书层）
1. 接收老板输入，结构化为 TaskCommand，不改写意图。
2. 提供任务查询、进展汇总、老板决策回传。
2. `Orchestrator Service`（集中调度）
1. 任务分类（简单/复杂）。
2. 新任务/后续任务识别（默认全自动语义关联）。
3. 驱动 MAF Workflow，维护状态机、超时、重试、并发。
3. `CLI Worker Runtime`
1. 基于 PtyTerminal 启动/停止/监控 CLI 会话。
2. 统一标准输入输出流、退出码、心跳、资源指标采集。
4. `Plan Worker` / `Execute Worker`
1. 复杂任务先 Plan Worker 产出里程碑与验收标准。
2. Orchestrator 校验后创建交接包，切换 Execute Worker。
5. `PDCA Evaluator`
1. Check：规则判断偏航（超时、停滞、错误密度、里程碑逾期）。
2. Act：触发 Replan Workflow，并记录失败路径。
6. `Knowledge Hub`
1. 结构化任务记录入 SQLite。
2. 长文本日志/摘要入本地文件。
3. 向量索引层（后续插件化接入，本期预留接口）。
7. `Web Console`
1. 任务看板。
2. 会话终端多标签切换。
3. 实时事件流与告警面板。
4. 周期摘要与阶段报告。

## 工作流设计（MAF）
1. `IntakeWorkflow`
1. 输入：老板自然语言任务。
2. 输出：`TaskIntent`（目标、约束、优先级、验收口径）。
2. `ClassificationWorkflow`
1. 判断简单/复杂、是否后续任务、是否需要老板决策点。
3. `PlanWorkflow`（仅复杂任务）
1. 由 CLI Worker 在 Plan 模式执行。
2. 输出 `PlanArtifact`：milestones、I/O contract、acceptance、risk、rollback。
4. `HandoffWorkflow`
1. 校验 PlanArtifact 完整性。
2. 生成 `ExecutionHandoff`，分配新 Execute Worker。
5. `ExecutionWorkflow`
1. 驱动 Execute Worker 实施。
2. 产出中间事件与最终交付。
6. `ReviewWorkflow`
1. 按规则计算 drift_score。
2. 偏航时自动触发 `ReplanWorkflow`。
7. `HITL Decision Workflow`
1. 使用 MAF request/response 模式挂起等待老板决策。
2. 收到决策后从检查点恢复执行。

## 核心状态机
1. `queued`
2. `intake_structured`
3. `classified`
4. `planning`（复杂任务）
5. `plan_reviewed`
6. `handoff_ready`
7. `executing`
8. `blocked_for_decision`
9. `replanning`
10. `done`
11. `failed`
12. `canceled`

## 公共接口与类型（重要）
1. `Task`
1. `task_id,title,intent,constraints,priority,status,created_at,updated_at`
2. `TaskLink`
1. `source_input_id,task_id,link_type(new|follow_up),confidence,reason`
3. `PlanArtifact`
1. `plan_id,task_id,planner_session_id,milestones,io_contracts,acceptance_criteria,risks`
4. `ExecutionHandoff`
1. `handoff_id,task_id,from_plan_id,executor_session_id,handoff_checklist,context_bundle_ref`
5. `ExecutionSession`
1. `session_id,task_id,cli_type,workdir,env_profile,status,pid,started_at,ended_at`
6. `ProgressEvent`
1. `event_id,task_id,session_id,event_type,severity,payload,timestamp`
7. `EvaluationRecord`
1. `record_id,task_id,rule_id,drift_score,action_taken,created_at`

## API 规划
1. `POST /api/tasks` 创建任务。
2. `GET /api/tasks/{id}` 查询任务详情。
3. `POST /api/tasks/{id}/decision` 提交老板决策（HITL恢复）。
4. `GET /api/tasks/{id}/timeline` 查询事件时间线。
5. `POST /api/sessions/{id}/input` 向指定 CLI 会话发送输入。
6. `POST /api/sessions/{id}/terminate` 中止会话。
7. `GET /api/reports/progress?window=...` 获取阶段汇总。
8. `GET /api/knowledge/search?q=...` 检索历史经验。
9. `SignalR Hub /hubs/runtime` 推送实时状态、终端输出、告警。

## 与参考项目的映射
1. 复用 `PtyTerminal` 的 `Pty.Net` 作为 PTY 内核。
2. 参考其 `PtyWeb` 展示链路，但传输层替换为 `SignalR`。
3. 采用 MAF 的 Workflow、Checkpoint、HITL、Observability 作为编排主干。
4. 结论：`PtyTerminal` 解决“会话能力”，`MAF` 解决“任务编排与治理能力”。

## 四类业务场景落地
1. “调研学生公寓”
1. Classification -> Plan（信息源、检索步骤、核验标准）-> Execute -> 汇总报告。
2. “帮我做一个 XXX 系统”
1. Plan 产出里程碑（需求、设计、编码、测试、发布建议）-> 分段执行 -> 持续汇报。
3. “给我说一下学生公寓情况”
1. 命中已有任务与知识库，生成即时摘要与证据引用。
4. “最近做的事情进度汇总”
1. 聚合 Task/Event/EvaluationRecord 生成跨任务周报。

## 测试计划与验收
1. 单元测试
1. 状态机转换合法性。
2. 偏航规则命中准确性。
3. PlanArtifact 校验器完整性。
2. 集成测试
1. Pty 会话生命周期（启动/输入/退出/异常）。
2. Plan/Exec 必须不同 session。
3. 无 handoff 不得执行复杂任务。
4. SignalR 断线重连与消息有序性。
5. MAF Checkpoint 恢复后任务一致性。
3. 端到端测试
1. 复杂开发任务全链路。
2. 老板中途纠偏并恢复执行。
3. 长任务崩溃恢复。
4. 验收指标
1. 任务状态可见延迟 < 3 秒。
2. 偏航后 1 个检测周期内触发 replan。
3. 复杂任务 Plan->Exec 交接成功率 >= 99%（本地基准）。

## 实施阶段
1. Phase 1（1-2周）
1. 项目骨架、SQLite 模型、PTY runtime、基础 API、SignalR 事件总线。
2. Phase 2（1-2周）
1. MAF 工作流接入：Intake/Classification/Execution。
2. 简单任务可全链路跑通。
3. Phase 3（2周）
1. 复杂任务 PlanWorkflow、HandoffWorkflow、Plan/Exec 分离执行。
4. Phase 4（1-2周）
1. PDCA 评估、Replan、HITL 决策挂起/恢复、检查点。
5. Phase 5（1周）
1. Web 控制台完善、报告模板、稳定性与压测。

## 风险与默认决策
1. 默认全自动语义任务关联，可能误判；V1 保留人工改挂接口。
2. SQLite 在高并发下瓶颈明显；V2 迁移 PostgreSQL 预留抽象层。
3. 统一大预算 token 策略可能成本高；加日预算硬阈值与告警。
4. CLI 外部工具不稳定时，依赖 checkpoint + 重试 + 人工决策兜底。

## 参考依据
1. PtyTerminal 仓库：https://github.com/gsw945/PtyTerminal
2. Microsoft Agent Framework 仓库：https://github.com/microsoft/agent-framework
3. MAF 文档总览：https://learn.microsoft.com/en-us/agent-framework/
4. Workflows 概览：https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/overview
5. Checkpoints：https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/checkpoints
6. HITL：https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/human-in-the-loop
7. Requests/Responses：https://learn.microsoft.com/en-us/agent-framework/tutorials/workflows/requests-and-responses
8. Observability：https://learn.microsoft.com/en-us/agent-framework/tutorials/agents/enable-observability
