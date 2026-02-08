using System.Text.RegularExpressions;
using PtyAgent.Api.Contracts;
using PtyAgent.Api.Domain;
using PtyAgent.Api.Infrastructure;

namespace PtyAgent.Api.Services;

public sealed class TaskLinkingService
{
    private readonly SqliteStore _store;

    public TaskLinkingService(SqliteStore store)
    {
        _store = store;
    }

    public async Task<TaskLink> BuildLinkAsync(CreateTaskRequest request, Guid newTaskId)
    {
        var sourceInputId = request.SourceInputId ?? Guid.NewGuid();

        if (request.FollowUpTaskId is Guid explicitTaskId)
        {
            var existing = await _store.GetTaskAsync(explicitTaskId);
            if (existing is not null)
            {
                return new TaskLink(sourceInputId, existing.TaskId, LinkType.FollowUp, 1.0, "explicit_follow_up", DateTimeOffset.UtcNow);
            }
        }

        var candidates = await _store.ListRecentTasksAsync(20);
        var best = FindBestCandidate(request, candidates, out var score);
        if (best is not null && score >= 0.2)
        {
            return new TaskLink(sourceInputId, best.TaskId, LinkType.FollowUp, score, "semantic_fallback", DateTimeOffset.UtcNow);
        }

        return new TaskLink(sourceInputId, newTaskId, LinkType.New, 1.0, "new_task", DateTimeOffset.UtcNow);
    }

    private static TaskItem? FindBestCandidate(CreateTaskRequest request, IReadOnlyList<TaskItem> candidates, out double score)
    {
        score = 0;
        TaskItem? best = null;
        var incoming = Tokenize($"{request.Title} {request.Intent}");

        foreach (var candidate in candidates)
        {
            var candidateTokens = Tokenize($"{candidate.Title} {candidate.Intent}");
            if (candidateTokens.Count == 0)
            {
                continue;
            }

            var overlap = incoming.Intersect(candidateTokens).Count();
            var union = incoming.Union(candidateTokens).Count();
            var jaccard = union == 0 ? 0 : (double)overlap / union;

            var ageHours = (DateTimeOffset.UtcNow - candidate.UpdatedAt).TotalHours;
            var recencyBonus = ageHours <= 6 ? 0.15 : ageHours <= 24 ? 0.08 : 0;
            var finalScore = Math.Min(1.0, jaccard + recencyBonus);

            if (finalScore > score)
            {
                score = finalScore;
                best = candidate;
            }
        }

        return best;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var words = Regex.Split(text.ToLowerInvariant(), "[^a-z0-9\u4e00-\u9fa5]+", RegexOptions.Compiled)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length >= 2);
        return words.ToHashSet(StringComparer.Ordinal);
    }
}
