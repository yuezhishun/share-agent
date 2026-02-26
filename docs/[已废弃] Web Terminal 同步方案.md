# [已废弃] Web Terminal 同步方案

> 状态：已废弃
>
> 废弃日期：2026-02-25
>
> 原因：文档中的协议、架构或实现路径与当前仓库代码差异过大。
>
> 当前实现以 apps 下源码为准：前端与 dotnet gateway 已切换到 SignalR Hub /hubs/terminal。
>
> 建议参考：README.md、docs/terminal-gateway-dotnet.md、docs/nginx-config-paths.md。

# Web Terminal 同步方案实现计划

## Context

实现一个 Web 版 CLI，解决两个核心同步问题：
1. **Resize 同步问题**：前端 resize 时，PTY 输出与新尺寸不匹配，导致显示混乱
2. **数据顺序不同步**：网络传输时 PTY 流式输出与接收顺序不一致，ANSI 序列被拆分导致乱码

**技术栈**：
- 后端：Porta.Pty + XTerm.NET（headless 模式）
- 前端：Vue + xterm.js
- 通信：ASP.NET Core SignalR（自动连接管理、重连、广播支持）

**核心策略**：
- **简化版**：移除完整序号机制，依赖 SignalR 有序传输
- **当前屏幕快照同步**：仅同步终端当前可见内容（最终状态）
- **全部历史记录同步**：只同步最终状态，不记录 PTY 输出过程
- **不记录过程动画**：无法进行回放，只关心最终状态
- **事件驱动触发**：非定期触发，由客户端请求或检测到异常时触发

---

## 架构设计

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              Vue 前端                                │
│  ┌──────────────┐         ┌──────────────┐         ┌──────────────┐   │
│  │   xterm.js   │         │ SyncManager  │         │   SignalR     │   │
│  │  (渲染层)    │◄────────►│ (序号/ACK)  │         │   Client      │   │
│  └──────────────┘         └──────────────┘         └──────────────┘   │
└───────────────────────────────────────────────▲──────────────────────────────┘
                                          │ SignalR
                                          │ (自动重连/心跳)
                                          │
┌───────────────────────────────────────────────┴──────────────────────────────┐
│                          .NET 后端                                   │
│  ┌──────────────┐         ┌──────────────┐         ┌──────────────┐   │
│  │ Terminal    │         │  Sequence    │         │   Porta.Pty  │   │
│  │  Hub        │         │  Manager     │         │   Adapter     │   │
│  └──────────────┘         └──────────────┘         └──────────────┘   │
│         │                                                      │    │
│         │ 使用但不依赖                                           │    │
│  ┌──────▼──────┐                                              │    │
│  │  XTerm.NET  │  (用于状态快照和验证，不用作解析)               │    │
│  └─────────────┘                                              │    │
└──────────────────────────────────────────────────────────────────────────────┘
```

**SignalR 优势**：
- 自动连接管理、重连、心跳
- 内置超时和错误处理
- 支持组和广播（便于多终端、协作）
- 与 ASP.NET Core 无缝集成
- 支持类型强化的 Hub 方法

---

## 协议设计（SignalR Hub）

### TerminalHub 定义

SignalR 使用 Hub 模式，服务端定义方法，客户端调用。

**服务端方法（服务器 → 客户端）**：
```csharp
public interface ITerminalClient
{
    // 推送 PTY 输出（实时）
    Task ReceiveOutputAsync(string data);

    // 推送当前屏幕快照
    Task ReceiveScreenSnapshotAsync(ScreenSnapshot snapshot);

    // 推送 Resize 确认
    Task ReceiveResizeAckAsync(int cols, int rows);
}

public class TerminalHub : Hub<ITerminalClient>
{
    public string ConnectionId => Context.ConnectionId;

    // 客户端调用：发送用户输入到 PTY
    public async Task SendInputAsync(UserInput input);

    // 客户端调用：请求 Resize
    public async Task RequestResizeAsync(ResizeRequest request);

