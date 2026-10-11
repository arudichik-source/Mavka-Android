using System.Text.Json;

namespace Mavka.Mobile;

internal sealed record UnitAsset(string Name, string Measure, decimal Quantity);
internal sealed record UnitAssetsSnapshot(
    string Unit, string AsOf, IReadOnlyList<UnitAsset> Items);
internal sealed record IncomingPosition(int LineNo, string Name, string Unit, decimal Quantity, string? Nsn);
internal sealed record IncomingInvoice(
    string DocumentId, string Number, string Recipient,
    string Date, IReadOnlyList<IncomingPosition> Positions);

internal static class MavkaExchangeParser
{
    public static bool TryAssets(string? json, out UnitAssetsSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65000) return false;
        try
        {
            using var doc = Parse(json);
            var root = doc.RootElement;
            if (String(root, "format") != "MAVKA_UNIT_ASSETS_V1") return false;
            var unit = String(root, "unit");
            if (string.IsNullOrWhiteSpace(unit)) return false;
            if (!root.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 500)
                return false;

            var values = new List<UnitAsset>();
            foreach (var item in items.EnumerateArray())
            {
                var name = String(item, "name");
                var measure = String(item, "unit") ?? "";
                if (string.IsNullOrWhiteSpace(name) ||
                    !item.TryGetProperty("quantity", out var qty) ||
                    qty.ValueKind != JsonValueKind.Number ||
                    !qty.TryGetDecimal(out var n) || n < 0) return false;
                values.Add(new UnitAsset(name, measure, n));
            }

            snapshot = new UnitAssetsSnapshot(unit, String(root, "asOf") ?? "", values);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
                                   or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    public static bool TryInvoice(string? json, out IncomingInvoice? invoice)
    {
        invoice = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65000) return false;
        try
        {
            using var document = Parse(json);
            var root = document.RootElement;
            if (String(root, "format") != "MAVKA_DOCUMENT_V1" ||
                String(root, "type") != "invoice") return false;

            var number = String(root, "documentNumber");
            var recipient = String(root, "recipient");
            var id = String(root, "documentId");
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(number) ||
                string.IsNullOrWhiteSpace(recipient)) return false;

            if (!root.TryGetProperty("positions", out var positions))
                root.TryGetProperty("items", out positions);
            if (positions.ValueKind != JsonValueKind.Array ||
                positions.GetArrayLength() is < 1 or > 200)
                return false;

            var parsed = new List<IncomingPosition>();
            var line = 0;
            foreach (var item in positions.EnumerateArray())
            {
                var name = String(item, "name");
                var unit = String(item, "unit");
                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(unit) ||
                    !item.TryGetProperty("quantity", out var quantity) ||
                    quantity.ValueKind != JsonValueKind.Number ||
                    !quantity.TryGetDecimal(out decimal amount) ||
                    amount <= 0 || amount > 1000000000)
                    return false;
                parsed.Add(new IncomingPosition(++line, name, unit, amount,
                    String(item, "nsn")));
            }
            invoice = new IncomingInvoice(id, number, recipient,
                String(root, "documentDate") ?? String(root, "date") ?? "",
                parsed);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
                                   or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(
        json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16
        });

    private static string? String(JsonElement root, string key)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()?.Trim();
        return text is { Length: > 0 and <= 200 } ? text : null;
    }
}
