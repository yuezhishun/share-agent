# ProcessRunner 使用指南

## 概述

ProcessRunner 是一个现代化的 .NET 子进程执行库，提供简洁易用的 API 来执行和管理终端进程。它基于 `System.Diagnostics.Process` 实现，支持链式调用、事件监听、进程管理和管道命令等高级功能。

## 核心特性

- **链式调用支持**：`AddArguments().SetWorkingDirectory().ExecuteAsync()` 流畅编程
- **事件驱动架构**：丰富的事件通知机制和实时输出处理
- **统一进程管理**：ProcessManager 支持多进程并发管理
- **管道命令支持**：支持将一个命令的输出管道到另一个命令
- **智能并发控制**：可配置的最大并发数和资源管理
- **异常处理完善**：全面的错误捕获和处理机制
- **跨平台支持**：Windows、Linux、macOS 全平台兼容

## 快速开始

### 基础命令执行

```csharp
// 基础命令执行
var result = await new ProcessCommand("cmd")
    .AddArguments("/c", "echo Hello World")
    .SetTimeout(TimeSpan.FromSeconds(30))
    .ExecuteAsync();

Console.WriteLine($"输出: {result.StandardOutput}");
Console.WriteLine($"进程ID: {result.ProcessId}");
Console.WriteLine($"退出码: {result.ExitCode}");
```

### 使用事件监听

```csharp
var result = await new ProcessCommand("npm")
    .AddArguments("run", "build")
    .SetTimeout(TimeSpan.FromMinutes(5))
    .OnStarted(pid => Console.WriteLine($"进程启动: {pid}"))
    .OnOutput(text => Console.WriteLine($"输出: {text}"))
    .OnError(error => Console.Error.WriteLine($"错误: {error}"))
    .OnExited(code => Console.WriteLine($"进程退出: {code}"))
    .ExecuteAsync();
```

## 使用场景

### 1. 自动化脚本执行

**适用场景**：需要执行系统命令、批处理脚本或自动化任务

```csharp
// 批量文件处理
var files = Directory.GetFiles("./docs", "*.md");
foreach (var file in files)
{
    var result = await new ProcessCommand("pandoc")
        .AddArguments(file, "-o", $"{file}.html")
        .SetTimeout(TimeSpan.FromMinutes(1))
        .ExecuteAsync();

    if (result.ExitCode == 0)
    {
        Console.WriteLine($"转换成功: {file}");
    }
}
```

**优势**：
- 自动处理进程创建和销毁
- 完整保留输出格式
- 支持设置超时和工作目录

### 2. 进程管理与并发控制

**适用场景**：需要同时管理多个进程，如批量测试、并行构建

```csharp
using var manager = new ProcessManager(maxConcurrency: 5);

// 注册不同的进程
var processIds = new List<string>();

var buildProcessId = manager.RegisterProcess(
    new ProcessCommand("npm")
        .AddArguments("run", "build")
        .SetTimeout(TimeSpan.FromMinutes(5)),
    metadata: new Dictionary<string, object> { ["Type"] = "Build" }
);

var testProcessId = manager.RegisterProcess(
    new ProcessCommand("npm")
        .AddArguments("test")
        .SetTimeout(TimeSpan.FromMinutes(10)),
    metadata: new Dictionary<string, object> { ["Type"] = "Test" }
);

// 监听所有进程事件
manager.ProcessStarted += (sender, e) =>
    Console.WriteLine($"进程启动: {e.ProcessId}");
manager.ProcessOutput += (sender, e) =>
    Console.WriteLine($"[{e.ProcessId}] {e.Content}");
manager.ProcessCompleted += (sender, e) =>
    Console.WriteLine($"进程完成: {e.ProcessId}");

// 批量启动
await manager.StartProcessesAsync(new[] { buildProcessId, testProcessId });

// 等待所有进程完成
var results = await manager.WaitAllProcessesAsync();
```

### 3. 管道命令