    // 客户端调用：请求同步
    public async Task RequestSyncAsync(SyncRequest request);
}
```

**客户端方法（客户端 → 服务器）**：
```typescript
import { HubConnection } from '@microsoft/signalr';

interface TerminalHub {
  sendInput(input: UserInput): Promise<void>;
  requestResize(request: ResizeRequest): Promise<void>;
  requestSync(request: SyncRequest): Promise<void>;
}

// 接收服务器推送的方法
interface TerminalClient {
  receiveOutput(data: string): void;
  receiveScreenSnapshot(snapshot: ScreenSnapshot): void;
  receiveResizeAck(cols: number, rows: number): void;
  onReconnecting(): void;
  onReconnected(): void;
  onClose(): void;
}
```
```

### 消息类型定义（简化版）

**注意**：移除序号机制，依赖 SignalR 有序传输，只关心最终状态同步。

```typescript
// 当前屏幕快照（仅同步可见内容）
interface ScreenSnapshot {
  cols: number;
  rows: number;
  cursorX: number;
  cursorY: number;
  activeBuffer: 'normal' | 'alternate';
  content: string[];  // 每行内容（简化，不包含样式）
  timestamp: number;
}

// 历史记录（最终状态同步）
interface HistorySnapshot {
  command: string;      // 执行的命令
  output: string;       // 最终输出
  timestamp: number;
}

// 用户输入
interface UserInput {
  data: string;
}

// Resize 请求
interface ResizeRequest {
  cols: number;
  rows: number;
}

// 同步请求
interface SyncRequest {
  type: 'screen' | 'history';  // 屏幕快照 或 历史记录
}
```

---

## 实施建议

**简化原则**：
- 不依赖序号/ACK 机制（SignalR 保证有序传输）
- 只关心最终状态（当前屏幕 + 历史记录）
- 不记录 PTY 输出过程/动画（无法回放）

**分阶段实现**：
1. 优先实现基础 SignalR 连接 + PTY 实时数据转发
2. 实现当前屏幕快照同步（客户端请求时触发）
3. 实现最终历史记录同步（命令历史 + 输出记录）
4. 测试正常后再考虑添加增量优化
5. 容器化部署，每个会话运行在独立容器中

---

## 实现计划

### 第一阶段：SignalR 基础架构

#### 后端：TerminalHub.cs

```csharp
using Microsoft.AspNetCore.SignalR;

public class TerminalHub : Hub<ITerminalClient>
{
    private readonly TerminalSessionManager _sessionManager;

    public TerminalHub(TerminalSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        await _sessionManager.RegisterConnectionAsync(connectionId);
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        await _sessionManager.UnregisterConnectionAsync(Context.ConnectionId);
    }

    // 客户端调用：发送用户输入到 PTY
    public async Task SendInputAsync(InputMessage message)
    {
        await _sessionManager.SendInputAsync(Context.ConnectionId, message);
    }

    // 客户端调用：请求 Resize
    public async Task RequestResizeAsync(ResizeMessage message)
    {
        await _sessionManager.RequestResizeAsync(Context.ConnectionId, message);
    }

    // 客户端调用：请求同步
    public async Task RequestSyncAsync(SyncRequestMessage message)
    {
        await _sessionManager.RequestSyncAsync(Context.ConnectionId, message);
    }

    // 客户端调用：发送 ACK
    public async Task SendAckAsync(AckMessage message)
    {
        await _sessionManager.ProcessAckAsync(Context.ConnectionId, message);
    }
}
```

#### 后端：TerminalSessionManager.cs

