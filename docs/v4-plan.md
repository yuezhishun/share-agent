# V4 文件编辑工作台实现计划

## Summary

新增一个独立的桌面页 `DesktopTerminalViewV4.vue`，专门承载升级后的文件工作台：

- V4 作为新入口，普通代码文件使用 Monaco，提供接近 VS Code 的编辑体验。
- Markdown 文件使用成熟编辑器的即时渲染模式，采用 Typora 风格的“预览为主，聚焦块时显示源码编辑”。
- V3 保持可用，作为稳定旧版保留；V4 单独路由灰度验证，确认稳定后再决定是否替换 `/` 默认入口。
- 后端文件接口做兼容性增强，支持真正的“编辑读取”模式，避免当前预览截断逻辑影响 IDE 编辑。

## Key Changes

### 1. 页面与路由

- 新增视图文件：`apps/secretary-web/src/views/DesktopTerminalViewV4.vue`
- 路由新增：`/terminal-v4` -> `DesktopTerminalViewV4`
- 当前 `/` 继续指向 V3，不在本次内切默认入口。
- V4 复用现有终端、文件、配方、快捷指令 store，不另起一套状态体系。
- V4 只重构“中间文件编辑工作台”和其关联交互，不推翻 V3 已有的终端三栏框架。

### 2. V4 页面结构

- V4 保持现有桌面工作台信息架构：
  - 左侧终端/实例区
  - 中间终端与文件标签页区
  - 右侧文件浏览器/快捷指令/配方区
- 中间区抽成明确的“编辑器工作台”：
  - 终端 tab
  - 文件 tab strip
  - editor host
  - 文件级工具栏
- V4 中不再使用 `.editor-textarea` 作为主编辑控件。
- 允许从右侧文件浏览器打开多个文件标签页，并保留每个标签页独立 dirty 状态。

### 3. 编辑器栈

- 普通文本/代码文件使用 `monaco-editor`。
- Markdown 文件使用 `Vditor` 的 `ir` 模式。
- 编辑器选择规则固定为：
  - `.md`/`.markdown` -> `markdown-ir`
  - 其他可读文本文件 -> `code`
- 文件标签页状态固定包含：
  - `id/path/name/loading/error/editorKind/content/lastSavedContent/dirty/truncated/truncateReason/mtime?`
- 关闭脏标签页前确认。
- `Ctrl/Cmd+S` 保存当前活动标签页。
- 重载脏文件时继续确认丢弃修改。

### 4. 普通代码文件体验

- Monaco 默认配置固定为：
  - `automaticLayout: true`
  - `minimap: { enabled: false }`
  - `wordWrap: 'on'`
  - `scrollBeyondLastLine: false`
  - `fontFamily` 对齐现有 JetBrains Mono
- 语言按扩展名映射；未命中回退 `plaintext`。
- 保留顶部工具栏动作：
  - 保存
  - 重载
  - 未保存状态标识
- 本次不做 LSP、智能补全、diff、自动保存、多光标定制等扩展功能。

### 5. Markdown Typora 式体验

- Markdown 标签页使用单面板即时渲染编辑，不做左右分栏。
- 默认呈现渲染效果；当前聚焦块进入源码编辑态；离焦恢复渲染态。
- 支持标题、列表、引用、代码块、表格、链接、图片等常见 Markdown 元素的实时渲染。
- 工具栏保持极简，至少保留：
  - 保存
  - 重载
  - 可选“源码模式”切换
- 不再额外保留旧的纯文本 Markdown textarea 视图。

### 6. 前端模块拆分

- 从现有 V3 复制最小必要骨架到 V4，避免直接在 V3 上大改。
- 文件标签页与编辑器状态提炼为独立 composable 或模块，供 V4 使用。
- 编辑器宿主组件拆成两类：
  - `CodeFileEditor`
  - `MarkdownIrEditor`
- 文件打开、保存、重载、脏状态更新逻辑集中在共享文件-tab 模块中，不散落在页面脚本里。

### 7. 后端接口增强

- `GET /api/files/read` 新增 `mode=preview|edit`
- 行为固定为：
  - `preview` 沿用当前按 `max_lines` 截断的轻量预览
  - `edit` 返回完整文本内容，不受 `max_lines` 截断影响
- `edit` 模式文本上限固定为 2 MiB；超过上限返回明确错误，不返回部分内容。
- 非文本文件继续拒绝编辑读取。
- `POST /api/files/write` 保持不变，继续使用 `{ path, content }`

## Public APIs / Interfaces

- 新增前端路由：`/terminal-v4`
- `GET /api/files/read` 新增查询参数 `mode`
- V4 编辑器容器增加稳定测试标识：
  - `data-testid="file-editor-code"`
  - `data-testid="file-editor-markdown"`
- 文件标签页内部状态模型增加 `editorKind`，但不改外部文件浏览器接口。

## Test Plan

### 后端 xUnit

- `mode=preview` 继续按行截断
- `mode=edit` 返回完整文本
- `mode=edit` 超限时返回明确错误
- 非文本文件在 `edit` 模式下被拒绝

### 前端 Playwright

- 访问 `/terminal-v4` 可正常打开工作台
- 打开普通代码文件后显示 Monaco，可编辑、保存、重载
- `Ctrl/Cmd+S` 保存活动代码文件
- 打开 Markdown 文件后显示即时渲染编辑器，而不是旧 textarea
- Markdown 修改后能看到 Typora 风格的即时渲染效果
- 切换多个文件标签页时状态互不串扰
- 关闭未保存标签页触发确认

### 回归

- V3 页面继续可打开、保存普通文件
- 右侧文件浏览器上传、重命名、删除、切目录在 V4 中继续正常

## Assumptions

- 本次目标是新建 V4 页面，不替换现有 V3 默认入口。
- V4 允许初期复用 V3 的终端与侧边栏布局，只重点升级文件编辑工作台。
- Markdown 的“像 Typora”按块级即时渲染实现，不追求完整桌面 Typora 全部细节。
- 可接受引入 `monaco-editor` 和 `Vditor` 两个依赖，以降低实现风险并接近期望效果。
