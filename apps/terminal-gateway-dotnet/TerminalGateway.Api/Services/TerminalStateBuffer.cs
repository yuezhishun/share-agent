using System.Text;

namespace TerminalGateway.Api.Services;

public sealed class TerminalStateBuffer
{
    private readonly List<string> _visibleLines = [];
    private readonly StringBuilder _currentLine = new();
    private readonly int _maxVisibleLines;

    public TerminalStateBuffer(int maxVisibleLines = 1000)
    {
        _maxVisibleLines = Math.Max(50, maxVisibleLines);
    }

    public IReadOnlyList<string> VisibleLines
    {
        get
        {
            var lines = _visibleLines.ToList();
            if (_currentLine.Length > 0)
            {
                lines.Add(_currentLine.ToString());
            }

            return lines;
        }
    }

    public IReadOnlyList<string> ApplyChunk(string chunk)
    {
        var committedLines = new List<string>();
        if (string.IsNullOrEmpty(chunk))
        {
            return committedLines;
        }

        foreach (var c in chunk)
        {
            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                var line = _currentLine.ToString();
                _currentLine.Clear();
                _visibleLines.Add(line);
                committedLines.Add(line);
                if (_visibleLines.Count > _maxVisibleLines)
                {
                    _visibleLines.RemoveAt(0);
                }
                continue;
            }

            _currentLine.Append(c);
        }

        return committedLines;
    }
}
