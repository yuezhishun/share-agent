# WindowsTerminal 风格终端工作台功能清单（基于 terminal-gateway + secretary-web）

## Summary
目标是在现有“可创建/连接 PTY 会话”的基础上，升级为类似 Windows Terminal + VSCode 的终端工作台：
1. 支持 CLI 配置档（Profile）管理（如 tools/mcp/skills）。
2. 支持多实例并行运行、侧栏展示、快速切换。
3. 刷新后可恢复实例列表与内容（基于 gateway 内存会话 + 输出回放）。
4. 为后续 Orchestrator 任务联动预留 profile 和实例元数据。

已确认默认决策：
1. 配置范围：全局共享。
2. 会话持久化：Gateway 内存 + 重连恢复（首期）。

## 一、功能列表（按优先级）

### P0（必须）
1. 终端配置档（Profiles）
- 新增“配置档管理”页面/抽屉：创建、编辑、删除、复制、设默认。
- 配置项：name、cliType、shell、cwd、args、env、startupCommands、icon、color。
- 内置默认 profile：bash、codex、mcp-tools、skills-runner（可编辑副本）。

2. 实例创建与侧栏切换（VSCode风格）
- 左侧“实例列表”展示运行中/已退出实例（可筛选）。
- 每个实例展示：标题、profile 名、状态、最后活动时间、cwd 简写。
- 点击实例即切换主终端窗口；支持“新建实例”“基于 profile 新建”。

3. 多实例状态机与会话恢复
- 状态：idle/connecting/connected/reconnecting/exited/error。
- 页面刷新后：自动拉取 gateway 会话列表并恢复左侧列表；自动连接上次活动实例。
- 已运行实例可重连并继续输入；已退出实例显示只读历史。

4. xterm.js 标准交互体验
- 使用 xterm.js + fit addon，键盘输入、粘贴、resize、滚动行为符合终端预期。
- 快捷操作：清屏、重连、断开、终止实例。
- 明确状态提示与错误提示（连接失败、会话不存在、权限/参数错误）。

### P1（应做）
1. Profile 启动参数模板化
- 支持变量占位：${workspaceRoot}、${taskId}、${profileName}。
- 为 tools/mcp/skills 提供推荐模板（如默认 cwd、启动命令）。

2. 实例分组与过滤
- 分组维度：按 profile、按 taskId、按状态。
- 搜索实例（sessionId/profile/cwd）。

3. 实例行为配置
- 启动后自动执行 startupCommands。
- 默认是否自动重连、是否自动聚焦、终止确认开关。

### P2（可选）
1. 窗格分屏（类似 Windows Terminal Panes）
- 同一实例视图分屏或多实例并排。

2. 会话快照导出
- 导出最近 N KB 输出日志。

3. 实例固定/收藏
- 常用 profile 或常用实例固定到侧栏顶部。

## 二、关键接口与类型变更（Decision Complete）

### terminal-gateway 新增/扩展 API
1. GET /profiles
- 返回全局 profile 列表（含内置 + 用户新增）。

2. POST /profiles
- 创建 profile。

示例：
```json
{
  "name": "mcp-tools",
  "cliType": "custom",
  "shell": "/bin/bash",
  "cwd": "/workspace/tools/mcp",
  "args": ["-i"],
  "env": {"NODE_ENV":"development"},
  "startupCommands": ["pwd", "ls"],
  "icon": "tool",
  "color": "#1ea7a4"
}
```

3. PUT /profiles/:profileId、DELETE /profiles/:profileId

4. POST /sessions
- 扩展支持 profileId，创建时合并 profile 配置与请求覆盖字段。
- 合并优先级：request overrides > profile defaults > system defaults。

5. GET /sessions?includeExited=0|1&profileId=&taskId=
- 支持过滤，用于侧栏分组和检索。

6. GET /sessions/:sessionId/snapshot
- 返回可回放输出片段（用于刷新恢复与只读历史）。

