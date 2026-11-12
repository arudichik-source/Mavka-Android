namespace Mavka.Mobile;

/// <summary>
/// A posted local accounting document is legal/financial evidence: hide it
/// from the active list only with a dedicated accounting reversal, never via
/// the archive trash operation. Unposted documents are recoverable for 30 days.
/// </summary>
internal static class ArchiveTrashPolicy
{
    public const int RetentionDays = 30;

    public static bool CanMoveToTrash(string? postingStatus) =>
        !string.Equals(postingStatus, "posted", StringComparison.Ordinal);

    public static bool CanRestore(string? deletedUtc, DateTimeOffset now)
    {
        if (!TryDeletionTime(deletedUtc, out var deleted)) return false;
        return deleted <= now && deleted > now.AddDays(-RetentionDays);
    }

    public static bool CanPurge(string? deletedUtc, DateTimeOffset now) =>
        TryDeletionTime(deletedUtc, out var deleted) && deleted <= now.AddDays(-RetentionDays);

    private static bool TryDeletionTime(string? value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out parsed);
}
