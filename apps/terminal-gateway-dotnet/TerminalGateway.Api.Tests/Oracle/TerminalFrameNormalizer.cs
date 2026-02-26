using System.Text.Json;
using System.Text.RegularExpressions;
using TerminalGateway.Api.Services;

namespace TerminalGateway.Api.Tests.Oracle;

public sealed record NormalizedFrame(int Cols, int Rows, int CursorX, int CursorY, IReadOnlyList<string> VisibleLines)
{
    public static readonly NormalizedFrame Empty = new(0, 0, 0, 0, []);
}

public static partial class TerminalFrameNormalizer
{
    public static NormalizedFrame FromOracle(OracleFrame frame)
    {
        return new NormalizedFrame(
            frame.Cols,
            frame.Rows,
            Math.Max(0, frame.CursorX),
            Math.Max(0, frame.CursorY),
            NormalizeLines(frame.VisibleLines));
    }

    public static NormalizedFrame FromBuffer(TerminalStateBuffer buffer, int cols, int rows)
    {
        var lines = buffer.VisibleLines;
        var window = lines.Skip(Math.Max(0, lines.Count - Math.Max(1, rows))).ToList();
        var cursorY = Math.Max(0, window.Count - 1);
        var cursorX = window.Count == 0 ? 0 : window[^1].Length;

        return new NormalizedFrame(
            Math.Max(1, cols),
            Math.Max(1, rows),
            cursorX,
            cursorY,
            NormalizeLines(window));
    }

    public static NormalizedFrame FromSnapshot(JsonElement snapshot)
    {
        if (snapshot.ValueKind != JsonValueKind.Object)
        {
            return NormalizedFrame.Empty;
        }

        var cols = snapshot.TryGetProperty("size", out var size) && size.TryGetProperty("cols", out var colsValue)
            ? colsValue.GetInt32()
            : 0;
        var rows = snapshot.TryGetProperty("size", out size) && size.TryGetProperty("rows", out var rowsValue)
            ? rowsValue.GetInt32()
            : 0;

        var cursorX = snapshot.TryGetProperty("cursor", out var cursor) && cursor.TryGetProperty("x", out var xValue)
            ? xValue.GetInt32()
            : 0;
        var cursorY = snapshot.TryGetProperty("cursor", out cursor) && cursor.TryGetProperty("y", out var yValue)
            ? yValue.GetInt32()
            : 0;

        var lines = new List<string>();
        if (snapshot.TryGetProperty("rows", out var rowItems) && rowItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowItems.EnumerateArray().OrderBy(x => x.TryGetProperty("y", out var y) ? y.GetInt32() : int.MaxValue))
            {
                if (!row.TryGetProperty("segs", out var segs) || segs.ValueKind != JsonValueKind.Array)
                {
                    lines.Add(string.Empty);
                    continue;
                }

                var text = string.Concat(
                    segs.EnumerateArray()
                        .Where(seg => seg.ValueKind == JsonValueKind.Array)
                        .Select(seg =>
                        {
                            if (seg.GetArrayLength() == 0)
                            {
                                return string.Empty;
                            }

                            var first = seg[0];
                            return first.ValueKind == JsonValueKind.String ? first.GetString() ?? string.Empty : string.Empty;
                        }));
                lines.Add(text);
            }
        }

        return new NormalizedFrame(
            Math.Max(1, cols),
            Math.Max(1, rows),
            Math.Max(0, cursorX),
            Math.Max(0, cursorY),
            NormalizeLines(lines));
    }

    private static IReadOnlyList<string> NormalizeLines(IEnumerable<string> lines)
    {
        var normalized = lines
            .Select(x => NormalizeLine(x ?? string.Empty))
            .ToList();

        var start = 0;
        var end = normalized.Count - 1;
        while (start <= end && string.IsNullOrEmpty(normalized[start]))
        {
            start++;
        }

        while (end >= start && string.IsNullOrEmpty(normalized[end]))
        {
            end--;
        }

        if (start > end)
        {
            return [];
        }

        return normalized.Skip(start).Take(end - start + 1).ToList();
    }

    private static string NormalizeLine(string line)
    {
        var normalized = line.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = AnsiRegex().Replace(normalized, string.Empty);
        normalized = string.Concat(normalized.Where(ch => ch == '\n' || !char.IsControl(ch)));
        return normalized.TrimEnd();
    }

    [GeneratedRegex("\\x1B(?:\\[[0-?]*[ -/]*[@-~]|\\][^\\u0007\\x1B]*(?:\\u0007|\\x1B\\\\)|[@-Z\\\\-_])")]
    private static partial Regex AnsiRegex();
}
