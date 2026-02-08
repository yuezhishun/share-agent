using System.Diagnostics;

namespace PtyAgent.Api.Runtime.Terminal;

public sealed class ProcessTerminalBackend : ITerminalBackend
{
    public string Name => "process";

    public Task<ITerminalSession> StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "-lc \"" + Escape(options.Command) + "\"",
                WorkingDirectory = options.Workdir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.Start();

        ITerminalSession session = new ProcessTerminalSession(process);
        return Task.FromResult(session);
    }

    private static string Escape(string command)
    {
        return command.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private sealed class ProcessTerminalSession : ITerminalSession
    {
        private readonly Process _process;

        public ProcessTerminalSession(Process process)
        {
            _process = process;
            _process.Exited += (_, _) => Exited?.Invoke(this, new TerminalExitedEventArgs(_process.ExitCode));
        }

        public event EventHandler<TerminalExitedEventArgs>? Exited;

        public int Pid => _process.Id;
        public string BackendName => "process";
        public TextReader OutputReader => _process.StandardOutput;
        public TextReader? ErrorReader => _process.StandardError;

        public async Task SendInputAsync(string input, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _process.StandardInput.WriteLineAsync(input);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }

        public Task TerminateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