**适用场景**：需要将多个命令通过管道连接

```csharp
// 创建管道命令：查找文件并统计数量
var result = await new ProcessCommand("find")
    .AddArguments(".", "-name", "*.cs")
    .PipeTo(new ProcessCommand("wc").AddArguments("-l"))
    .ExecuteAsync();

Console.WriteLine($"C# 文件行数: {result.StandardOutput}");
```

### 4. 动态超时调整

**适用场景**：需要根据程序执行状态动态调整超时时间的任务

```csharp
public class AdaptiveTimeoutMonitor
{
    private readonly ProcessCommand _command;
    private TaskCompletionSource<bool> _tcs = new();

    public AdaptiveTimeoutMonitor()
    {
        _command = new ProcessCommand("gradle")
            .AddArguments("test")
            .SetTimeout(TimeSpan.FromMinutes(5))
            .OnOutput(OnOutputReceived)
            .OnExited(OnProcessExited);
    }

    public async Task<ProcessResult> ExecuteAsync()
    {
        var result = await _command.ExecuteAsync();
        await _tcs.Task;
        return result;
    }

    private void OnOutputReceived(string text)
    {
        if (text.Contains("Running tests"))
        {
            Console.WriteLine("检测到测试开始，延长超时时间...");
            _command.TryUpdateTimeout(TimeSpan.FromMinutes(15));
        }
    }

    private void OnProcessExited(int exitCode)
    {
        Console.WriteLine($"命令完成: 退出码 {exitCode}");
        _tcs.SetResult(true);
    }
}
```

### 5. 高级事件处理

**适用场景**：需要根据上下文信息进行复杂的事件处理逻辑

```csharp
public class DockerBuildMonitor
{
    private readonly ProcessCommand _command;
    private TaskCompletionSource<bool> _tcs = new();

    public DockerBuildMonitor()
    {
        _command = new ProcessCommand("docker")
            .AddArguments("build", "-t", "myapp:latest", ".")
            .SetTimeout(TimeSpan.FromHours(1))
            .OnStarted(OnProcessStarted)
            .OnOutput(OnOutputReceived)
            .OnTimeout(OnTimeoutOccurred)
            .OnExited(OnProcessExited);
    }

    public async Task<ProcessResult> ExecuteAsync()
    {
        var result = await _command.ExecuteAsync();
        await _tcs.Task;
        return result;
    }

    private void OnProcessStarted(int pid)
    {
        Console.WriteLine($"[开始] Docker 构建启动: PID {pid}");
    }

    private void OnOutputReceived(string text)
    {
        AnalyzeBuildOutput(text);
    }

    private void OnTimeoutOccurred(string message)
    {
        Console.WriteLine($"[超时] Docker 构建超时: {message}");
    }

    private void OnProcessExited(int exitCode)
    {
        Console.WriteLine($"[完成] Docker 构建完成: 退出码 {exitCode}");
        _tcs.SetResult(true);
    }

    private void AnalyzeBuildOutput(string output)
    {

### 6. 合并到 terminal-gateway-dotnet 后的 HTTP 能力

`apps/terminal-gateway-dotnet/TerminalGateway.Api` 现已内置 ProcessRunner 源码，并暴露以下接口：

- `POST /api/processes/run`：同步执行单个命令或管道命令，直接返回输出结果
- `POST /api/processes`：启动托管进程
- `GET /api/processes`：列出托管进程
- `GET /api/processes/{processId}`：获取托管进程状态与结果
- `GET /api/processes/{processId}/output`：读取托管进程输出历史
- `POST /api/processes/{processId}/wait?timeout_ms=5000`：等待托管进程完成
- `POST /api/processes/{processId}/stop`：停止托管进程

补充说明：

- `cwd` 会限制在网关的 `FILES_BASE_PATH` 下，避免越界执行
- 托管进程最大并发数可通过 `TERMINAL_PROCESS_MANAGER_MAX_CONCURRENCY` 配置
        if (output.Contains("Sending build context"))
        {
            Console.WriteLine($"[阶段1] {DateTime.Now:HH:mm:ss} - 发送构建上下文");
        }
        else if (output.Contains("Step"))
        {
            Console.WriteLine($"[阶段2] {DateTime.Now:HH:mm:ss} - 执行构建步骤");
        }
        else if (output.Contains("Successfully tagged"))
        {
            Console.WriteLine($"[完成] {DateTime.Now:HH:mm:ss} - 构建成功");
        }
    }
}
```

