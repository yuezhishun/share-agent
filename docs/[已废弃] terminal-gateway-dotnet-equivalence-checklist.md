# [已废弃] terminal-gateway-dotnet-equivalence-checklist

> 状态：已废弃
>
> 废弃日期：2026-02-25
>
> 原因：文档中的协议、架构或实现路径与当前仓库代码差异过大。
>
> 当前实现以 apps 下源码为准：前端与 dotnet gateway 已切换到 SignalR Hub /hubs/terminal。
>
> 建议参考：README.md、docs/terminal-gateway-dotnet.md、docs/nginx-config-paths.md。

# terminal-gateway-dotnet 对等实现清单

## 1. 目标与边界
1. 在 `apps/` 下新增 C# 服务，功能对等现有 `apps/terminal-gateway`。
2. 底层 PTY 引擎使用 `Porta.Pty`。
3. 不影响当前 Node 版网关与 orchestrator 默认配置。

## 2. 必须对等的 API
1. `GET /healthz`
2. `GET /projects/discover`
3. `GET /fs/dirs`
4. `GET /profiles`
5. `POST /profiles`
6. `PUT /profiles/{profileId}`
7. `DELETE /profiles/{profileId}`
8. `GET /settings/global-quick-commands`
9. `PUT /settings/global-quick-commands`
10. `GET /settings/fs-allowed-roots`
11. `PUT /settings/fs-allowed-roots`
12. `GET /sessions`
13. `POST /sessions`
14. `POST /sessions/{sessionId}/terminate`
15. `DELETE /sessions/{sessionId}`
16. `POST /sessions/prune-exited`
17. `GET /sessions/{sessionId}/snapshot`
18. `GET /sessions/{sessionId}/history`
19. `POST /internal/sessions`（`X-Internal-Token` 校验）
20. `GET /internal/sessions/{sessionId}`
21. `POST /internal/sessions/{sessionId}/input`
22. `POST /internal/sessions/{sessionId}/resize`
23. `POST /internal/sessions/{sessionId}/terminate`
24. `GET /ws/terminal`（WebSocket）

## 3. WebSocket 协议对等
1. Query 参数：`sessionId`（必填）、`replay`、`replayMode`、`sinceSeq`、`writeToken`。
2. 客户端上行消息：
1. `{ "type": "input", "data": "..." }`
2. `{ "type": "resize", "cols": 120, "rows": 40 }`
3. `{ "type": "ping", "ts": 123 }`
3. 服务端下行消息：
1. `ready`
2. `output`
3. `exit`
4. `error`
5. `pong`
4. 错误码对等：`SESSION_REQUIRED`、`SESSION_NOT_FOUND`、`READ_ONLY`、`BAD_MESSAGE`。

## 4. 会话管理能力对等
1. 内存会话表：`sessionId -> SessionRecord`。
2. 状态：`running | exited`。
3. 单写者模型：`writeToken` + `writerPeer`。
4. 输出缓存上限：`maxOutputBufferBytes`。
5. 输出序号：`headSeq`、`tailSeq`、`nextSeq`。
6. `snapshot(limitBytes)`。
7. `history(beforeSeq, limitBytes)`。
8. 增量回放：`sinceSeq` + `truncatedSince`。
9. exited 会话重连回放后发送 `exit`。
10. `remove/pruneExited` 行为一致。

## 5. Profile 与 Settings 对等
1. 内置 profile 种子。
2. custom profile CRUD。
3. profile 持久化文件（JSON）。
4. global quick commands 持久化。
5. fs allowed roots 持久化。

## 6. 配置对等（环境变量）
1. `PORT`
2. `HOST`
3. `TERMINAL_GATEWAY_TOKEN`
4. `TERMINAL_WS_TOKEN`
5. `TERMINAL_PROFILE_STORE_FILE`
6. `TERMINAL_SETTINGS_STORE_FILE`
7. `TERMINAL_MAX_OUTPUT_BUFFER_BYTES`
8. `TERMINAL_CODEX_CONFIG_PATH`
9. `TERMINAL_CLAUDE_CONFIG_PATH`
10. `TERMINAL_FS_ALLOWED_ROOTS`

## 7. Porta.Pty 适配要点
1. `PtyProvider.SpawnAsync` 创建会话。
2. `ReaderStream` 后台读取 -> output chunk。
3. `WriterStream` 写入 -> input。
4. `Resize(cols, rows)`。
5. `ProcessExited` 事件 -> 状态与广播。
6. `Kill()` 语义映射 terminate（对 signal 参数做兼容映射）。

## 8. 对等测试清单
1. spawn + ws output。
2. reconnect + ping/pong。
3. exited reconnect。
4. public create 返回 writeToken。
5. writeToken 权限控制。
6. replay full/tail/none。
7. sinceSeq delta replay。
8. truncatedSince 场景。
9. snapshot/history 场景。
10. terminate/remove/prune-exited。
11. profile CRUD。
12. settings 持久化。
13. fs allowed roots 持久化与校验。

## 9. 验收标准
1. 对等 API 路由全部可用。
2. 关键字段命名与语义与 Node 版保持一致。
3. 测试通过率达到 Node 版核心用例覆盖范围。
4. 与 orchestrator 的 internal 调用可直接切换联调。
