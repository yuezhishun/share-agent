# Web Terminal V3 对齐 V1 实施计划

## 摘要
- 目标：新增 `terminal-v3` 页面，在不破坏 `terminal-v2` 现有稳定性的前提下，对齐 `terminal-v1` 的功能、界面和交互体验。
- 页面定位：
1. `terminal-v1`：现有完整功能基线与体验参考。
2. `terminal-v2`：现有稳定性基线与协议验证页面，原则上不承接大规模产品化改造。
3. `terminal-v3`：承接 v1 能力与 v2 稳定链路的新主工作台。
- 已锁定决策：
1. 不在 `terminal-v2` 上直接叠加 v1 全量功能。
2. `terminal-v3` 以 v1 的产品体验为基线。
3. `terminal-v3` 的底层连接、同步、恢复能力沿用 v2 协议与 store 思路。
4. 如体验对齐与稳定性发生冲突，优先保证稳定性，不回退到旧协议。

## 实施范围
- `apps/secretary-web/src/views`：新增 `DesktopTerminalViewV3.vue`，承接桌面端三栏工作台。
- `apps/secretary-web/src/router`：新增 `/terminal-v3` 路由，保留 `/` 和 `/terminal-v2` 现状不动。
- `apps/secretary-web/src/stores`：复用 `webcli-files`、`webcli-recipes`，为 v3 补齐所需的 UI 会话状态与 v2 终端能力接入。
- `apps/secretary-web/e2e`：新增 v3 页面回归用例，覆盖布局、交互、文件、配方、响应式与稳定性场景。
- `docs/`：保留本计划文档作为实现清单与验收依据。

## 公开接口与页面边界
- 新增前端路由：
1. `GET /terminal-v3` -> `DesktopTerminalViewV3.vue`
- 不新增后端 API；v3 默认复用现有：
1. v2 实例与节点 API：`/api/v2/instances`、`/api/v2/nodes`
2. v2 Hub：`/hubs/terminal-v2`
3. 文件 API：`/api/files/*`
- 数据模型沿用现有结构，不引入 v3 专属 schema：
1. 实例摘要继续使用 v2 `instances/nodes`
2. 配方继续使用 `webcli-recipes` 的 `name/group/cwd/command/args/env`
3. 文件浏览继续使用 `webcli-files` 的 list/read/write/upload/download 能力

## 方案细节（可直接实现）
### A. 页面骨架与视觉对齐
1. 新建 `DesktopTerminalViewV3.vue`，页面结构对齐 v1：
- 左栏：终端会话 + 终端配方
- 中区：终端 Tab + 文件编辑 Tab
- 右栏：文件浏览器 + 快捷指令
2. 保留 v1 的双侧栏折叠模式：
- 左栏独立折叠
- 右栏独立折叠
- 双栏同时折叠进入终端专注模式
3. 视觉样式以 v1 为基准：
- 字体使用 `Inter` 与 `JetBrains Mono`
- 沿用 v1 的字号、间距、边框、状态色、终端容器与滚动条风格
- 不沿用 v2 当前卡片式实验风格
4. 状态展示方式与 v1 对齐：
- 顶部工具栏显示当前状态
- 中区头部显示当前 terminal / file tab
- 保留 v2 的连接状态语义，但不保留独立 “V2” 实验标识

### B. 终端能力接入策略
1. `terminal-v3` 使用 v2 的终端连接与同步能力，不再使用 v1 的旧终端协议链路。
2. `webcli-terminal-v2` 作为底层连接基座，补齐 v3 所需的页面级能力：
- 实例列表刷新
- 节点列表加载
- 创建实例
- 连接实例
- 终止实例
- resync 与 reconnect 状态反馈
3. v1 中成熟的终端交互逻辑迁入 v3 页面层：
- 字体 ready 后再做稳定 fit
- 可见性恢复时重做 fit/resync
- 切换 terminal/file tab 时避免错误触发 resize
- 侧栏折叠/窗口 resize 后保持输入焦点
4. 保留并明确支持以下高频交互：
- 左键选中
- 右键粘贴
- 有选区时 `Ctrl+C` 优先复制
- 无选区时 `Ctrl+C` 可作为终端中断
- bracketed paste

### C. 会话列表与实例治理对齐
1. 在 v3 左栏补齐 v1 的会话列表能力：
- 实例列表展示
- 当前实例高亮
- 在线/离线状态
- 刷新列表
- 关闭实例
2. 实例别名能力从 v1 迁入可复用层，v3 继续支持：
- 设置别名
- 修改别名
- 清除别名
3. 创建实例入口与 v1 对齐：
- 普通创建
- 使用默认配方创建
- 创建后自动连接并切回 terminal tab
4. 若 v2 store 缺少别名/UI session 等纯前端状态，应抽为公共 composable 或 utility，不把 v1 整个 store 复制到 v3。

