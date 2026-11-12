using System.Globalization;

namespace Mavka.Mobile;

internal sealed record DatedLedgerEntry(
    string EventId, string DocumentId, string Unit, string Name, string Measure,
    string? Nsn, string AccountingDate, decimal QuantityDelta,
    decimal? UnitPrice, string Status);

internal sealed record HistoricalHolding(
    string Name, string Measure, string? Nsn, decimal Quantity,
    decimal? UnitPrice, decimal? TotalValue);

/// <summary>
/// Decimal, document-linked end-of-day balances.
/// The dated opening snapshot is a start-of-day baseline: same-day posted
/// transactions are counted once after it. Informational snapshots and
/// unconfirmed records must never enter this input sequence.
/// </summary>
internal static class LedgerBalanceCalculator
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy"];

    public static DateOnly ParseDate(string date)
    {
        if (!DateOnly.TryParseExact(date, DateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value))
            throw new ArgumentException("Невідома облікова дата: " + date);
        return value;
    }

    public static IReadOnlyList<HistoricalHolding> Calculate(
        IEnumerable<DatedLedgerEntry> entries, string unit, DateOnly asOf)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<DatedLedgerEntry>();
        foreach (var entry in entries)
        {
            if (entry.Status != "posted" ||
                !string.Equals(entry.Unit.Trim(), unit.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (!unique.Add(entry.EventId))
                throw new InvalidOperationException("Дубльований ідентифікатор руху.");
            if (ParseDate(entry.AccountingDate) <= asOf)
                accepted.Add(entry);
        }
        return accepted
            .GroupBy(e => (e.Name, e.Measure, e.Nsn, e.UnitPrice))
            .Select(g =>
            {
                var qty = g.Sum(e => e.QuantityDelta);
                var price = g.Key.UnitPrice;
                return new HistoricalHolding(
                    g.Key.Name, g.Key.Measure, g.Key.Nsn, qty,
                    price, price.HasValue ? qty * price.Value : null);
            })
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
