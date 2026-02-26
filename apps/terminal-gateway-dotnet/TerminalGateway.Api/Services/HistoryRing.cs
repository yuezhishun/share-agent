namespace TerminalGateway.Api.Services;

public sealed class HistoryRing
{
    private readonly int _capacity;
    private readonly List<HistoryLine> _items = [];
    private int _cursor = 1;

    public HistoryRing(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public int Count => _items.Count;

    public string NewestCursor() => $"h-{_cursor}";

    public void Push(string text)
    {
        _items.Add(new HistoryLine($"h-{_cursor++}", text));
        if (_items.Count > _capacity)
        {
            _items.RemoveAt(0);
        }
    }

    public (IReadOnlyList<HistoryLine> Lines, string NextBefore, bool Exhausted) Fetch(string before, int limit)
    {
        var safeLimit = Math.Max(1, Math.Min(200, limit));
        var beforeNum = ParseCursor(before);
        var oldest = _items.Count > 0 ? ParseCursor(_items[0].Cursor) : int.MaxValue;
        var effectiveBefore = beforeNum <= oldest ? _cursor : beforeNum;

        var candidates = _items.Where(x => ParseCursor(x.Cursor) < effectiveBefore).ToList();
        var lines = candidates.Skip(Math.Max(0, candidates.Count - safeLimit)).ToList();

        if (lines.Count == 0)
        {
            return ([], $"h-{effectiveBefore}", candidates.Count == 0);
        }

        var nextBefore = lines[0].Cursor;
        var exhausted = candidates.Count <= lines.Count;
        return (lines, nextBefore, exhausted);
    }

    private static int ParseCursor(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || !cursor.StartsWith("h-", StringComparison.Ordinal))
        {
            return int.MaxValue;
        }

        return int.TryParse(cursor[2..], out var number) ? number : int.MaxValue;
    }

    public sealed record HistoryLine(string Cursor, string Text);
}
