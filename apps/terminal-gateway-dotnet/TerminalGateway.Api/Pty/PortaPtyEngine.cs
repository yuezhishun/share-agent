using System.Diagnostics;
using System.Text;

namespace TerminalGateway.Api.Pty;

// Process-based adapter with the same surface as the future Porta.Pty engine.
public sealed class PortaPtyEngine : IPtyEngine
{
    public Task<IPtyRuntimeSession> CreateAsync(PtyLaunchOptions options, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = options.Executable,
            WorkingDirectory = options.Cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in options.Args)
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (var pair in options.Env)
        {
            psi.Environment[pair.Key] = pair.Value;
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();
        var runtime = new ProcessPtyRuntimeSession(process);
        runtime.StartBackgroundReaders();
        return Task.FromResult<IPtyRuntimeSession>(runtime);
    }

    private sealed class ProcessPtyRuntimeSession : IPtyRuntimeSession
    {
        private readonly Process _process;
        private readonly CancellationTokenSource _cts = new();

        public ProcessPtyRuntimeSession(Process process)
        {
            _process = process;
            _process.Exited += (_, _) => Exited?.Invoke(_process.HasExited ? _process.ExitCode : null);
        }

        public int Pid => _process.Id;
        public event Action<string>? OutputReceived;
        public event Action<int?>? Exited;

        public void StartBackgroundReaders()
        {
            _ = ReadLoopAsync(_process.StandardOutput, _cts.Token);
            _ = ReadLoopAsync(_process.StandardError, _cts.Token);
        }

        private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            var buffer = new char[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                OutputReceived?.Invoke(new string(buffer, 0, read));
            }
        }

        public async Task WriteAsync(string data, CancellationToken cancellationToken = default)
        {
            if (_process.HasExited)
            {
                return;
            }

            await _process.StandardInput.WriteAsync(data.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }

        public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task TerminateAsync(string signal, CancellationToken cancellationToken = default)
        {
            if (_process.HasExited)
            {
                return Task.CompletedTask;
            }

            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            _process.Dispose();
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
