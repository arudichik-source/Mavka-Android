namespace Mavka.Mobile;

/// <summary>
/// The amount shown next to an inventory search must describe only visible
/// positions. A separate all-items total can still be shown for context.
/// </summary>
internal readonly record struct InventoryValueSummary(decimal KnownTotal, int UnknownPriceCount);

internal static class InventorySearchSummary
{
    public static IReadOnlyList<T> Filter<T>(
        IEnumerable<T> holdings, string? query,
        Func<T, string> name, Func<T, string?> nsn)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(nsn);
        var term = (query ?? "").Trim();
        return holdings.Where(item =>
            term.Length == 0 ||
            name(item).Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (nsn(item)?.Contains(term, StringComparison.OrdinalIgnoreCase) == true))
            .ToList();
    }

    public static InventoryValueSummary Summarize<T>(
        IEnumerable<T> visibleHoldings, Func<T, decimal?> totalValue)
    {
        ArgumentNullException.ThrowIfNull(visibleHoldings);
        ArgumentNullException.ThrowIfNull(totalValue);
        decimal amount = 0;
        int missing = 0;
        foreach (var holding in visibleHoldings)
        {
            decimal? value = totalValue(holding);
            if (value.HasValue) amount = checked(amount + value.Value);
            else missing++;
        }
        return new InventoryValueSummary(amount, missing);
    }
}
