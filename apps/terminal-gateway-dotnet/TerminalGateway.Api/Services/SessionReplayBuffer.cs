using System.Text;

namespace TerminalGateway.Api.Services;

public sealed class SessionReplayBuffer
{
    private readonly List<OutputChunk> _chunks = [];
    private int _bytes;

    public int HeadSeq { get; private set; } = 1;
    public int TailSeq { get; private set; }
    public int NextSeq { get; private set; } = 1;

    public AppendResult Append(string data, int maxBytes)
    {
        if (string.IsNullOrEmpty(data))
        {
            return new AppendResult(NextSeq, NextSeq - 1, false);
        }

        var seqStart = NextSeq;
        var seqEnd = seqStart + data.Length - 1;
        var truncated = false;
        var bytes = Encoding.UTF8.GetByteCount(data);
        var normalized = data;

        if (bytes > maxBytes)
        {
            normalized = TrimTailByBytes(normalized, maxBytes);
            var droppedChars = data.Length - normalized.Length;
            seqStart += droppedChars;
            seqEnd = seqStart + normalized.Length - 1;
            bytes = Encoding.UTF8.GetByteCount(normalized);
            truncated = true;
        }

        _chunks.Add(new OutputChunk(normalized, bytes, seqStart, seqEnd));
        _bytes += bytes;
        NextSeq = seqEnd + 1;
        RefreshSeq();

        while (_bytes > maxBytes && _chunks.Count > 0)
        {
            var dropped = _chunks[0];
            _chunks.RemoveAt(0);
            _bytes -= dropped.Bytes;
            truncated = true;
        }

        if (_bytes < 0)
        {
            _bytes = 0;
        }

        RefreshSeq();
        return new AppendResult(seqStart, seqEnd, truncated);
    }

    public SnapshotResult Snapshot(int limitBytes, int maxOutputBufferBytes, bool outputTruncated, string sessionId, string status, int? exitCode)
    {
        var data = CollectTailDataWithinBytes(_chunks, limitBytes);
        var bytes = Encoding.UTF8.GetByteCount(data);
        var truncated = outputTruncated || _bytes > bytes;
        return new SnapshotResult(sessionId, status, exitCode, data, bytes, _bytes, truncated, maxOutputBufferBytes, HeadSeq, TailSeq);
    }

    public HistoryResult History(int? beforeSeq, int limitBytes, bool outputTruncated, string sessionId)
    {
        var selected = CollectHistoryBeforeSeq(_chunks, beforeSeq, limitBytes);
        int? nextBeforeSeq = selected.HasMore && selected.Chunks.Count > 0 ? selected.Chunks[0].SeqStart : null;
        return new HistoryResult(sessionId, selected.Chunks, selected.HasMore, nextBeforeSeq, outputTruncated);
    }

    public DeltaResult CollectDelta(int sinceSeq)
    {
        if (sinceSeq < HeadSeq - 1)
        {
            return new DeltaResult([], true);
        }

        if (sinceSeq >= TailSeq)
        {
            return new DeltaResult([], false);
        }

        var output = new List<DeltaChunk>();
        foreach (var chunk in _chunks)
        {
            if (chunk.SeqEnd <= sinceSeq)
            {
                continue;
            }

            if (chunk.SeqStart > sinceSeq)
            {
                output.Add(new DeltaChunk(chunk.Data, chunk.SeqStart, chunk.SeqEnd));
                continue;
            }

            var cut = sinceSeq - chunk.SeqStart + 1;
            var partial = chunk.Data[cut..];
            if (partial.Length == 0)
            {
                continue;
            }

            output.Add(new DeltaChunk(partial, sinceSeq + 1, chunk.SeqEnd));
        }

        return new DeltaResult(output, false);
    }

    public string JoinAllData() => string.Concat(_chunks.Select(x => x.Data));
    public int OutputBytes => _bytes;

