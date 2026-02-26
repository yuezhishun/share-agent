# Porta.Pty 使用说明

> 状态：参考文档（未废弃）
>
> 说明：本文档用于记录 Porta.Pty 通用用法。项目实际集成方式请以 `apps/terminal-gateway-dotnet/TerminalGateway.Api` 源码和 `docs/terminal-gateway-dotnet.md` 为准。

Porta.Pty 是一个跨平台的伪终端 (PTY) 库，用于 .NET。它允许你生成和与终端进程进行交互，支持 Windows、Linux 和 macOS。

## 目录

- [安装](#安装)
- [快速开始](#快速开始)
- [基础用法](#基础用法)
- [平台特定示例](#平台特定示例)
- [高级用法](#高级用法)
- [API 参考](#api-参考)
- [常见问题](#常见问题)

---

## 安装

### 通过 NuGet 安装

```bash
dotnet add package Porta.Pty
```

### 通过 Package Manager Console 安装

```powershell
Install-Package Porta.Pty
```

### 支持的 .NET 版本

- .NET Standard 2.0
- .NET Core 2.0+
- .NET 5+
- .NET Framework 4.6.1+

---

## 快速开始

下面是一个最简单的示例，展示如何生成一个终端进程：

```csharp
using Porta.Pty;
using System.Text;

// 配置终端选项
var options = new PtyOptions
{
    App = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
        : "/bin/bash",
    Cwd = Environment.CurrentDirectory,
    Cols = 120,
    Rows = 30
};

// 生成终端进程
using IPtyConnection terminal = await PtyProvider.SpawnAsync(options, CancellationToken.None);

// 写入命令
byte[] command = Encoding.UTF8.GetBytes("echo Hello World\r");
await terminal.WriterStream.WriteAsync(command, 0, command.Length);
await terminal.WriterStream.FlushAsync();

// 读取输出
byte[] buffer = new byte[4096];
int bytesRead = await terminal.ReaderStream.ReadAsync(buffer, 0, buffer.Length);
string output = Encoding.UTF8.GetString(buffer, 0, bytesRead);
Console.WriteLine(output);
```

---

## 基础用法

### PtyOptions 配置

`PtyOptions` 类用于配置生成的终端进程：

| 属性 | 类型 | 必填 | 描述 |
|------|------|------|------|
| `App` | `string` | 是 | 要生成的可执行文件路径 |
| `Cwd` | `string` | 是 | 工作目录 |
| `CommandLine` | `string[]` | 否 | 命令行参数 |
| `Name` | `string?` | 否 | 终端名称（可选） |
| `Cols` | `int` | 否 | 初始列数（默认由系统决定） |
| `Rows` | `int` | 否 | 初始行数（默认由系统决定） |
| `VerbatimCommandLine` | `bool` | 否 | 是否不引用参数（默认 false） |
| `Environment` | `IDictionary<string, string>` | 否 | 环境变量 |

### 完整配置示例

```csharp
var options = new PtyOptions
{
    Name = "MyTerminal",
    App = "/bin/bash",
    Cwd = "/home/user",
    Cols = 80,
    Rows = 24,
    CommandLine = new[] { "-c", "ls -la" },
    Environment = new Dictionary<string, string>
    {
        { "MY_VAR", "value" },
        { "PATH", "/custom/path:$PATH" }
    }
};
```

### 移除环境变量

将环境变量的值设置为空字符串即可移除：

```csharp
options.Environment = new Dictionary<string, string>
{
    { "TO_REMOVE", "" }  // 这将移除 TO_REMOVE 环境变量
};
```

---

## 平台特定示例

### Windows 示例

```csharp
var options = new PtyOptions
{
    App = @"C:\Windows\System32\cmd.exe",
    Cwd = @"C:\",
    Cols = 120,
    Rows = 30,
    CommandLine = new[] { "/c", "dir" }
};

using IPtyConnection terminal = await PtyProvider.SpawnAsync(options, CancellationToken.None);
```

### Linux 示例

```csharp
var options = new PtyOptions
{
    App = "/bin/bash",
    Cwd = "/home/user",
    Cols = 120,
    Rows = 30,
    CommandLine = new[] { "-l" }  // 登录 shell
};

using IPtyConnection terminal = await PtyProvider.SpawnAsync(options, CancellationToken.None);
```

### macOS 示例

```csharp
var options = new PtyOptions
{
    App = "/bin/zsh",
    Cwd = "/Users/user",
    Cols = 120,
    Rows = 30
};

using IPtyConnection terminal = await PtyProvider.SpawnAsync(options, CancellationToken.None);
```

---

## 高级用法

### 处理进程退出

```csharp
terminal.ProcessExited += (sender, e) =>
{
    Console.WriteLine($"终端已退出，退出码: {e.ExitCode}");
};

// 等待进程退出
bool exited = terminal.WaitForExit(5000);  // 等待 5 秒
if (exited)
{
    Console.WriteLine($"退出码: {terminal.ExitCode}");
}
```

### 调整终端大小

```csharp
// 动态调整终端尺寸
terminal.Resize(100, 30);
```

### 终止进程

```csharp
// 立即终止进程
terminal.Kill();
```

### 持续读取输出

```csharp
async Task ReadOutputAsync(IPtyConnection terminal)
{
    byte[] buffer = new byte[4096];

    while (true)
    {
        int bytesRead = await terminal.ReaderStream.ReadAsync(buffer, 0, buffer.Length);
        if (bytesRead == 0)
            break;

        string output = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        Console.Write(output);
    }
}

// 使用 Task.Run 在后台读取
var readTask = Task.Run(() => ReadOutputAsync(terminal));
```

### 交互式终端示例

```csharp
async Task RunInteractiveTerminalAsync()
{
    var options = new PtyOptions
    {
        App = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : "/bin/bash",
        Cwd = Environment.CurrentDirectory,
        Cols = 120,
        Rows = 30
    };

    using IPtyConnection terminal = await PtyProvider.SpawnAsync(options, CancellationToken.None);

    // 处理退出事件
    terminal.ProcessExited += (sender, e) =>
    {
        Console.WriteLine($"\n[进程退出: {e.ExitCode}]");
    };

    // 启动输出读取任务
    var readTask = Task.Run(async () =>
    {
        byte[] buffer = new byte[4096];
        while (true)
        {
            int bytesRead = await terminal.ReaderStream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0)
                break;
            Console.Write(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }
    });

    // 读取用户输入并发送到终端
    while (true)
    {
        string? input = Console.ReadLine();
        if (input == null)
            break;

        byte[] bytes = Encoding.UTF8.GetBytes(input + "\r\n");
        await terminal.WriterStream.WriteAsync(bytes, 0, bytes.Length);
        await terminal.WriterStream.FlushAsync();
    }

    await readTask;
}
```

### 运行复杂命令

```csharp
// 方式一：使用 CommandLine 参数
var options = new PtyOptions
{
    App = "/bin/bash",
    CommandLine = new[] { "-c", "echo 'Hello' && echo 'World'" },
    Cwd = Environment.CurrentDirectory
};

// 方式二：使用 VerbatimCommandLine
var options = new PtyOptions
{
    App = "/bin/bash",
    CommandLine = new[] { "-c", "echo 'Hello' && echo 'World'" },
    VerbatimCommandLine = false,  // 默认值，参数会被引用
    Cwd = Environment.CurrentDirectory
};
```

---

## API 参考

### PtyProvider

静态类，提供终端生成功能。

#### SpawnAsync

```csharp
public static Task<IPtyConnection> SpawnAsync(
    PtyOptions options,
    CancellationToken cancellationToken)
```

生成一个新的伪终端进程。

**参数：**
- `options` - 终端配置选项
- `cancellationToken` - 取消令牌

**返回：** `IPtyConnection` 实例

---

### IPtyConnection

表示与伪终端进程的活动连接。

#### 属性

| 属性 | 类型 | 描述 |
|------|------|------|
| `ReaderStream` | `Stream` | 用于从终端读取数据的流 |
| `WriterStream` | `Stream` | 用于向终端写入数据的流 |
| `Pid` | `int` | 终端进程 ID |
| `ExitCode` | `int` | 进程退出码（进程退出后可用） |

#### 事件

```csharp
event EventHandler<PtyExitedEventArgs>? ProcessExited
```

当终端进程退出时触发。

**PtyExitedEventArgs:**
- `ExitCode` - 进程退出码

#### 方法

```csharp
// 调整终端大小
void Resize(int cols, int rows)

// 立即终止进程
void Kill()

// 等待进程退出
// 返回: 如果进程在超时时间内退出返回 true，否则返回 false
bool WaitForExit(int milliseconds)
```

---

## 常见问题

### Q: 为什么需要原生库？

A: 从 .NET 7 开始，运行时默认启用 W^X (Write XOR Execute) 内存保护。当从托管代码调用 `fork()` 时，fork 的子进程可能会违反 W^X 不变量。通过将 fork+exec 完全委托给原生 C 代码，避免了在 fork 的子进程中运行任何托管的 .NET 代码，从而完全消除了 W^X 冲突。

### Q: 如何确定终端大小？

A: 初始大小通过 `PtyOptions` 的 `Cols` 和 `Rows` 属性设置。运行时可以通过 `Resize()` 方法动态调整。

### Q: 如何处理二进制输出？

A: 直接使用 `ReaderStream` 和 `WriterStream` 进行二进制数据的读写，不需要进行编码转换：

```csharp
byte[] buffer = new byte[4096];
int bytesRead = await terminal.ReaderStream.ReadAsync(buffer, 0, buffer.Length);
// buffer 包含原始二进制数据
```

### Q: Windows 和 Unix 平台有什么区别？

A:
- **Windows**: 使用 ConPTY API，通过 `Vanara.PInvoke.Kernel32` 进行 P/Invoke
- **Unix**: 使用 POSIX PTY 函数 (`forkpty`, `openpty`)，通过原生 C 库调用

API 使用方式在所有平台上保持一致。

### Q: 如何在 Docker 容器中使用？

A: 在 Linux Docker 容器中使用时，确保容器有足够的权限创建 PTY：

```dockerfile
# Dockerfile
RUN apt-get update && apt-get install -y util-linux
```

```bash
# 运行容器时添加 --tty 参数
docker run -it --tty your-image
```

### Q: 终端输出中包含 ANSI 转义序列，如何处理？

A: ANSI 转义序列是正常的终端输出，用于控制颜色、光标位置等。如果需要纯文本输出，可以使用专门的 ANSI 解析库如 `Console.ANSITerm` 或 `Spectre.Console`。

### Q: 如何运行长时间运行的命令？

A: 使用 `WaitForExit()` 方法可以等待进程完成，或通过 `ProcessExited` 事件监听退出：

```csharp
// 阻塞等待，最多等待 30 秒
bool finished = terminal.WaitForExit(30000);

// 或使用事件监听
terminal.ProcessExited += (s, e) => {
    Console.WriteLine($"命令完成，退出码: {e.ExitCode}");
};
```

---

## 许可证

MIT License - 详见 [LICENSE](../LICENSE) 文件。

## 更多信息

- [GitHub 仓库](https://github.com/tomlm/Porta.Pty)
- [NuGet 包](https://www.nuget.org/packages/Porta.Pty/)
