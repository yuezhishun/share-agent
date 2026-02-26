using XTerm;
using XTerm.Options;

namespace TerminalGateway.Api.Tests.Oracle;

public sealed record OracleFrame(int Cols, int Rows, int CursorX, int CursorY, IReadOnlyList<string> VisibleLines);

public sealed class XTermOracleAdapter : IDisposable
{
    private readonly Terminal _terminal;

    public XTermOracleAdapter(int cols = 80, int rows = 25)
    {
        _terminal = new Terminal(new TerminalOptions
        {
            Cols = Math.Max(1, cols),
            Rows = Math.Max(1, rows),
            ConvertEol = true,
            Scrollback = 4000
        });
    }

    public void Feed(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        _terminal.Write(chunk);
    }

    public void Resize(int cols, int rows)
    {
        _terminal.Resize(Math.Max(1, cols), Math.Max(1, rows));
    }

    public OracleFrame Export()
    {
        var lines = _terminal.GetVisibleLines() ?? [];
        return new OracleFrame(
            _terminal.Cols,
            _terminal.Rows,
            _terminal.Buffer.X,
            _terminal.Buffer.Y,
            lines.ToList());
    }

    public void Dispose()
    {
        _terminal.Dispose();
    }
}