### 6. 开发工具集成

**适用场景**：IDE、编辑器等开发工具需要集成命令执行功能

```csharp
public class BuildMonitor
{
    private TaskCompletionSource<bool> _tcs = new();
    private bool _hasErrors = false;

    public async Task<bool> BuildProjectAsync(string projectPath, string configuration = "Release")
    {
        var command = new ProcessCommand("dotnet")
            .AddArguments("build", projectPath, "--configuration", configuration)
            .SetWorkingDirectory(Path.GetDirectoryName(projectPath))
            .SetEnvironmentVariables(("DOTNET_CLI_TELEMETRY_OPTOUT", "1"))
            .OnOutput(OnBuildOutput)
            .OnError(OnBuildError)
            .OnExited(OnBuildCompleted);

        var result = await command.ExecuteAsync();
        await _tcs.Task;

        return !_hasErrors;
    }

    private void OnBuildOutput(string text)
    {
        Console.WriteLine(text);

        if (text.Contains("warning"))
        {
            Console.WriteLine($"⚠️ 警告: {text}");
        }
    }

    private void OnBuildError(string error)
    {
        _hasErrors = true;
        Console.Error.WriteLine($"❌ 错误: {error}");
    }

    private void OnBuildCompleted(int exitCode)
    {
        Console.WriteLine($"编译完成: 退出码 {exitCode}");
        _tcs.SetResult(true);
    }
}
```

### 7. 测试自动化

**适用场景**：需要测试命令行工具或系统接口

```csharp
public async Task<bool> TestCommandLineTool()
{
    try
    {
        var result = await new ProcessCommand("my-cli-tool")
            .AddArguments("test", "--verbose")
            .SetTimeout(TimeSpan.FromMinutes(2))
            .ExecuteAsync();

        // 验证退出码
        if (result.ExitCode != 0)
        {
            Console.WriteLine($"测试失败: {result.StandardError}");
            return false;
        }

        // 验证输出内容
        if (!result.StandardOutput.Contains("All tests passed"))
        {
            Console.WriteLine("测试输出不符合预期");
            return false;
        }

        return true;
    }
    catch (TimeoutException)
    {
        Console.WriteLine("测试超时");
        return false;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"测试异常: {ex.Message}");
        return false;
    }
}
```

### 8. 媒体处理工具

**适用场景**：使用 FFmpeg 等工具进行音视频处理

```csharp
var tcs = new TaskCompletionSource<bool>();
var currentProgress = 0.0;

var ffmpegCommand = new ProcessCommand("ffmpeg")
    .AddArguments("-i", "input.mp4", "-c:v", "libx264", "-c:a", "aac", "output.mp4")
    .SetTimeout(TimeSpan.FromHours(2))
    .OnOutput(text =>
    {
        Console.WriteLine(text);

        // 解析进度信息
        if (text.Contains("time="))
        {
            var progress = ParseFFmpegProgress(text);
            currentProgress = progress;
            UpdateProgressBar(progress);
        }
    })
    .OnExited(exitCode =>
    {
        Console.WriteLine($"FFmpeg 完成: 退出码 {exitCode}");
        tcs.SetResult(true);
    });

var result = await ffmpegCommand.ExecuteAsync();
await tcs.Task;
```

### 9. 系统管理工具

**适用场景**：系统监控、资源管理、网络诊断等