```csharp
public class TerminalSessionManager
{
    private readonly ConcurrentDictionary<string, TerminalSession> _sessions;

    public async Task RegisterConnectionAsync(string connectionId)
    {
        var session = new TerminalSession(connectionId);
        _sessions.TryAdd(connectionId, session);
    }

    public async Task UnregisterConnectionAsync(string connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            await session.DisposeAsync();
        }
    }

    public async Task SendInputAsync(string connectionId, InputMessage message)
    {
        if (_sessions.TryGetValue(connectionId, out var session))
        {
            await session.WriteToPtyAsync(message.data);
        }
    }

    public async Task RequestResizeAsync(string connectionId, ResizeMessage message)
    {
        if (_sessions.TryGetValue(connectionId, out var session))
        {
            await session.ResizeAsync(message.cols, message.rows);
        }
    }
}
```

#### 后端：TerminalSession.cs（简化）

```csharp
public class TerminalSession : IDisposable
{
    private readonly HubCaller<ITerminalClient> _caller;
    private readonly IPty _pty;
    private readonly SequenceManager _seqManager;
    private readonly SnapshotManager _snapshotManager;
    private readonly Channel<OutboundMessage> _outputChannel;
    private readonly SemaphoreSlim _readLimiter;
    private volatile bool _outputSuspended;

    public TerminalSession(HubCaller<ITerminalClient> caller, IPty pty, TerminalOptions options)
    {
        _caller = caller;
        _pty = pty;
        _seqManager = new SequenceManager();
        _outputChannel = Channel.CreateBounded<OutboundMessage>(1000);
        _readLimiter = new SemaphoreSlim(10, 10);
        _snapshotManager = new SnapshotManager();

        // 创建 PTY
        // ... PTY 初始化逻辑

        _ = OutputTask();
    }

    private async Task OnPtyDataAsync(string data)
    {
        if (_outputSuspended)
        {
            // 背压：暂停从 PTY 读取
            return;
        }

        await _readLimiter.WaitAsync();
        try
        {
            bool success = _outputChannel.Writer.TryWrite(new OutboundMessage
            {
                Type = "output",
                Data = data,
                Seq = _seqManager.NextSeq()
            });

            if (!success)
            {
                _outputSuspended = true;
            }
        }
        finally
        {
            _readLimiter.Release();
        }
    }

    public async Task WriteToPtyAsync(string data)
    {
        await _pty.WriteAsync(data);
    }

    public async Task ResizeAsync(int cols, int rows)
    {
        // 调整 PTY 尺寸
        await _pty.ResizeAsync(cols, rows);
        // 立即恢复，无需固定等待
    }

    public void SuspendOutput() => _outputSuspended = true;
    public void ResumeOutput() => _outputSuspended = false;

    private async Task OutputTask()
    {
        await foreach (var msg in _outputChannel.Reader.ReadAllAsync())
        {
            await _caller.Clients[Context.ConnectionId].ReceiveOutputAsync(msg);
            _seqManager.RegisterSent(msg.Seq);

            if (_outputSuspended && _outputChannel.Reader.Count < 500)
            {
                ResumeOutput();
            }
        }
    }
}
```

#### 前端：SignalRClient.js

