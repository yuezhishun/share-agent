# 终端配方与环境变量库改造方案

## Summary

目标分成两块一起落地：

- 在“终端配方”页签右上角新增一个相对独立的“环境变量库”入口，位置在“终端尺寸”按钮左边。
- 环境变量库按节点管理，支持 `Key / Value / Group` 三字段，且 `Value` 支持字符串或数组；数组在运行时按节点 OS 自动拼接成环境变量字符串，Windows 用 `;`，Linux 用 `:`。

终端配方继续按节点存储；创建或编辑配方时，既可以按组批量选择环境变量，也可以单独勾选具体变量项。

## Key Changes

### 1. 节点信息补充 OS 字段

为支持配方兼容性和数组型环境变量落地规则，节点模型补充 `node_os`：

- 后端节点协议增加 `node_os`，取值固定为 `windows` / `linux`
- 本地节点由服务端运行时判断，远端节点通过集群注册和心跳同步
- `/api/nodes` 返回 `node_os`
- 前端节点 store 保存 `node_os`，供配方过滤和环境变量数组 join 使用

### 2. 新增“环境变量库”独立模型

新增一套按节点管理的数据模型，不混在终端配方表里：

- 新表建议命名 `terminal_env_entries`
- 字段：
  - `env_id`
  - `key`
  - `value_json`
  - `group_name`
  - `sort_order`
  - `enabled`
  - `created_at`
  - `updated_at`
- `value_json` 统一存 JSON：
  - 字符串值存 `"xxx"`
  - 数组值存 `["a", "b"]`
- 校验规则：
  - `key` 必填，去空格
  - `group_name` 允许空，空时归到 `general`
  - `value_json` 只能是字符串或字符串数组
  - 数组项去空字符串
- 返回给前端时规范化为：
  - `id`
  - `key`
  - `valueType: string | array`
  - `value`
  - `group`
  - `enabled`

接口建议沿用现有节点风格：

- `GET /api/nodes/{nodeId}/terminal-envs`
- `POST /api/nodes/{nodeId}/terminal-envs`
- `PUT /api/nodes/{nodeId}/terminal-envs/{envId}`
- `DELETE /api/nodes/{nodeId}/terminal-envs/{envId}`

### 3. 环境变量库窗口设计

入口放在 `RightWorkspaceSidebar` 的配方页签头部，顺序改为：

- 环境变量库按钮
- 终端尺寸按钮
- 新建配方按钮

交互形式采用独立浮层窗口，不挤占配方列表主体：

- 形式：右侧面板内的覆盖式弹窗/抽屉，不跳路由
- 打开后包含两区：
  - 左侧：分组列表与分组筛选
  - 右侧：变量项列表 + 编辑表单
- 顶部操作：
  - 新建变量
  - 按组筛选
  - 搜索 key
- 单条变量编辑字段：
  - `Key`
  - `Value`
  - `Value 类型切换`
    - 单值
    - 数组
  - `Group`
  - 启用开关
- 数组值 UI：
  - 每行一个值
  - 支持增删行
  - 明确提示“运行时按节点平台自动拼接；Windows=`;`，Linux=`:`”
- 列表展示：
  - 显示 key、group、值摘要
  - 数组值显示元素个数和预览
  - 支持编辑、删除
- 关闭窗口不影响当前配方编辑上下文

### 4. 终端配方模型接入环境变量选择

终端配方不再只保存一坨 `default_env`，而是分成“引用环境变量库 + 配方自身覆盖”两层：

- `CliTemplateRecord` 新增：
  - `env_entry_ids: string[]`
  - `env_group_names: string[]`
- 含义：
  - `env_group_names` 表示整组引用
  - `env_entry_ids` 表示额外单项引用
- 配方编辑时可同时选组和单项：
  - 先勾组
  - 再勾单项补充
  - 已被组选中的单项在 UI 上标记为“已由分组包含”
- 配方运行时最终环境变量合并顺序：
  1. 选中的组内变量
  2. 单独选中的变量
  3. 配方自己的 `default_env`
  4. 运行时 override