```csharp
// 网络连通性测试
var networks = new[] { "8.8.8.8", "1.1.1.1", "114.114.114.114" };

foreach (var ip in networks)
{
    try
    {
        var result = await new ProcessCommand("ping")
            .AddArguments(ip, "-n", "4")
            .SetTimeout(TimeSpan.FromSeconds(30))
            .ExecuteAsync();

        var success = result.StandardOutput.Contains("Reply from");
        Console.WriteLine($"{ip}: {(success ? "✅ 连通" : "❌ 不通")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{ip}: 测试失败 - {ex.Message}");
    }
}
```

### 10. 容器和虚拟化

**适用场景**：Docker、WSL 等容器环境的管理

```csharp
// Docker 容器管理
var containers = await new ProcessCommand("docker")
    .AddArguments("ps", "--format", "table {{.Names}}\t{{.Status}}")
    .SetTimeout(TimeSpan.FromSeconds(10))
    .ExecuteAsync();

Console.WriteLine("运行中的容器:");
Console.WriteLine(containers.StandardOutput);

// 启动新容器
await new ProcessCommand("docker")
    .AddArguments("run", "-d", "--name", "my-app", "nginx:latest")
    .SetTimeout(TimeSpan.FromSeconds(30))
    .ExecuteAsync();
```

## 配置方法

### ProcessCommand 配置

```csharp
var command = new ProcessCommand("executable")
    // 参数配置
    .AddArguments("arg1", "arg2", "arg3")

    // 工作目录
    .SetWorkingDirectory("/path/to/directory")

    // 环境变量
    .SetEnvironmentVariable("VAR1", "value1")
    .SetEnvironmentVariables(new[] { ("VAR2", "value2"), ("VAR3", "value3") })

    // 超时设置
    .SetTimeout(TimeSpan.FromMinutes(5))

    // 结果验证
    .WithValidation(CommandResultValidation.ZeroExitCode);
```

### 监听器配置

```csharp
var command = new ProcessCommand("npm")
    .AddArguments("start")

    // 标准输出监听器
    .OnOutput(text => Console.WriteLine(text))

    // 异步输出监听器
    .OnOutputAsync(async text => await logger.LogAsync(text))

    // 标准错误监听器
    .OnError(error => Console.Error.WriteLine(error))

    // 进程生命周期监听器
    .OnStarted(pid => Console.WriteLine($"进程启动: {pid}"))
    .OnExited(code => Console.WriteLine($"进程退出: {code}"))
    .OnTimeout(message => Console.WriteLine($"超时: {message}"));
```

### ProcessManager 配置

```csharp
using var manager = new ProcessManager(maxConcurrency: 5);

// 注册进程
var processId = manager.RegisterProcess(
    new ProcessCommand("node")
        .AddArguments("script.js")
        .SetTimeout(TimeSpan.FromMinutes(5)),
    metadata: new Dictionary<string, object> { ["JobId"] = "12345" }
);

// 启动进程
await manager.StartProcessAsync(processId);

// 等待进程完成
var result = await manager.WaitProcessAsync(processId);

// 获取进程状态
var status = manager.GetProcessStatus(processId);

// 获取进程输出
var outputs = manager.GetProcessOutput(processId);
```

## 错误处理最佳实践

### 1. 异常类型处理

```csharp
try
{
    var result = await new ProcessCommand("some-command")
        .AddArguments("args")
        .SetTimeout(TimeSpan.FromSeconds(30))
        .ExecuteAsync();
}
catch (FileNotFoundException)
{
    Console.WriteLine("命令不存在，请检查是否已安装");
}
catch (TimeoutException)
{
    Console.WriteLine("命令执行超时");
}
catch (OperationCanceledException)
{
    Console.WriteLine("操作被取消");
}
catch (UnauthorizedAccessException)
{
    Console.WriteLine("权限不足");
}
catch (ProcessExecutionException ex)
{
    Console.WriteLine($"进程执行失败: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"未知错误: {ex.Message}");
}
```

### 2. 退出码检查

