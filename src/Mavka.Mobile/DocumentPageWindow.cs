namespace Mavka.Mobile;

/// <summary>
/// UI-only pagination. Does not mutate, delete or re-order archive records.
/// Every archive page contains at most 50 document summaries.
/// </summary>
internal readonly record struct DocumentPageWindow(
    int PageIndex, int PageCount, int StartIndex, int ItemCount)
{
    public const int PageSize = 50;

    public static DocumentPageWindow For(int totalItems, int requestedIndex)
    {
        if (totalItems < 0)
            throw new ArgumentOutOfRangeException(nameof(totalItems));
        int pageCount = totalItems == 0 ? 1 : 1 + (totalItems - 1) / PageSize;
        int pageIndex = Math.Clamp(requestedIndex, 0, pageCount - 1);
        int start = pageIndex * PageSize;
        return new DocumentPageWindow(pageIndex, pageCount, start,
            Math.Min(PageSize, totalItems - start));
    }
}
