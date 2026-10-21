namespace Mavka.Mobile;

/// <summary>
/// Bounded enumeration of opaque, locally encrypted received-file records.
/// The vault holds at most MaxFiles items. Filter malformed names before
/// limiting the list, or valid files may disappear from the UI.
/// </summary>
internal static class PrivateVaultListingPolicy
{
    public const int MaxFiles = 100;

    public static bool IsId(string? id) => id is { Length: 64 } &&
        id.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static IReadOnlyList<string> CandidateIds(IEnumerable<string> paths) =>
        paths.Select(Path.GetFileName)
            .Where(static fileName => fileName is { Length: 68 } &&
                fileName.EndsWith(".bin", StringComparison.Ordinal))
            .Select(static fileName => fileName![..64])
            .Where(IsId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .Take(MaxFiles)
            .ToArray();
}
