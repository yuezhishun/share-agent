using TerminalGateway.Api.Models;
using TerminalGateway.Api.Pty;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Tests;

public class InstanceManagerLaunchArgsTests
{
    [Fact]
    public async Task CreateAsync_ShouldInjectInteractiveFlag_ForBashWithoutInteractiveMode()
    {
        var engine = new CapturePtyEngine();
        var manager = CreateManager(engine);
        var basePath = CreateBaseDirectory();

        try
        {
            await manager.CreateAsync(new CreateInstanceRequest
            {
                Command = "bash",
                Args = ["-l"],
                Cwd = basePath
            }, basePath, CancellationToken.None);

            Assert.NotNull(engine.LastOptions);
            Assert.Equal(["-i", "-l"], engine.LastOptions!.Args);
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldNotInjectInteractiveOrLoginFlag_WhenCommandFlagExists()
    {
        var engine = new CapturePtyEngine();
        var manager = CreateManager(engine);
        var basePath = CreateBaseDirectory();

        try
        {
            await manager.CreateAsync(new CreateInstanceRequest
            {
                Command = "bash",
                Args = ["-lc", "echo hello"],
                Cwd = basePath
            }, basePath, CancellationToken.None);

            Assert.NotNull(engine.LastOptions);
            Assert.Equal(["-lc", "echo hello"], engine.LastOptions!.Args);
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldInjectInteractiveAndLoginFlags_WhenBashHasNoArgs()
    {
        var engine = new CapturePtyEngine();
        var manager = CreateManager(engine);
        var basePath = CreateBaseDirectory();

        try
        {
            await manager.CreateAsync(new CreateInstanceRequest
            {
                Command = "bash",
                Cwd = basePath
            }, basePath, CancellationToken.None);

            Assert.NotNull(engine.LastOptions);
            Assert.Equal(["-i", "-l"], engine.LastOptions!.Args);
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldInjectInteractiveOnly_WhenShellIsSh()
    {
        var engine = new CapturePtyEngine();
        var manager = CreateManager(engine);
        var basePath = CreateBaseDirectory();

        try
        {
            await manager.CreateAsync(new CreateInstanceRequest
            {
                Command = "sh",
                Cwd = basePath
            }, basePath, CancellationToken.None);

            Assert.NotNull(engine.LastOptions);
            Assert.Equal(["-i"], engine.LastOptions!.Args);
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldUseConfiguredGitBashPath_OnWindows()
    {
        var engine = new CapturePtyEngine();
        const string gitBashPath = @"C:\Program Files\Git\bin\bash.exe";
        var manager = CreateManager(
            engine,
            gitBashPath: gitBashPath,
            isWindows: () => true,
            fileExists: path => string.Equals(path, gitBashPath, StringComparison.OrdinalIgnoreCase));
        var basePath = CreateBaseDirectory();

        try
        {
            await manager.CreateAsync(new CreateInstanceRequest
            {
                Command = "bash",
                Cwd = basePath
            }, basePath, CancellationToken.None);

            Assert.NotNull(engine.LastOptions);
            Assert.Equal(gitBashPath, engine.LastOptions!.Executable);
            Assert.Equal(["-i", "-l"], engine.LastOptions.Args);
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldPreserveExplicitBashCommandArgs_WhenMappedToGitBashOnWindows()
    {
        var engine = new CapturePtyEngine();
        const string gitBashPath = @"C:\Program Files\Git\bin\bash.exe";
        var manager = CreateManager(
            engine,
            gitBashPath: gitBashPath,
            isWindows: () => true,
            fileExists: path => string.Equals(path, gitBashPath, StringComparison.OrdinalIgnoreCase));
        var basePath = CreateBaseDirectory();

        try
        {
            await manager.CreateAsync(new CreateInstanceRequest
            {
                Command = "bash -lc \"echo hi\"",
                Cwd = basePath
            }, basePath, CancellationToken.None);

            Assert.NotNull(engine.LastOptions);
            Assert.Equal(gitBashPath, engine.LastOptions!.Executable);
            Assert.Equal(["-lc", "echo hi"], engine.LastOptions.Args);
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldFallbackToPowerShell_WhenWindowsGitBashUnavailable()
    {
        var engine = new CapturePtyEngine();
        var manager = CreateManager(
            engine,
            isWindows: () => true,
            fileExists: _ => false,
            resolveExecutableFromPath: (_, _) => string.Empty);
        var basePath = CreateBaseDirectory();

        try
        {
            await manager.CreateAsync(new CreateInstanceRequest
            {
                Command = "bash",
                Cwd = basePath
            }, basePath, CancellationToken.None);

            Assert.NotNull(engine.LastOptions);
            Assert.Equal("powershell.exe", engine.LastOptions!.Executable);
            Assert.Equal(["-NoLogo", "-NoExit"], engine.LastOptions.Args);
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldResolveWindowsDefaultShell_WhenCommandIsEmpty()
    {
        var engine = new CapturePtyEngine();
        const string gitBashPath = @"C:\Program Files\Git\bin\bash.exe";
        var manager = CreateManager(
            engine,
            gitBashPath: gitBashPath,
            isWindows: () => true,
            fileExists: path => string.Equals(path, gitBashPath, StringComparison.OrdinalIgnoreCase));
        var basePath = CreateBaseDirectory();

        try
        {
            await manager.CreateAsync(new CreateInstanceRequest
            {
                Command = "",
                Cwd = basePath
            }, basePath, CancellationToken.None);

            Assert.NotNull(engine.LastOptions);
            Assert.Equal(gitBashPath, engine.LastOptions!.Executable);
            Assert.Equal(["-i", "-l"], engine.LastOptions.Args);
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldPrefixPathAndDeduplicate_WhenPathPrefixesConfigured()
    {
        var engine = new CapturePtyEngine();
        var manager = CreateManager(engine, ["/www/server/nodejs/v22.22.0/bin", "/opt/custom/bin"]);
        var basePath = CreateBaseDirectory();

        try
        {
            await manager.CreateAsync(new CreateInstanceRequest
            {
                Command = "bash",
                Args = ["-lc", "echo ok"],
                Cwd = basePath,
                Env = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["PATH"] = "/usr/local/bin:/www/server/nodejs/v22.22.0/bin:/usr/bin"
                }
            }, basePath, CancellationToken.None);

            Assert.NotNull(engine.LastOptions);
            Assert.NotNull(engine.LastOptions!.Env);
            Assert.True(engine.LastOptions.Env.TryGetValue("PATH", out var pathValue));
            Assert.Equal(
                string.Join(Path.PathSeparator, ["/www/server/nodejs/v22.22.0/bin", "/opt/custom/bin", "/usr/local/bin", "/usr/bin"]),
                pathValue);
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    private static InstanceManager CreateManager(
        IPtyEngine engine,
        IReadOnlyList<string>? pathPrefixes = null,
        string? gitBashPath = null,
        Func<bool>? isWindows = null,
        Func<string, bool>? fileExists = null,
        Func<string, IDictionary<string, string>, string?>? resolveExecutableFromPath = null)
    {
        return new InstanceManager(
            engine,
            historyLimit: 200,
            rawReplayMaxBytes: 1024 * 1024,
            defaultCols: 120,
            defaultRows: 34,
            nodeId: "node-a",
            nodeName: "Node A",
            nodeRole: "master",
            pathPrefixes: pathPrefixes,
            gitBashPath: gitBashPath,
            isWindows: isWindows,
            fileExists: fileExists,
            resolveExecutableFromPath: resolveExecutableFromPath);
    }

    private static string CreateBaseDirectory()
    {
        var basePath = Path.Combine(Path.GetTempPath(), $"tg-instance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        return basePath;
    }

    private sealed class CapturePtyEngine : IPtyEngine
    {
        public PtyLaunchOptions? LastOptions { get; private set; }

        public Task<IPtyRuntimeSession> CreateAsync(PtyLaunchOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult<IPtyRuntimeSession>(new FakeRuntimeSession());
        }
    }

    private sealed class FakeRuntimeSession : IPtyRuntimeSession
    {
        public int Pid => 12345;

        public event Action<string>? OutputReceived
        {
            add { }
            remove { }
        }

        public event Action<int?>? Exited
        {
            add { }
            remove { }
        }

        public Task WriteAsync(string data, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task TerminateAsync(string signal, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
