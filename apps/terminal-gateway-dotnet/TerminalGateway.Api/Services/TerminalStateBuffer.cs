using System.Text;

namespace TerminalGateway.Api.Services;

public sealed class TerminalStateBuffer
{
    private const int TabWidth = 8;
    private readonly List<string> _visibleLines = [];
    private readonly StringBuilder _currentLine = new();
    private readonly int _maxVisibleLines;
    private ParseState _parseState = ParseState.Text;
    private int _cursor;

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
            if (_parseState == ParseState.Csi)
            {
                if (c is >= '@' and <= '~')
                {
                    _parseState = ParseState.Text;
                }
                continue;
            }

            if (_parseState == ParseState.Osc)
            {
                if (c == '\u0007')
                {
                    _parseState = ParseState.Text;
                    continue;
                }

                if (c == '\u001b')
                {
                    _parseState = ParseState.OscEsc;
                }

                continue;
            }

            if (_parseState == ParseState.OscEsc)
            {
                _parseState = c == '\\' ? ParseState.Text : ParseState.Osc;
                continue;
            }

            if (_parseState == ParseState.Esc)
            {
                if (c == '[')
                {
                    _parseState = ParseState.Csi;
                    continue;
                }

                if (c == ']')
                {
                    _parseState = ParseState.Osc;
                    continue;
                }

                _parseState = ParseState.Text;
                continue;
            }

            if (c == '\u001b')
            {
                _parseState = ParseState.Esc;
                continue;
            }

            if (c == '\r')
            {
                _cursor = 0;
                continue;
            }

            if (c == '\b')
            {
                _cursor = Math.Max(0, _cursor - 1);
                continue;
            }

            if (c == '\n')
            {
                var line = _currentLine.ToString();
                _currentLine.Clear();
                _cursor = 0;
                _visibleLines.Add(line);
                committedLines.Add(line);
                if (_visibleLines.Count > _maxVisibleLines)
                {
                    _visibleLines.RemoveAt(0);
                }

                continue;
            }

            if (c == '\t')
            {
                ExpandTab();
                continue;
            }

            if (c < ' ')
            {
                continue;
            }

            WriteAtCursor(c);
        }

        return committedLines;
    }

    private void WriteAtCursor(char value)
    {
        while (_currentLine.Length < _cursor)
        {
            _currentLine.Append(' ');
        }

        if (_cursor < _currentLine.Length)
        {
            _currentLine[_cursor] = value;
        }
        else
        {
            _currentLine.Append(value);
        }

        _cursor++;
    }

    private void ExpandTab()
    {
        var spaces = TabWidth - (_cursor % TabWidth);
        if (spaces <= 0)
        {
            spaces = TabWidth;
        }

        for (var i = 0; i < spaces; i++)
        {
            WriteAtCursor(' ');
        }
    }

    private enum ParseState
    {
        Text,
        Esc,
        Csi,
        Osc,
        OscEsc
    }
}
