using System.Text;

namespace TerminalGateway.Api.Services;

public sealed class TerminalStateBuffer
{
    private const int TabWidth = 8;
    private const int MaxCsiBufferLength = 64;
    private readonly List<string> _visibleLines = [];
    private readonly StringBuilder _currentLine = new();
    private readonly StringBuilder _csiBuffer = new();
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

    public int CursorColumn => Math.Max(0, _cursor);

    public bool IsOnFreshLine => _cursor == 0 && _currentLine.Length == 0;

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
                    HandleCsi(c, _csiBuffer.ToString());
                    _csiBuffer.Clear();
                    _parseState = ParseState.Text;
                }
                else if (_csiBuffer.Length < MaxCsiBufferLength)
                {
                    _csiBuffer.Append(c);
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
                    _csiBuffer.Clear();
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

    private void HandleCsi(char command, string args)
    {
        switch (command)
        {
            case 'K':
                HandleEraseInLine(ParseCsiParam(args, 0, 0));
                return;
            case 'J':
                HandleEraseInDisplay(ParseCsiParam(args, 0, 0));
                return;
            case 'G':
                _cursor = Math.Max(0, ParseCsiParam(args, 0, 1) - 1);
                return;
            case 'C':
                _cursor += Math.Max(1, ParseCsiParam(args, 0, 1));
                return;
            case 'D':
                _cursor = Math.Max(0, _cursor - Math.Max(1, ParseCsiParam(args, 0, 1)));
                return;
            case 'H':
            case 'f':
                _cursor = Math.Max(0, ParseCsiParam(args, 1, 1) - 1);
                return;
            case 'P':
            {
                var count = Math.Max(1, ParseCsiParam(args, 0, 1));
                if (_cursor < _currentLine.Length)
                {
                    var removeCount = Math.Min(count, _currentLine.Length - _cursor);
                    _currentLine.Remove(_cursor, removeCount);
                }
                return;
            }
            case 'X':
            {
                var count = Math.Max(1, ParseCsiParam(args, 0, 1));
                if (_cursor < _currentLine.Length)
                {
                    var eraseEnd = Math.Min(_currentLine.Length, _cursor + count);
                    for (var i = _cursor; i < eraseEnd; i++)
                    {
                        _currentLine[i] = ' ';
                    }
                }
                return;
            }
            default:
                return;
        }
    }

    private void HandleEraseInLine(int mode)
    {
        if (_currentLine.Length == 0)
        {
            _cursor = Math.Max(0, _cursor);
            return;
        }

        var safeCursor = Math.Clamp(_cursor, 0, _currentLine.Length);
        switch (mode)
        {
            case 1:
            {
                var removeCount = Math.Min(safeCursor + 1, _currentLine.Length);
                if (removeCount > 0)
                {
                    _currentLine.Remove(0, removeCount);
                }
                _cursor = 0;
                return;
            }
            case 2:
                _currentLine.Clear();
                _cursor = 0;
                return;
            default:
                _currentLine.Length = safeCursor;
                _cursor = safeCursor;
                return;
        }
    }

    private void HandleEraseInDisplay(int mode)
    {
        switch (mode)
        {
            case 2:
            case 3:
                _visibleLines.Clear();
                _currentLine.Clear();
                _cursor = 0;
                return;
            case 1:
                HandleEraseInLine(1);
                return;
            default:
                HandleEraseInLine(0);
                return;
        }
    }

    private static int ParseCsiParam(string args, int index, int fallback)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return fallback;
        }

        var trimmed = args.TrimStart('?', '>', '!');
        if (trimmed.Length == 0)
        {
            return fallback;
        }

        var parts = trimmed.Split(';');
        if (index < 0 || index >= parts.Length)
        {
            return fallback;
        }

        return int.TryParse(parts[index], out var value) ? value : fallback;
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