```javascript
import * as signalR from '@microsoft/signalr';

export class SignalRClient {
  constructor(hubUrl, onMessage) {
    this.hubUrl = hubUrl;
    this.onMessage = onMessage;
    this.connection = null;
    this.hub = null;
    this.seq = 0;
    this.connectionState = 'disconnected';
    this.receivedCount = 0;
    this.ackThreshold = 10;
    this.ackTimeout = null;
  }

  connect() {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(this.hubUrl)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    this.connection.start();

    this.hub = this.connection.serverTimeout(60000);  // 60秒超时
    this.setupEventHandlers();
  }

  setupEventHandlers() {
    // 自动重连事件
    this.connection.onreconnecting(() => {
      console.log('SignalR reconnecting...');
      this.connectionState = 'reconnecting';
    });

    this.connection.onreconnected(() => {
      console.log('SignalR reconnected');
      this.connectionState = 'connected';
      // 重连后请求同步
      this.hub.invoke('RequestSync', { reason: 'reconnect', seq: this.nextSeq() });
    });

    this.connection.onclose(() => {
      console.log('SignalR closed');
      this.connectionState = 'disconnected';
    });

    // Hub 方法调用
    this.hub.on('ReceiveOutput', (msg) => this.handleMessage(msg));
    this.hub.on('ReceiveSnapshot', (msg) => this.handleMessage(msg));
    this.hub.on('ReceiveResizeAck', (msg) => this.handleMessage(msg));
    this.hub.on('ReceiveSyncResponse', (msg) => this.handleMessage(msg));
  }

  async sendInput(data) {
    await this.hub.invoke('SendInput', {
      data: data,
      seq: this.nextSeq(),
      timestamp: Date.now()
    });
  }

  async requestResize(cols, rows) {
    await this.hub.invoke('RequestResize', {
      cols: cols,
      rows: rows,
      seq: this.nextSeq(),
      timestamp: Date.now()
    });
  }

  recordReceived(seq) {
    this.receivedCount++;
    this.lastAckedSeq = seq;

    // 累积发送 ACK
    if (this.receivedCount >= this.ackThreshold) {
      this.sendAck();
      this.receivedCount = 0;
    } else {
      this.scheduleAckTimeout(500);
    }
  }

  scheduleAckTimeout(ms) {
    if (this.ackTimeout) {
      clearTimeout(this.ackTimeout);
    }
    this.ackTimeout = setTimeout(() => {
      this.sendAck();
      this.receivedCount = 0;
    }, ms);
  }

  sendAck() {
    const gaps = this.detectGaps();
    if (gaps.length === 0 && this.receivedCount === 0) {
      return;
    }

    this.hub.invoke('SendAck', {
      ackedSeq: this.lastAckedSeq,
      nack: gaps,
      timestamp: Date.now()
    });
  }

  detectGaps() {
    // 检测缺失序号
    return [];
  }

  nextSeq() {
    return this.seq++;
  }

  handleMessage(msg) {
    this.onMessage(msg);
    this.recordReceived(msg.seq);
  }

  disconnect() {
    this.connection.stop();
    this.connection = null;
    this.hub = null;
    this.connectionState = 'disconnected';
  }
}
```

### 第二阶段：当前屏幕快照服务

#### 后端：SnapshotService.cs

```csharp
public class SnapshotService
{
    private readonly Terminal _terminal;

    public SnapshotService(Terminal terminal)
    {
        _terminal = terminal;
    }

    // 捕获当前屏幕快照（只包含可见内容）
    public ScreenSnapshot CaptureScreenSnapshot()
    {
        var buffer = _terminal.Buffer;

        return new ScreenSnapshot
        {
            cols = _terminal.Cols,
            rows = _terminal.Rows,
            cursorX = buffer.X,
            cursorY = buffer.Y,
            activeBuffer = _terminal.ActiveBuffer.ToString(),
            content = GetVisibleLines(buffer),
            timestamp = DateTime.UtcNow
        };
    }

    private string[] GetVisibleLines(TerminalBuffer buffer)
    {
        var lines = new List<string>();
        int startLine = buffer.YDisp;
        int endLine = Math.Min(buffer.Length, buffer.YDisp + buffer.Rows);

        for (int i = startLine; i < endLine; i++)
        {
            var line = buffer.GetLine(i);
            if (line != null)
            {
                lines.Add(line.TranslateToString(trimRight: true));
            }
        }

        return lines.ToArray();
    }
}
```

### 第三阶段：最终历史记录服务

#### 后端：HistoryService.cs

```csharp
public class HistoryService
{
    private readonly ConcurrentDictionary<string, List<HistoryEntry>> _sessionHistory;

    public void RecordCommand(string connectionId, string command)
    {
        // 简化：只记录命令，不记录中间过程
        _sessionHistory.AddOrUpdate(connectionId, new List<HistoryEntry>(),
            (key, oldList) => {
                var newList = oldList.ToList();
                newList.Add(new HistoryEntry { Command = command, Timestamp = DateTime.UtcNow });
                return newList;
            });
    }

    public HistoryEntry[] GetFinalHistory(string connectionId)
    {
        if (_sessionHistory.TryGetValue(connectionId, out var history))
        {
            return history.ToArray();
        }
        return Array.Empty<HistoryEntry>();
    }
}

public class HistoryEntry
{
    public string Command { get; set; }
    public DateTime Timestamp { get; set; }
}
```