### D. 文件浏览器与文件编辑对齐
1. v3 右栏接入 `webcli-files`，补齐文件浏览器：
- 打开目录
- 返回上级目录
- 显示隐藏文件
- 上传文件
- 下载文件/目录
- 新建目录
- 重命名
- 删除
2. v3 中区补齐文件编辑器：
- 打开文件到 Tab
- 多文件 Tab 切换
- dirty 状态提示
- 重新加载
- 保存
- 截断展示提示
- 错误提示
3. 文件浏览默认路径策略与 v1 对齐：
- 优先当前实例 `cwd`
- 其次当前文件浏览路径
- 最后退回 `basePath`
4. 切换到文件 Tab 时不得触发远端 resize；切回 terminal 后才允许重新测量并必要时发送 resize。

### E. 终端配方与快捷指令对齐
1. v3 左栏接入 `webcli-recipes`，完整支持：
- 配方列表
- 分组展示
- 新建
- 编辑
- 删除
- 运行
- 设为默认创建配方
2. 配方编辑字段与 v1 保持一致：
- `name`
- `group`
- `cwd`
- `command`
- `args`
- `env`
3. 支持从当前 terminal 上下文生成配方草稿，并提示用户保存。
4. v3 右栏补齐快捷指令面板，支持：
- 分组展示
- 点击发送
- 焦点自动回到 terminal
- 与文件浏览器共享右栏 tab 切换入口

### F. 响应式与布局降级
1. v3 响应式行为以 v1 为基线：
- 宽屏显示完整三栏
- 中等宽度允许一侧折叠或下沉
- 手机宽度默认双栏折叠，仅保留主工作区
2. 保持现有 `/mobile` 与 `/mobile/files` 不动；本轮不重做移动专用页面。
3. 折叠、展开、切换 tab、窗口 resize、页面隐藏恢复后，都需要执行统一的 terminal re-fit 策略。
4. 响应式改造不得破坏：
- 输入焦点
- 终端尺寸稳定
- reconnect 后 snapshot 恢复

### G. 路由与迁移策略
1. 新增 `/terminal-v3` 页面，不修改 `/terminal-v2` 现有行为。
2. `terminal-v2` 后续只承担协议稳定性验证、故障复现与回归对照，不再继续堆叠产品功能。
3. `terminal-v3` 功能验证稳定后，再评估是否把默认桌面入口从 `/` 切换为 `/terminal-v3`。
4. 在默认入口切换前，v1 继续作为生产参考页存在。

## 测试计划
- 页面结构：
1. `/terminal-v3` 可正常渲染三栏布局、折叠按钮、terminal 区、文件区、配方区。
2. `/terminal-v2` 页面行为与现状一致，无额外功能耦合。
- 终端稳定性：
1. 创建、连接、输入、终止、重连、resync 正常。
2. 页面隐藏恢复后能重新同步显示。
3. 侧栏折叠/展开、tab 切换、窗口 resize 后终端输入不丢。
- 终端交互：
1. 左键选中可复制。
2. 右键可直接粘贴到 terminal。
3. 有选区时 `Ctrl+C` 不发送中断。
4. 无选区时 `Ctrl+C` 可发送中断。
- 文件能力：
1. 目录浏览、上传、下载、重命名、新建目录、删除正常。
2. 打开文件、编辑、保存、切换 file tab 正常。
3. 切换 file tab 不触发错误 resize。
- 配方能力：
1. 新建、编辑、删除、执行、设默认配方正常。
2. 使用默认配方创建实例后可自动连接。
- 响应式：
1. 桌面宽度三栏完整显示。
2. 手机宽度默认双侧栏折叠。
3. 折叠状态切换不影响终端焦点和输入。

## 实施顺序
1. 新增 `terminal-v3` 路由与页面骨架，先搭出三栏布局和折叠模型。
2. 接入 v2 terminal store，完成 terminal 主区创建/连接/输入/resize/resync。
3. 迁入会话列表、实例别名、状态展示与创建/终止流程。
4. 接入 `webcli-files`，完成右栏浏览器与中区文件编辑。
5. 接入 `webcli-recipes` 与快捷指令面板，补齐配方工作流。
6. 收口样式、字体、响应式与交互细节。
7. 补齐 v3 e2e 与回归验证，确认 v2 页面无回退。

## 假设与默认值
- 默认 v3 仅新增前端页面，不要求本轮新增后端接口。
- 默认 v2 store 允许做小幅扩展以支撑 v3，但不改变其稳定性语义。
- 默认 v1 是体验参考而不是继续扩展的目标页面。
- 默认“左键选中，右键粘贴”是 v3 必须具备的交互，不作为可选项。
