namespace Mavka.Mobile;

/// <summary>
/// Read-only comparison of a reference snapshot and a dated local ledger.
/// It never writes any movements or replaces the local baseline.
/// </summary>
internal static class LedgerReconciliation
{
    internal sealed record Difference(
        string Name, string Unit, string? Nsn, decimal? UnitPrice,
        decimal LocalQuantity, decimal ReferenceQuantity, decimal Delta,
        decimal? LocalValue, decimal? ReferenceValue);

    // Identity is exact accounting identity; unlike display-name suggestions,
    // a similar title must not merge distinct positions.
    public static IReadOnlyList<Difference> Compare(
        IEnumerable<HistoricalHolding> local,
        IEnumerable<UnitAsset> remote)
    {
        static string Key(string name, string unit, string? nsn, decimal? price)
            => string.Join("\u001F", name.Trim(), unit.Trim(), (nsn ?? "").Trim(),
                price?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<unknown>");

        var combined = new Dictionary<string,
            (string Name,string Unit,string? Nsn,decimal? Price,decimal Local,decimal Remote)>(
            StringComparer.Ordinal);

        foreach (var item in local)
        {
            var k = Key(item.Name, item.Measure, item.Nsn, item.UnitPrice);
            if (!combined.TryGetValue(k, out var v))
                v = (item.Name, item.Measure, item.Nsn, item.UnitPrice, 0, 0);
            combined[k] = (v.Name, v.Unit, v.Nsn, v.Price, v.Local + item.Quantity, v.Remote);
        }
        foreach (var item in remote)
        {
            var k = Key(item.Name, item.Measure, item.Nsn, item.UnitPrice);
            if (!combined.TryGetValue(k, out var v))
                v = (item.Name, item.Measure, item.Nsn, item.UnitPrice, 0, 0);
            combined[k] = (v.Name, v.Unit, v.Nsn, v.Price, v.Local, v.Remote + item.Quantity);
        }
        return combined.Values
            .Where(item => item.Local != item.Remote)
            .Select(item => new Difference(item.Name, item.Unit, item.Nsn, item.Price,
                item.Local, item.Remote, item.Local - item.Remote,
                item.Price.HasValue ? item.Local * item.Price.Value : null,
                item.Price.HasValue ? item.Remote * item.Price.Value : null))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Nsn, StringComparer.Ordinal)
            .ToList();
    }
}