---

### 第二阶段：序号与 ACK 机制（已移除，改用简化版本）

#### 后端：SequenceManager.cs

```csharp
public class SequenceManager
{
    private uint _sendSeq = 0;
    private uint _lastAckedSeq = 0;
    private readonly ConcurrentDictionary<uint, DateTime> _unacked = new();

    public uint NextSeq()
    {
        return Interlocked.Increment(ref _sendSeq) - 1;
    }

    public void RegisterSent(uint seq)
    {
        _unacked[seq] = DateTime.UtcNow;
        _ = CheckTimeoutAsync(seq);
    }

    public void ProcessAck(uint ackedSeq, List<uint>? nackList)
    {
        // 移除已确认的消息
        foreach (var seq in _unacked.Keys.Where(k => k <= ackedSeq).ToList())
        {
            _unacked.TryRemove(seq, out _);
        }
        _lastAckedSeq = ackedSeq;

        // 处理 NACK（重传）
        if (nackList != null && nackList.Count > 0)
        {
            await RetransmitMessagesAsync(nackList);
        }
    }

    private async Task RetransmitMessagesAsync(List<uint> seqs)
    {
        // 从缓存中重传消息
        // 需要由外部提供消息缓存
    }
}
```

---

### 第三阶段：Resize 同步处理

#### Resize 流程

```
前端                        SignalR                    后端
  │                             │                        │
  │ 1. 防抖后检测 resize       │                        │
  ├─────────────────────────────>│                        │
  │    requestResize {cols, rows} │                        │
  │                             │  1. PTY.Resize()        │
  │                             │                        │
  │ 2. 本地先 resize             │  2. 捕获新状态          │
  │    (减少闪烁)               │                        │
  │                             │<───────────────────────┤
  │    receiveResizeAck          │                        │
  │<───────────────────────────┤                        │
  │  3. 验证尺寸匹配            │                        │
```

#### 前端：Vue 组件

```vue
<script>
import { Terminal } from 'xterm';
import { FitAddon } from 'xterm-addon-fit';
import { SignalRClient } from '@/utils/SignalRClient';

export default {
  data() {
    return {
      terminal: null,
      fitAddon: null,
      signalR: null,
      isResizing: false,
      resizeTimer: null
    };
  },

  mounted() {
    this.initTerminal();
    this.setupSignalR();
    this.setupResizeHandler();
  },

  methods: {
    initTerminal() {
      this.terminal = new Terminal({
        cursorBlink: true,
        fontSize: 14
      });
      this.fitAddon = new FitAddon();
      this.terminal.loadAddon(this.fitAddon);
      this.terminal.open(this.$refs.terminal);
      this.fitAddon.fit();
    },

    setupSignalR() {
      this.signalR = new SignalRClient('/terminalhub', (msg) => {
        this.handleServerMessage(msg);
      });
      this.signalR.connect();

      // SignalR 内置自动重连
      this.signalR.connection.onreconnecting(() => {
        this.$emit('terminal-reconnecting');
      });
      this.signalR.connection.onreconnected(() => {
        this.$emit('terminal-reconnected');
      });
    },

    handleServerMessage(msg) {
      switch (msg.constructor.name) {
        case 'TerminalMessage':
          this.terminal.write(msg.data);
          break;
        case 'SnapshotMessage':
          this.$emit('terminal-snapshot', msg);
          break;
        case 'ResizeAckMessage':
          this.isResizing = false;
          this.$emit('terminal-resize-complete');
          break;
      }
    },

    setupResizeHandler() {
      const resizeObserver = new ResizeObserver(this.handleResize);
      resizeObserver.observe(this.$refs.terminalContainer);
    },

    handleResize(entries) {
      if (this.isResizing) return;

      const rect = entries[0].contentRect;
      const fontSize = 14;
      const charWidth = fontSize * 0.6;
      const charHeight = fontSize * 1.2;

      const cols = Math.floor(rect.width / charWidth);
      const rows = Math.floor(rect.height / charHeight);

      clearTimeout(this.resizeTimer);
      this.resizeTimer = setTimeout(() => {
        this.performResize(cols, rows);
      }, 200);
    },

    async performResize(cols, rows) {
      if (cols === this.terminal.cols && rows === this.terminal.rows) {
        return;
      }

      this.isResizing = true;

      try {
        // 1. 本地 resize（减少闪烁）
        this.terminal.resize(cols, rows);
        this.fitAddon.fit();

        // 2. 通过 SignalR 发送 resize 请求
        await this.signalR.requestResize(cols, rows);
      } catch (error) {
        console.error('Resize failed:', error);
        this.isResizing = false;
      }
    }
  }
};
</script>
```

