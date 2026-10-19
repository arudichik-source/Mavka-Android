using System.Text.Json;

namespace Mavka.Mobile;

// Read-only reference history originating from desktop Mavka.
// It cannot mutate local quantities, prices or confirmed receipt records.
internal sealed record RemoteMovement(
    string DocumentId, string DocumentNumber, string Date, string Kind,
    string Name, string Unit, string? Nsn, decimal Quantity, decimal? UnitPrice);

internal sealed record RemoteMovementJournal(
    string Unit, string AsOf, IReadOnlyList<RemoteMovement> Entries);

internal static class MavkaMovementJournal
{
    public const string Format = "MAVKA_UNIT_MOVEMENTS_V1";

    public static bool TryRead(string? json, out RemoteMovementJournal? journal)
    {
        journal = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 196608)
            return false;
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 14
            });
            var root = parsed.RootElement;
            if (String(root, "format") != Format) return false;
            var unit = String(root, "unit");
            if (string.IsNullOrWhiteSpace(unit) ||
                !root.TryGetProperty("entries", out var rows) ||
                rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 200)
                return false;
            var entries = new List<RemoteMovement>();
            foreach (var row in rows.EnumerateArray())
            {
                string? type = String(row, "type");
                if (type is not ("invoice" or "writeoff" or "transfer" or "adjustment"))
                    return false;
                var documentId = String(row, "documentId");
                var documentNumber = String(row, "documentNumber");
                var date = String(row, "date");
                var name = String(row, "name");
                var itemUnit = String(row, "unit");
                if (string.IsNullOrWhiteSpace(documentId) ||
                    string.IsNullOrWhiteSpace(documentNumber) ||
                    string.IsNullOrWhiteSpace(date) ||
                    string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(itemUnit) ||
                    !row.TryGetProperty("quantity", out var number) ||
                    number.ValueKind != JsonValueKind.Number ||
                    !number.TryGetDecimal(out var quantity) ||
                    quantity <= 0 || quantity > 1_000_000_000m)
                    return false;

                decimal? price = null;
                if (row.TryGetProperty("price", out var value))
                {
                    if (value.ValueKind != JsonValueKind.Number ||
                        !value.TryGetDecimal(out var unitPrice) ||
                        unitPrice < 0 || unitPrice > 1_000_000_000_000m)
                        return false;
                    price = unitPrice;
                }
                entries.Add(new RemoteMovement(documentId, documentNumber, date, type,
                    name, itemUnit, String(row, "nsn"), quantity, price));
            }
            journal = new RemoteMovementJournal(unit, String(root, "asOf") ?? "", entries);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or
                                   ArgumentException or OverflowException)
        {
            return false;
        }
    }

    public static IReadOnlyList<RemoteMovement> ForAsset(
        RemoteMovementJournal journal, string unit, string name,
        string measure, string? nsn)
    {
        if (!string.Equals(journal.Unit.Trim(), unit.Trim(),
            StringComparison.OrdinalIgnoreCase))
            return [];

        return journal.Entries.Where(entry =>
            string.Equals(entry.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Unit.Trim(), measure.Trim(), StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(nsn) ||
                string.IsNullOrWhiteSpace(entry.Nsn) ||
                string.Equals(entry.Nsn.Trim(), nsn.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static string? String(JsonElement root, string field)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(field, out var data) ||
            data.ValueKind != JsonValueKind.String) return null;
        var text = data.GetString()?.Trim();
        return text is { Length: > 0 and <= 200 } ? text : null;
    }
}
