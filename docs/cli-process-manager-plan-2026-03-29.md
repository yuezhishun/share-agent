# CLI 进程管理升级方案（2026-03-29 执行版）

> 说明：这是本次已经按其实施过的方案归档稿，用于后续修订和对比。该方案已被发现存在严重问题，后续修改应基于此文档继续演进，而不是把它当成最终正确方案。

## 1. Summary

目标是新增一套“模板驱动的本地 CLI 进程管理”能力，保留现有节点式 API 体系，但将本地节点的核心运行模型从“任意命令执行”升级为“预设 CLI 模板启动”。

前端不继续扩展现有 Vue `secretary-web` 的 `/proc` 页面，而是在仓库内新增一个独立 React 子应用，参考 `AionUi` 的技术选型与交互风格实现新版页面。

本期范围只做非交互进程管理：启动、列表、详情、stdout/stderr、等待、停止、删除；不做实时 stdin 输入，不做跳转到 PTY 会话。CLI 模板由“终端配方”升级而来，持久化直接使用 SQLite。

## 2. Implementation Changes

### 2.1 .NET 后端：新增 CLI 模板与 CLI 进程管理子系统

在 `apps/terminal-gateway-dotnet/TerminalGateway.Api` 内新增一套与现有 `ProcessApiService` 并行的服务，职责拆分如下：

- `CliTemplateService`
  负责模板 CRUD、SQLite 持久化、内置模板种子、模板校验。
- `CliProcessManager`
  负责按模板启动本地 CLI、维护运行中/已结束进程、采集 stdout/stderr、等待/停止/删除。
- `CliLaunchBuilder`
  负责把模板 + 启动参数解析成最终命令，仅允许本地 CLI 启动，不接受任意 `file/args`。
- `CliSqliteStore`
  负责 SQLite 表初始化和模板读写；第一版只持久化模板，不持久化运行记录。

模板模型固定为“模板定义 + 启动参数”：

- 模板定义字段：
  `templateId`, `name`, `cliType`, `executable`, `baseArgs`, `defaultCwd`, `defaultEnv`, `description`, `icon`, `color`, `isBuiltin`, `createdAt`, `updatedAt`
- 启动请求字段：
  `templateId`, `cwdOverride`, `envOverrides`, `extraArgs`, `label`, `nodeId`

限制策略固定如下：

- 仅允许通过模板启动，不开放通用 `file/args` 提交。
- `executable` 必须来自模板表，不允许请求体覆盖。
- `cwdOverride` 仍沿用 `FILES_BASE_PATH` 范围校验。
- `envOverrides` 仅做键值覆盖，不允许删除模板默认环境变量。
- 运行记录只在内存中保存，服务重启后清空。

### 2.2 节点兼容：保留节点维度 API，但为 CLI 管理新增专用接口

保留现有 `/api/nodes/{nodeId}/processes*` 体系，不在本期强制删除；新版页面不再依赖这套旧的通用进程接口，而是走新的 CLI 专用接口。为保证集群兼容，本地节点直接执行，远程节点继续通过现有 `ClusterCommandBroker` 转发，新增命令类型：

- `cli.template.list`
- `cli.template.create`
- `cli.template.update`
- `cli.template.delete`
- `cli.process.start`
- `cli.process.list`
- `cli.process.get`
- `cli.process.output`
- `cli.process.wait`
- `cli.process.stop`
- `cli.process.remove`

新增 HTTP 接口固定为：

- `GET /api/nodes/{nodeId}/cli/templates`
- `POST /api/nodes/{nodeId}/cli/templates`
- `PUT /api/nodes/{nodeId}/cli/templates/{templateId}`
- `DELETE /api/nodes/{nodeId}/cli/templates/{templateId}`
- `POST /api/nodes/{nodeId}/cli/processes`
- `GET /api/nodes/{nodeId}/cli/processes`
- `GET /api/nodes/{nodeId}/cli/processes/{processId}`
- `GET /api/nodes/{nodeId}/cli/processes/{processId}/output`
- `POST /api/nodes/{nodeId}/cli/processes/{processId}/wait?timeout_ms=...`
- `POST /api/nodes/{nodeId}/cli/processes/{processId}/stop`
- `DELETE /api/nodes/{nodeId}/cli/processes/{processId}`

返回结构沿用现有进程接口的命名习惯：

- 列表返回 `{ items: [...] }`
- 详情包含 `processId`, `status`, `startTime`, `endTime`, `durationMs`, `command`, `templateId`, `templateName`, `outputCount`, `result`
- 输出项包含 `timestamp`, `processId`, `outputType`, `content`