```csharp
var result = await new ProcessCommand("command")
    .AddArguments("args")
    .ExecuteAsync();

if (result.ExitCode != 0)
{
    Console.WriteLine($"命令执行失败，退出码: {result.ExitCode}");
    if (!string.IsNullOrEmpty(result.StandardError))
    {
        Console.WriteLine($"错误信息: {result.StandardError}");
    }
    return;
}

// 处理成功情况
Console.WriteLine(result.StandardOutput);
```

## 最佳实践

### 1. 资源管理

```csharp
// 始终使用 using 语句确保资源释放
using var manager = new ProcessManager();
// 使用管理器...
// 管理器会在作用域结束时自动释放所有资源
```

### 2. 并发控制

```csharp
// 根据系统资源设置合适的并发数
var maxConcurrency = Math.Min(Environment.ProcessorCount * 2, 10);
using var manager = new ProcessManager(maxConcurrency);

// 监控并发使用情况
manager.ProcessStarted += (sender, e) =>
{
    Console.WriteLine($"当前运行数: {manager.RunningProcessCount}/{manager.MaxConcurrency}");
};
```

### 3. 监听器最佳实践

```csharp
// 监听器应该快速处理，避免阻塞主线程
manager.ProcessOutput += (sender, e) =>
{
    // 快速处理输出
    Console.WriteLine(e.Content);

    // 异步处理耗时操作
    _ = Task.Run(async () =>
    {
        await logger.LogAsync($"[{e.ProcessId}] {e.Content}");
    });
};
```

## 性能考虑

1. **超时设置**：为长时间运行的命令设置合理的超时时间
2. **缓冲区大小**：默认使用 4KB 缓冲区，可根据需要调整
3. **并发控制**：避免同时启动过多进程，可能导致系统资源不足
4. **内存管理**：及时释放资源，避免内存泄漏

## 兼容性

- **.NET 8.0+**：支持现代 .NET 平台
- **Windows**：完全支持
- **Linux/macOS**：完全支持
- **Docker 容器**：支持在容器环境中运行

## 构建和部署

### 构建项目

```bash
# 构建整个项目
dotnet build

# Release 构建
dotnet build -c Release
```

### 运行测试

```bash
dotnet test
```

## 项目结构

```
CliExecutor/
├── ProcessCommand.cs           # 进程命令配置
├── ProcessExecutor.cs          # 命令执行器
├── ProcessManager.cs           # 进程管理器
├── ProcessCommandContext.cs    # 执行上下文
├── ProcessCommandEvent.cs      # 事件定义
├── ProcessExecutionException.cs # 异常定义
├── CommandResultValidation.cs  # 结果验证
├── PathResolver.cs             # 路径解析
├── PipedProcessCommand.cs      # 管道命令
├── PipedProcessExecutor.cs     # 管道执行器
├── Tests/                      # 测试项目
└── README.md                   # 本文档
```

## 故障排除

### 常见问题

1. **进程无法启动**
   - 检查命令路径是否正确
   - 确认程序文件存在且可执行
   - 验证工作目录路径

2. **权限问题**
   - Windows: 检查 UAC 设置
   - Linux/macOS: 检查文件权限和执行权限

3. **字符编码问题**
   - 确保使用正确的编码（UTF-8）
   - 检查终端环境的编码设置

4. **并发限制问题**
   - 检查最大并发数设置是否合理
   - 确认进程正常结束，没有僵尸进程

## 贡献指南

欢迎贡献代码！请遵循以下步骤：

1. Fork 项目
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 创建 Pull Request

### 代码规范

- 使用 C# 12.0 特性
- 遵循 Microsoft 编码规范
- 添加适当的 XML 文档注释
- 确保所有公共 API 都有相应的单元测试
- 保持一致的代码风格和命名约定

## 许可证

本项目采用 MIT 许可证。

---

**ProcessRunner** - 让 .NET 应用拥有强大的进程执行能力！