---

### 第四阶段：快照机制（事件驱动）

#### 后端：SnapshotManager.cs

```csharp
public class SnapshotManager
{
    private readonly Terminal _terminal;
    private readonly int _compressionLevel = 6;
    private DateTime _lastSnapshotTime = DateTime.MinValue;
    private TimeSpan _minSnapshotInterval = TimeSpan.FromSeconds(10);

    public event EventHandler<SnapshotData>? SnapshotReady;

    public SnapshotManager(Terminal terminal)
    {
        _terminal = terminal;
    }

    public void TriggerSnapshot(string reason = "manual")
    {
        // 检查是否需要触发
        if (ShouldCaptureSnapshot(reason))
        {
            _ = CaptureSnapshotAsync(reason);
        }
    }

    private bool ShouldCaptureSnapshot(string reason)
    {
        // 强制触发
        if (reason == "reconnect" || reason == "gap_detected")
        {
            return true;
        }

        // 条件触发：超过最小间隔
        if (DateTime.UtcNow - _lastSnapshotTime < _minSnapshotInterval)
        {
            return false;
        }

        // 内容变化检测（订阅 XTerm.NET 事件）
        return HasContentChanged();
    }

    private bool HasContentChanged()
    {
        // 需要订阅 Terminal 事件跟踪变化
        // 简化实现：返回 true（实际应检查状态变化）
        return true;
    }

    private async Task CaptureSnapshotAsync(string reason)
    {
        try
        {
            var buffer = _terminal.Buffer;

            // 捕获状态
            var snapshot = new SnapshotData
            {
                Cols = _terminal.Cols,
                Rows = _terminal.Rows,
                CursorX = buffer.X,
                CursorY = buffer.Y,
                YDisp = buffer.YDisp,
                YBase = buffer.YBase,
                ActiveBuffer = _terminal.ActiveBuffer.ToString(),
                Timestamp = DateTime.UtcNow
            };

            // 序列化并压缩（异步）
            var bufferData = await SerializeBufferAsync(buffer);
            snapshot.BufferData = bufferData;
            snapshot.BufferChecksum = ComputeChecksum(bufferData);

            SnapshotReady?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Snapshot capture failed: {ex.Message}");
        }
    }
}

public class SnapshotData
{
    public int Cols { get; set; }
    public int Rows { get; set; }
    public int CursorX { get; set; }
    public int CursorY { get; set; }
    public int YDisp { get; set; }
    public int YBase { get; set; }
    public string ActiveBuffer { get; set; }
    public DateTime Timestamp { get; set; }
    public string BufferData { get; set; } = string.Empty;
    public string BufferChecksum { get; set; } = string.Empty;
}
```

---

## 关键文件清单