### 2.3 模板来源：把“终端配方”正式升级为 CLI Template

不再复用 `apps/secretary-web/src/stores/webcli-recipes.js` 的浏览器本地存储模式；其产品语义被后端模板模型接管。模板行为固定如下：

- 旧“配方”的 `name/cwd/command/args/env` 映射到新模板的 `name/defaultCwd/executable/baseArgs/defaultEnv`
- 分组、图标、颜色保留为模板展示属性
- 内置模板至少提供：`bash`、`codex`，以及 1 个示例 CLI 模板
- 用户模板保存在 SQLite，内置模板由服务启动时种子写入内存视图，但不允许删除

SQLite 方案固定为：

- 新增配置项 `TERMINAL_CLI_TEMPLATE_DB_PATH`
- 默认路径放在 `apps/terminal-gateway-dotnet/TerminalGateway.Api` 运行目录可配置位置
- 单表即可落地首版：`cli_templates`
- 不做迁移框架；应用启动时执行幂等建表 SQL

### 2.4 React 前端：新增独立子应用

新增独立 React 子应用，建议路径固定为：

- `apps/proc-web-react`

技术栈固定为：

- React + TypeScript + Vite
- `react-router-dom`
- `swr`
- `@arco-design/web-react`

不接入现有 Vue 路由，不替换 `/proc`；作为独立子应用单独构建和部署，使用环境变量配置 API Base。

页面结构固定为三栏：

- 左栏：节点选择 + CLI 模板列表 + 模板编辑抽屉
- 中栏：运行中/历史进程列表 + 启动表单
- 右栏：进程详情 + stdout/stderr 过滤 + 自动滚动输出面板

交互固定为：

- 先选节点，再选模板，再填写启动参数并启动
- 模板可新增、编辑、删除；内置模板只读
- 启动后自动选中新进程并轮询详情/输出
- 支持 `all/stdout/stderr/system` 过滤
- 支持等待完成、停止、删除
- 不做 stdin 输入框，不做终端模拟器，不做多标签终端

前端数据层固定为：

- `useCliTemplates(nodeId)`
- `useCliProcesses(nodeId)`
- `useCliProcessDetail(nodeId, processId)`
- `useCliProcessOutput(nodeId, processId)`
- `useStartCliProcess()`
- `useMutateCliTemplate()`

## 3. Public APIs / Types

新增后端请求与响应类型：

- `CliTemplateRecord`
- `CreateCliTemplateRequest`
- `UpdateCliTemplateRequest`
- `StartCliProcessRequest`
- `StopCliProcessRequest`
- `CliProcessRecord`
- `CliProcessOutputItem`

字段约束固定为：

- `cliType` 为受控字符串，首版允许：`bash`, `codex`, `custom`
- `extraArgs` 为字符串数组
- `defaultEnv` / `envOverrides` 为 `Dictionary<string,string>`
- `status` 固定复用：`pending`, `running`, `completed`, `failed`, `stopped`
- `outputType` 固定复用：`standardoutput`, `standarderror`, `system`

## 4. Test Plan

后端测试：

- 新增 xUnit 测试覆盖模板 CRUD、SQLite 初始化、内置模板只读、非法模板校验。
- 覆盖本地节点 CLI 启动、输出采集、等待完成、停止、删除。
- 覆盖 `cwdOverride` 越界返回 403。
- 覆盖远程节点通过 `ClusterCommandBroker` 转发 CLI 模板和 CLI 进程命令。
- 保留旧 `ProcessRunnerTests` 与 `GatewayApiTests`，并新增 CLI 专用集成测试，不修改旧接口语义。

前端测试：

- React 子应用使用 Vitest + Testing Library 覆盖模板列表、模板表单、启动流程、输出过滤、错误态。
- 补 1 条 E2E：选择节点 -> 选择模板 -> 启动 -> 查看输出 -> 停止。
- 旧 Vue `processes-view.spec.js` 不删除，但新版 React app 增加独立 E2E，不与旧 `/proc` 共用断言。

## 5. Assumptions

- 本期“进程管理升级”以新增专用 CLI 管理体系为主，不直接重构旧的通用 `/api/processes` 和 Vue `/proc`。
- 本地 CLI 启动仅支持非交互式管理，不进入 PTY 会话。
- 模板持久化用 SQLite，运行记录不持久化。
- React 子应用独立部署，不嵌入现有 `secretary-web` 构建链。
- 如果远程节点尚未升级到新版本，CLI 专用节点接口对该节点可返回明确的“不支持”错误，而不是回退到旧通用进程模型。
