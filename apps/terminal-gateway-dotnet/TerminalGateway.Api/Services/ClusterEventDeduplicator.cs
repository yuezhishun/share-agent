using System.Collections.Concurrent;
using TerminalGateway.Api.Models;

namespace TerminalGateway.Api.Services;

public sealed class ClusterEventDeduplicator
{
    private readonly object _gate = new();
    private readonly HashSet<string> _seenEventIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _lastSeq = new(StringComparer.Ordinal);

    public bool TryAccept(ClusterTerminalEventEnvelope envelope, out bool hasGap)
    {
        hasGap = false;
        var eventId = BuildEventId(envelope);
        var key = $"{envelope.NodeId}|{envelope.InstanceId}";

        lock (_gate)
        {
            if (_seenEventIds.Contains(eventId))
            {
                return false;
            }

            _seenEventIds.Add(eventId);
            if (_seenEventIds.Count > 10000)
            {
                _seenEventIds.Clear();
                _seenEventIds.Add(eventId);
            }

            var seq = Math.Max(0, envelope.Seq);
            var last = _lastSeq.TryGetValue(key, out var current) ? current : 0;
            if (seq > 0 && seq <= last)
            {
                return false;
            }

            hasGap = last > 0 && seq > last + 1;
            if (seq > 0)
            {
                _lastSeq[key] = seq;
            }

            return true;
        }
    }

    private static string BuildEventId(ClusterTerminalEventEnvelope envelope)
    {
        var raw = (envelope.EventId ?? string.Empty).Trim();
        return raw.Length > 0
            ? raw
            : $"{envelope.NodeId}:{envelope.InstanceId}:{envelope.Seq}:{envelope.Type}";
    }
}
