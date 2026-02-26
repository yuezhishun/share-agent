using System.Text;
using Porta.Pty;

namespace TerminalGateway.Api.Pty;

public sealed class PortaPtyEngine : IPtyEngine
{
    public async Task<IPtyRuntimeSession> CreateAsync(PtyLaunchOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Executable))
        {
            throw new ArgumentException("executable is required", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.Cwd))
        {
            throw new ArgumentException("cwd is required", nameof(options));
        }

        var ptyOptions = new PtyOptions
        {
            App = options.Executable,
            Cwd = options.Cwd,
            Cols = options.Cols,
            Rows = options.Rows,
            CommandLine = options.Args.ToArray(),
            Environment = new Dictionary<string, string>(options.Env, StringComparer.Ordinal)
        };

        var conn = await PtyProvider.SpawnAsync(ptyOptions, cancellationToken);
        return new PortaRuntimeSession(conn);
    }

    private sealed class PortaRuntimeSession : IPtyRuntimeSession
    {
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

        private readonly IPtyConnection _connection;
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Task _readerLoop;

        public PortaRuntimeSession(IPtyConnection connection)
        {
            _connection = connection;
            _connection.ProcessExited += (_, e) => Exited?.Invoke(e.ExitCode);
            _readerLoop = Task.Run(ReadLoopAsync, CancellationToken.None);
        }

        public async Task WriteAsync(string data, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(data))
            {
                return;
            }

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                var payload = Utf8.GetBytes(data);
                await _connection.WriterStream.WriteAsync(payload.AsMemory(0, payload.Length), cancellationToken);
                await _connection.WriterStream.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default)
        {
            _connection.Resize(cols, rows);
            return Task.CompletedTask;
        }

        public Task TerminateAsync(string signal, CancellationToken cancellationToken = default)
        {
            _connection.Kill();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                _readerLoop.GetAwaiter().GetResult();
            }
            catch
            {
            }

            _connection.Dispose();
            _writeLock.Dispose();
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }

        public int Pid => _connection.Pid;

        public event Action<string>? OutputReceived;
        public event Action<int?>? Exited;

        private async Task ReadLoopAsync()
        {
            var buffer = new byte[4096];
            while (!_cts.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await _connection.ReaderStream.ReadAsync(buffer.AsMemory(0, buffer.Length), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                OutputReceived?.Invoke(Utf8.GetString(buffer, 0, read));
            }
        }
    }

}