    private void RefreshSeq()
    {
        HeadSeq = _chunks.Count > 0 ? _chunks[0].SeqStart : NextSeq;
        TailSeq = _chunks.Count > 0 ? _chunks[^1].SeqEnd : NextSeq - 1;
    }

    private static string CollectTailDataWithinBytes(IReadOnlyList<OutputChunk> chunks, int limitBytes)
    {
        if (chunks.Count == 0)
        {
            return string.Empty;
        }

        var selected = new List<string>();
        var used = 0;
        for (var i = chunks.Count - 1; i >= 0; i--)
        {
            var chunk = chunks[i];
            if (used + chunk.Bytes <= limitBytes)
            {
                selected.Add(chunk.Data);
                used += chunk.Bytes;
                continue;
            }

            var remaining = limitBytes - used;
            if (remaining > 0)
            {
                selected.Add(TrimTailByBytes(chunk.Data, remaining));
            }

            break;
        }

        selected.Reverse();
        return string.Concat(selected);
    }

    private static HistorySelectResult CollectHistoryBeforeSeq(IReadOnlyList<OutputChunk> chunks, int? beforeSeq, int limitBytes)
    {
        if (chunks.Count == 0)
        {
            return new HistorySelectResult([], false);
        }

        var boundary = beforeSeq ?? int.MaxValue;
        var selected = new List<HistoryChunk>();
        var used = 0;
        var hasMore = false;
        for (var i = chunks.Count - 1; i >= 0; i--)
        {
            var chunk = chunks[i];
            if (chunk.SeqStart >= boundary)
            {
                continue;
            }

            var data = chunk.Data;
            var seqStart = chunk.SeqStart;
            var seqEnd = chunk.SeqEnd;

            if (seqEnd >= boundary)
            {
                var keepLen = Math.Max(0, boundary - seqStart);
                data = data[..keepLen];
                seqEnd = boundary - 1;
            }

            if (data.Length == 0 || seqEnd < seqStart)
            {
                continue;
            }

            var bytes = Encoding.UTF8.GetByteCount(data);
            if (used + bytes <= limitBytes)
            {
                selected.Add(new HistoryChunk(data, seqStart, seqEnd));
                used += bytes;
                continue;
            }

            var remaining = limitBytes - used;
            if (remaining > 0)
            {
                var trimmed = TrimTailByBytes(data, remaining);
                var shift = data.Length - trimmed.Length;
                selected.Add(new HistoryChunk(trimmed, seqStart + shift, seqEnd));
            }

            hasMore = i > 0;
            break;
        }

        selected.Reverse();
        return new HistorySelectResult(selected, hasMore);
    }

    private static string TrimTailByBytes(string text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text) || maxBytes <= 0)
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            return text;
        }

        var start = 0;
        var output = text;
        while (output.Length > 0 && Encoding.UTF8.GetByteCount(output) > maxBytes && start < text.Length)
        {
            start++;
            output = text[start..];
        }

        return output;
    }

    public readonly record struct OutputChunk(string Data, int Bytes, int SeqStart, int SeqEnd);
    public readonly record struct AppendResult(int SeqStart, int SeqEnd, bool Truncated);
    public readonly record struct SnapshotResult(string SessionId, string Status, int? ExitCode, string Data, int Bytes, int TotalBytes, bool Truncated, int MaxOutputBufferBytes, int HeadSeq, int TailSeq);
    public readonly record struct HistoryChunk(string Data, int SeqStart, int SeqEnd);
    public readonly record struct HistoryResult(string SessionId, IReadOnlyList<HistoryChunk> Chunks, bool HasMore, int? NextBeforeSeq, bool Truncated);
    public readonly record struct DeltaChunk(string Data, int SeqStart, int SeqEnd);
    public readonly record struct DeltaResult(IReadOnlyList<DeltaChunk> Chunks, bool TruncatedSince);
    private readonly record struct HistorySelectResult(IReadOnlyList<HistoryChunk> Chunks, bool HasMore);
}
