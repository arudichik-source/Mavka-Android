namespace Mavka.Mobile;

/// <summary>
/// Durable IMAP processing state: only "ready" and "trashed" skip payload
/// retrieval. "ready" is still eligible for a bounded, independently
/// authenticated Gmail Trash retry. All other states must be revisited.
/// UIDs are only interpreted inside the current inbox UIDVALIDITY/account scope.
/// </summary>
internal static class GmailProcessingSelectionPolicy
{
    public static IReadOnlyList<uint> PayloadBatch(IEnumerable<uint> candidates,
        IReadOnlySet<uint> keyUids,IReadOnlySet<uint> changeUids,IReadOnlySet<uint> fileUids)
        =>candidates.Distinct().OrderBy(uid=>keyUids.Contains(uid)?0:changeUids.Contains(uid)||fileUids.Contains(uid)?1:2)
            .ThenBy(uid=>uid).Take(3).ToArray();
    public static bool IsReady(IReadOnlyDictionary<uint, string> processed, uint uid) =>
        processed.TryGetValue(uid, out var value) &&
        string.Equals(value, "ready", StringComparison.Ordinal);

    public static bool IsTrashed(IReadOnlyDictionary<uint, string> processed, uint uid) =>
        processed.TryGetValue(uid, out var value) &&
        string.Equals(value, "trashed", StringComparison.Ordinal);

    public static IReadOnlyList<uint> PendingPayloads(
        IEnumerable<uint> newestFirst, IReadOnlyDictionary<uint, string> processed,
        int maximum)
    {
        return newestFirst.Where(uid =>
            !IsReady(processed, uid) && !IsTrashed(processed, uid))
            .Take(Math.Clamp(maximum, 1, 500)).ToList();
    }

    public static IReadOnlyList<uint> ReadyForTrash(
        IEnumerable<uint> newestFirst, IReadOnlyDictionary<uint, string> processed,
        int maximum)
    {
        return newestFirst.Where(uid => IsReady(processed, uid))
            .Take(Math.Clamp(maximum, 1, 50)).ToList();
    }
}
