using System.Text.Json;

namespace Mavka.Mobile;

internal sealed record UnitAsset(string Name, string Measure, decimal Quantity);
internal sealed record UnitAssetsSnapshot(
    string Unit, string AsOf, IReadOnlyList<UnitAsset> Items);
internal sealed record IncomingInvoice(
    string DocumentId, string Number, string Recipient);

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
            using var doc = Parse(json);
            var root = doc.RootElement;
            if (String(root, "format") != "MAVKA_DOCUMENT_V1" ||
                String(root, "type") != "invoice") return false;
            var number = String(root, "documentNumber");
            var recipient = String(root, "recipient");
            if (string.IsNullOrWhiteSpace(number) ||
                string.IsNullOrWhiteSpace(recipient)) return false;
            var id = String(root, "documentId") ?? number;
            invoice = new IncomingInvoice(id, number, recipient);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
                                   or ArgumentException)
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