### 新增文件（后端）
- `src/WebTerminal/Hubs/TerminalHub.cs` - SignalR Hub
- `src/WebTerminal/Services/TerminalSessionManager.cs` - 会话管理
- `src/WebTerminal/Services/TerminalSession.cs` - 单个会话
- `src/WebTerminal/Services/SnapshotService.cs` - 屏幕快照服务
- `src/WebTerminal/Services/HistoryService.cs` - 历史记录服务
- `src/WebTerminal/Models/MessageTypes.cs` - 消息类型定义

### 新增文件（前端）
- `src/components/Terminal.vue` - Vue 组件
- `src/utils/SignalRClient.js` - SignalR 客户端封装
- `src/utils/SnapshotHandler.js` - 快照恢复

### 依赖的 XTerm.NET 文件（只读）
- `src/XTerm.NET/Terminal.cs` - 用于快照时获取状态
- `src/XTerm.NET/Buffer/TerminalBuffer.cs` - 用于读取 YDisp, YBase, Lines

### 新增 NuGet 包（后端）
- `Microsoft.AspNetCore.SignalR` - SignalR 服务端
- `Microsoft.AspNetCore.SignalR.Client` - 客户端 JS 库（如需要单独客户端类型检查）

---

### 第二阶段：当前屏幕快照服务 + 第三阶段：最终历史记录服务（新增）

---

## 验证计划

### 测试场景

1. **Resize 同步测试**
   - 启动会话，执行 `ls -la` 输出内容
   - 快速调整窗口大小多次
   - 验证：终端显示正常，无乱码

2. **网络抖动测试**
   - 使用网络延迟模拟工具（如 Clumsy）
   - 模拟 100-500ms 延迟
   - 验证：输出连续，无乱码
   - SignalR 自动重连功能正常

3. **丢包/乱序测试**
   - 模拟网络问题
   - 验证：序号机制正常工作
   - 验证：大 gap 时快照恢复

4. **快照恢复测试**
   - 运行复杂应用（如 `htop`）
   - 触发快照（模拟重连）
   - 验证：状态恢复正确

5. **全屏应用测试**
   - 运行 `vim` 编辑文件
   - 运行 `tmux` 多窗口
   - 验证：缓冲区切换、状态恢复正确

6. **压力测试**
   - 同时执行多个会话
   - 持续输出大量数据（`cat large-file`）
   - 验证：性能可接受，内存不泄漏
   - 验证：背压机制生效，不会 OOM

### 运行测试

```bash
# 后端
cd src/WebTerminal
dotnet run

# 前端
cd frontend
npm run dev

# 访问 http://localhost:5173
```

---

## 性能考虑

1. **快照触发**：事件驱动，非定期，仅在必要时触发
2. **压缩级别**：Gzip level 6，平衡压缩比和速度
3. **ACK 频率**：累积确认（N 个包或超时），减少心跳
4. **防抖延迟**：Resize 200ms，避免频繁触发
5. **批量大小**：输出队列有界 Channel（上限 1000）
6. **序号溢出**：使用环形空间比较（`seq1 - seq2 < 2^31`），重连时重置
7. **背压机制**：消费者慢时暂停从 PTY 读取，避免内存暴涨

---

## 安全性考虑

1. **身份验证**：
   - SignalR 支持基于 Token 的身份验证（在 Startup.cs 配置）
   - 可选：JWT 或自定义认证

2. **PTY 隔离**：
   - 每个会话运行在独立容器中（资源限制）
   - 限制可执行的敏感命令
   - 超时自动清理僵尸进程

3. **输入过滤**：
   - 警告：终端通常需要原始输入，过滤需谨慎
   - 限制单条消息长度

4. **速率限制**：
   - 限制每秒消息数量
   - 防止资源耗尽攻击

---

## 扩展性

1. **多会话支持**：每个连接对应一个 TerminalSession
2. **会话持久化**：可扩展快照到 Redis/数据库
3. **协作模式**：SignalR 原生支持组，便于实现多终端协作
4. **录像回放**：记录所有序号消息，可回放历史
5. **水平扩展**：SignalR 可替换为其他传输层（Azure SignalR Service）
6. **容器化部署**：每个会话独立容器，资源限制