- 若同一个 `key` 重复，后者覆盖前者
- 数组值转真实字符串时按目标节点 `node_os` join

这样可以满足：

- 按组选择 Node/.NET/Codex/Claude
- 单独补充 PATH、代理、Token 等个别变量
- 配方层仍能覆盖局部差异

### 5. 终端配方编辑器改造

配方编辑器保持“结构化为主 + 原始命令模式”，并增加环境变量库选择区：

- 基础字段：
  - 名称
  - 工作目录
  - 可执行文件
  - 参数列表
  - 兼容平台
- 新增“环境变量”区块：
  - 分组多选
  - 单项多选
  - 配方内覆盖变量表
- 显示规则：
  - 先显示已选组
  - 再显示组选外的单项
  - 最后显示配方覆盖项
- 预览区：
  - 展示最终会注入的环境变量结果
  - 对数组值显示“按当前节点 OS 展开后的结果”
- 不再要求用户在配方里手写整段 JSON 环境变量对象

### 6. 预置环境变量分组

环境变量库初始化时预置若干组，按节点首启写入，已有数据不覆盖：

- `nodejs`
  - `NODE_ENV`
  - `NPM_CONFIG_COLOR`
  - `FORCE_COLOR`
  - `PATH` 可用数组形式追加 Node/npm 常见目录
- `dotnet`
  - `DOTNET_ENVIRONMENT`
  - `ASPNETCORE_ENVIRONMENT`
  - `DOTNET_CLI_TELEMETRY_OPTOUT`
  - `DOTNET_NOLOGO`
- `codex`
  - `OPENAI_API_KEY`
  - `OPENAI_BASE_URL`
  - `CODEX_HOME`
- `claude`
  - `ANTHROPIC_API_KEY`
  - `ANTHROPIC_BASE_URL`

默认只放占位值或空值，不写真实密钥。

### 7. 预置终端配方

按节点 OS 继续补充内置终端配方，只显示兼容平台：

Linux：

- `Bash Interactive`
- `Bash Login`
- `Node REPL`
- `Codex CLI`
- `Claude CLI`

Windows：

- `PowerShell`
- `CMD`
- `Node REPL`
- `Codex CLI`
- `Claude CLI`

创建这些配方时默认关联相应环境变量组，但允许用户在配方编辑里删改。

## Public API / Interface Changes

后端新增/变更：

- `GET /api/nodes` 返回 `node_os`
- 新增 `terminal-envs` CRUD 接口
- `cli/templates` 读写新增：
  - `env_entry_ids`
  - `env_group_names`
  - `supported_os`

前端状态新增：

- `webcli-terminal-envs` store
- `recipeEditor` 增加：
  - `selectedEnvGroupNames`
  - `selectedEnvEntryIds`
  - `envOverrides`

## Test Plan

后端测试：

- `node_os` 在本地节点、集群节点、节点列表中正确返回
- 环境变量表支持字符串和字符串数组读写
- 数组值按 Windows `;`、Linux `:` 正确展开
- 配方运行时组选择、单项选择、覆盖项的优先级正确
- 同 key 冲突时覆盖顺序正确

前端测试：

- 配方页签头部新增环境变量库按钮，位置在终端尺寸左边
- 环境变量库窗口支持新增、编辑、删除、分组筛选、数组值编辑
- 配方编辑时可按组选择，也可单独选择变量
- 已被组选中的变量有正确标记，不重复提交
- 最终环境变量预览与后端执行结果一致
- 不同节点 OS 下 PATH 数组预览拼接结果不同

## Assumptions

- 环境变量库按节点存储，不做全局共享中心
- 数组值只允许字符串数组，不支持对象数组
- 数组值运行时自动 join，不把 JSON 原样传给进程
- 首版不支持 macOS 专属逻辑，未知 OS 不做数组自动平台优化
- 配方自己的覆盖环境变量仍然保留，用于处理单配方差异