7. WS /ws/terminal?sessionId=...
- 保持现有消息协议，补充字段：profileId、title（ready 事件可带）。

### secretary-web 主要前端模型
1. TerminalProfile
```ts
type TerminalProfile = {
  profileId: string;
  name: string;
  cliType: string;
  shell: string;
  cwd: string;
  args: string[];
  env: Record<string, string>;
  startupCommands: string[];
  icon?: string;
  color?: string;
  isBuiltin: boolean;
}
```

2. TerminalInstance
```ts
type TerminalInstance = {
  sessionId: string;
  profileId?: string;
  title: string;
  status: 'connecting'|'connected'|'reconnecting'|'exited'|'error';
  cwd?: string;
  taskId?: string;
  lastActivityAt: string;
}
```

3. Store 拆分
- terminalProfileStore：profile CRUD + 缓存。
- terminalSessionStore：实例列表、活动实例、连接状态机、重连策略。

## 三、页面与交互设计（secretary-web）
1. TerminalWorkspace 改版为三栏
- 左：实例侧栏（新建、过滤、分组、切换）。
- 中：主 xterm 终端区（当前实例）。
- 右（可折叠）：实例详情与 profile 参数（cwd/env/startup）。

2. 新建实例流程
- 点击“+” -> 选择 profile -> 可临时覆盖 cwd/command/env -> 创建并自动切换。

3. 刷新恢复流程
- onMounted: loadProfiles() + loadSessions()。
- 选中上次 activeSessionId（localStorage），若不存在则选最近 running。

4. 实例关闭语义
- 断开：仅断 WS，不杀进程。
- 终止：调用 terminate，状态变 exited 并保留历史显示。

## 四、与 Orchestrator 的联动边界（本轮只做兼容）
1. 会话元数据保留 taskId/cliType/profileId，便于任务页跳转终端。
2. TaskBoard 中若有 plannerSessionId/executorSessionId，可直接打开对应实例。
3. 本轮不新增鉴权体系；后续可在 /profiles、/sessions 接入用户鉴权。

## 五、测试用例与验收场景

### Gateway
1. profile CRUD：创建/更新/删除/内置只读限制（若设计为只读）。
2. 基于 profile 创建 session：参数合并正确。
3. 会话列表过滤：includeExited/profileId/taskId 正确。
4. WS attach/reconnect：running 可继续输入，exited 返回 exit 并关闭。
5. snapshot：刷新后可拉取历史片段并渲染。

### Web（组件+E2E）
1. 侧栏显示多个实例并可切换，切换后终端内容与输入目标正确。
2. 通过不同 profile 新建实例，启动 cwd/命令生效。
3. 刷新页面后实例仍可见，running 可重连，exited 可查看历史。
4. 断网重连与手动重连路径工作正常。
5. 断开与终止语义区分正确。

### 验收标准
1. 用户可配置至少 4 种 CLI profile（含 tools/mcp/skills 场景）。
2. 可同时打开 >= 5 个实例并在侧栏稳定切换。
3. 刷新页面后，运行中实例 30 秒内可恢复交互。
4. 每个实例都能独立保存启动参数与显示状态。

## 六、实施顺序（可直接执行）
1. terminal-gateway：先补 profiles 数据模型与 CRUD，再扩展 /sessions 参数合并与过滤。
2. terminal-gateway：补 snapshot 接口与 ready 元数据字段。
3. secretary-web：拆分 store（profiles/sessions），重构 TerminalWorkspace 为侧栏+主区架构。
4. secretary-web：接入 profile 管理 UI 与“基于 profile 新建实例”弹窗。
5. 回归：gateway 单测、web 构建、关键 E2E（多实例切换/刷新恢复/终止语义）。

## 七、假设与默认值
1. 首期仅 Linux 服务器环境（默认 shell /bin/bash）。
2. profile 为全局共享，不做用户隔离权限。
3. 会话持久化仅依赖 gateway 进程存活，不跨 gateway 重启恢复。
4. 本轮不实现安全鉴权与审计闭环，仅保留接口扩展位。
