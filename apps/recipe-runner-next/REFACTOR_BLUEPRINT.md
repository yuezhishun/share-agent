# 配方调度优先的重构蓝图

## 用途

本目录用于承接新一代项目实现，不再直接在原项目上继续叠加改动。

这份文档的作用有两个：

- 作为新项目实现的主蓝图
- 作为“向后退键”的指导文档：当新实现偏离目标、需要停下或回退时，优先回到这里重新对照

## 核心结论

项目从“远程终端/文件/进程工具集合”收敛为“节点对等的配方执行与结果工作台”。

新的系统一等对象是 `RecipeRun`，不是终端会话。

固定默认方向：

- 节点关系：对等节点，保留现有 master/slave 传输实现仅作为兼容层
- 配方存储：每个节点维护自己的配方目录
- 执行模型：显式选择目标节点，单次执行 `one-shot`
- 结果模型：结构化运行记录优先，终端作为二级入口
- 快捷指令：并入配方体系，默认视为轻量配方或配方别名
- 文件工作台：保留，但降级为配方编辑、输入准备、产物查看的辅助工具

## 新架构

### 1. recipe-catalog

职责：

- 管理节点本地配方和快捷入口
- 提供增删改查、导入导出、分组与默认执行器定义

核心对象：`RecipeDefinition`

建议字段：

- `recipe_id`
- `node_id`
- `name`
- `group`
- `cwd`
- `command`
- `args`
- `env`
- `default_runner`

其中 `default_runner` 固定为：

- `managed_job`
- `interactive_terminal`

### 2. run-orchestrator

职责：

- 根据配方创建一次执行
- 统一维护运行状态、结果、取消、错误
- 统一封装本地执行与跨节点执行

核心对象：`RecipeRun`

建议字段：

- `run_id`
- `recipe_id`
- `target_node_id`
- `trigger_source`
- `status`
- `started_at`
- `finished_at`
- `exit_code`
- `runner_type`
- `runtime_ref`
- `stdout_summary`
- `stderr_summary`
- `artifacts`
- `error`

### 3. execution-runtime

职责：

- 继续承载 PTY、托管进程、文件访问能力
- 但不再作为产品主入口，只做执行层

约束：

- 终端实例和托管进程都应尽量挂到 `run_id`
- 不能挂接到 `run_id` 的资源，明确标记为“临时调试会话”

### 4. workspace-ui

职责：

- 首页改为配方和运行记录工作台
- 终端退居二线，仅在任务详情或调试时出现
- 文件编辑器保留，但入口从“主工作台中心”改为“配方相关工具”

## 新接口方向

新 UI 和新服务优先围绕 `recipes` 与 `runs`，不再以 `instance` 为主。

建议接口：

- `GET /api/v3/nodes/{nodeId}/recipes`
- `POST /api/v3/nodes/{nodeId}/recipes`
- `PUT /api/v3/nodes/{nodeId}/recipes/{recipeId}`
- `DELETE /api/v3/nodes/{nodeId}/recipes/{recipeId}`
- `POST /api/v3/runs`
- `GET /api/v3/runs`
- `GET /api/v3/runs/{runId}`
- `POST /api/v3/runs/{runId}/cancel`
- `GET /api/v3/runs/{runId}/output`
- `GET /api/v3/runs/{runId}/terminal`

`POST /api/v3/runs` 的最小请求体建议包含：

- `recipe_id`
- `source_node_id`
- `target_node_id`
- `overrides`

## 与旧项目的关系

旧项目保留，作为稳定参考与兼容来源：

- `apps/secretary-web`
- `apps/terminal-gateway-dotnet`

兼容策略：

- 旧 `process.*`、`instance.*` API 先保留，不立即删除
- 新项目 UI 不再直接围绕旧终端实例模型设计
- 旧终端入口只保留给 legacy 页面或调试路径

## 分阶段实施

### Phase 1：先建立新领域层

目标：

- 新建 `RecipeCatalogService`
- 新建 `RecipeRunService`
- 让“运行配方”不再等于“创建终端实例”
- 默认先复用已有托管进程能力作为执行引擎

完成标准：

- 能创建、查询、取消 `RecipeRun`
- 能从配方直接发起一次执行并得到结构化结果

### Phase 2：收口跨节点调用

目标：

- 用统一路由层替换当前分散的 cluster 命令分支
- 跨节点调用统一围绕 recipe/run

建议命令：

- `recipe.sync`
- `run.create`
- `run.get`
- `run.output`
- `run.cancel`
- `runtime.attach_terminal`

完成标准：

- UI 不需要关心本地执行还是远端执行
- 节点间调用不再直接暴露旧的终端产品模型

### Phase 3：新工作台成型

目标：

- 新首页改成配方与运行记录工作台
- 保留文件编辑区，但降为辅助工具
- 终端只在任务详情或高级调试时出现

建议一级视图：

- `RecipesView`
- `RunsView`
- `WorkspaceView`

完成标准：

- 主要操作路径是“选择配方 -> 选择节点 -> 执行 -> 看结果”
- 不是“先开终端，再在终端里做事”

### Phase 4：历史入口降级

目标：

- 旧终端页降为 legacy
- 旧进程页不再作为一级主导航
- 裸终端创建只保留在高级模式

完成标准：

- 新用户默认不会先接触旧终端中心化工作流

## 向后退键指导

当新项目推进出现偏移时，按下面顺序检查：

1. 当前改动是否仍以 `RecipeRun` 为一等对象
2. 当前主路径是否仍是“配方 -> 执行 -> 结果”
3. 当前新增页面是否把终端重新抬回首页主中心
4. 当前新增模型是否让快捷指令再次独立成第二套核心体系
5. 当前跨节点调用是否又回到了按 `instance/process/files` 各自分叉

只要上述任一答案为“是，偏离了目标”，就应暂停实现，先回到这份蓝图修订。

## 非目标

第一阶段不做：

- 广播式多节点批量扇出
- 复杂队列、优先级、重试编排
- 多租户权限模型
- 一次性清空全部 legacy 代码
- 把 master/slave 运行时实现立即彻底删除

## 回归与验收原则

新项目每一阶段都应满足：

- 不要求旧项目立刻下线
- 新旧项目可并存
- 能明确比较“新路径是否比旧路径更短、更稳、更聚焦”

最低验收路径：

1. 创建节点本地配方
2. 选择目标节点执行一次
3. 得到结构化运行结果
4. 必要时进入终端调试
5. 查看或编辑相关文件产物

## 默认假设

- 当前仍是单用户系统
- 安全边界不是本轮主要目标
- 托管进程是默认执行载体
- 终端是辅助能力，不是核心产品入口

