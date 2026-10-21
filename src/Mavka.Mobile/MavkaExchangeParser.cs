using System.Text.Json;

namespace Mavka.Mobile;

internal sealed record UnitAsset(
    string Name, string Measure, decimal Quantity, decimal? UnitPrice, string? Nsn, int? ItemId=null, int? Category=null);
internal sealed record UnitAssetsSnapshot(
    string Unit, string AsOf, IReadOnlyList<UnitAsset> Items);
internal sealed record IncomingPosition(
    int LineNo, string Name, string Unit, decimal Quantity,
    string? Nsn, decimal? UnitPrice);
internal sealed record IncomingInvoice(
    string DocumentId, string Number, string Recipient,
    string Date, IReadOnlyList<IncomingPosition> Positions);

internal static class MavkaExchangeParser
{
    public static bool TryAssets(string? json, out UnitAssetsSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 196608) return false;
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
                var name = String(item, "name", 1024);
                var measure = String(item, "unit") ?? "";
                if (string.IsNullOrWhiteSpace(name) ||
                    !item.TryGetProperty("quantity", out var qty) ||
                    qty.ValueKind != JsonValueKind.Number ||
                    !qty.TryGetDecimal(out var n) || n < 0) return false;
                decimal? unitPrice = null;
                if (item.TryGetProperty("price", out var priceElement))
                {
                    if (priceElement.ValueKind != JsonValueKind.Number ||
                        !priceElement.TryGetDecimal(out var value) ||
                        value < 0 || value > 1000000000000m)
                        return false;
                    unitPrice = value;
                }
                int? itemId=null,category=null;
                if(item.TryGetProperty("itemId",out var itemIdentity) && itemIdentity.ValueKind!=JsonValueKind.Null){if(!itemIdentity.TryGetInt32(out var exactId) || exactId<1)return false;itemId=exactId;}
                if(item.TryGetProperty("category",out var categoryValue) && categoryValue.ValueKind!=JsonValueKind.Null){if(!categoryValue.TryGetInt32(out var exactCategory) || exactCategory<0)return false;category=exactCategory;}
                values.Add(new UnitAsset(name, measure, n, unitPrice, String(item, "nsn"),itemId,category));
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
        if (string.IsNullOrWhiteSpace(json) || json.Length > 196608) return false;
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
                var name = String(item, "name", 1024);
                var unit = String(item, "unit");
                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(unit) ||
                    !item.TryGetProperty("quantity", out var quantity) ||
                    quantity.ValueKind != JsonValueKind.Number ||
                    !quantity.TryGetDecimal(out decimal amount) ||
                    amount <= 0 || amount > 1000000000)
                    return false;
                decimal? price = null;
                if (item.TryGetProperty("price", out var priceElement))
                {
                    if (priceElement.ValueKind != JsonValueKind.Number ||
                        !priceElement.TryGetDecimal(out var value) ||
                        value < 0 || value > 1000000000000m)
                        return false;
                    price = value;
                }
                parsed.Add(new IncomingPosition(++line, name, unit, amount,
                    String(item, "nsn"), price));
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

    private static string? String(JsonElement root, string key, int maxLength = 200)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()?.Trim();
        return text is { Length: > 0 } && text.Length <= maxLength ? text : null;
    }
}
