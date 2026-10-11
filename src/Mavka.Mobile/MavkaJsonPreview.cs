using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// Read-only display of JSON attachments. Never imports, approves, or changes
/// accounting records. Validation is delegated to the same parsers as the UI.
/// </summary>
internal static class MavkaJsonPreview
{
    private const int MaxCharacters = 65000;
    private const int MaxPreviewCharacters = 30000;
    private static readonly CultureInfo UiCulture = CultureInfo.GetCultureInfo("uk-UA");

    public static string Format(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "JSON-файл порожній.";
        if (json.Length > MaxCharacters)
            return "JSON-файл перевищує дозволений розмір.";

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return "Коректний JSON (масив або значення):\n\n" + Pretty(root);

            var format = Value(root, "format");
            return format switch
            {
                "MAVKA_UNIT_ASSETS_V1" => FormatAssets(json),
                "MAVKA_UNIT_MOVEMENTS_V1" => FormatMovements(json),
                "MAVKA_DOCUMENT_V1" => FormatDocument(root),
                _ => "Коректний JSON. Формат обміну Мавки не визначений.\n" +
                     "Показано вміст для перегляду, без імпорту:\n\n" + Pretty(root)
            };
        }
        catch (JsonException)
        {
            return "Некоректний JSON. Перевір синтаксис надісланого файлу.";
        }
        catch (OverflowException)
        {
            return "Некоректний JSON. Числові значення перевищують допустимий діапазон.";
        }
    }

    private static string FormatAssets(string json)
    {
        if (!MavkaExchangeParser.TryAssets(json, out var snapshot) || snapshot is null)
            return "Розпізнано MAVKA_UNIT_ASSETS_V1, але дані початкових залишків " +
                   "не відповідають структурі обміну. Нічого не зараховано.";

        var output = new StringBuilder();
        output.AppendLine("Майно підрозділу — знімок (перегляд)");
        output.AppendLine("Формат: MAVKA_UNIT_ASSETS_V1");
        output.AppendLine("Підрозділ: " + snapshot.Unit);
        output.AppendLine("Станом на: " +
            (string.IsNullOrWhiteSpace(snapshot.AsOf) ? "не вказано" : snapshot.AsOf));
        output.AppendLine("Позицій: " + snapshot.Items.Count);
        output.AppendLine();

        int line = 0;
        decimal knownTotal = 0m;
        int unknownPrices = 0;
        foreach (var item in snapshot.Items)
        {
            output.AppendLine(++line + ". " + item.Name);
            output.AppendLine("   Кількість: " + Number(item.Quantity) + " " + item.Measure);
            if (!string.IsNullOrWhiteSpace(item.Nsn))
                output.AppendLine("   NSN: " + item.Nsn);

            if (item.UnitPrice.HasValue)
            {
                output.AppendLine("   Ціна: " + Money(item.UnitPrice.Value));
                decimal amount = item.UnitPrice.Value * item.Quantity;
                output.AppendLine("   Вартість: " + Money(amount));
                knownTotal += amount;
            }
            else
            {
                unknownPrices++;
                output.AppendLine("   Ціна: не вказана");
            }
            output.AppendLine();
        }
        output.AppendLine("Вартість позицій із відомою ціною: " + Money(knownTotal));
        if (unknownPrices > 0)
            output.AppendLine("Без ціни: " + unknownPrices +
                " позицій, їхня вартість до підсумку не включена.");
        output.AppendLine();
        output.Append("Лише перегляд. Початкові залишки не зараховано автоматично.");
        return LimitPreview(output);
    }

    private static string FormatMovements(string json)
    {
        if (!MavkaMovementJournal.TryRead(json, out var journal) || journal is null)
            return "Розпізнано MAVKA_UNIT_MOVEMENTS_V1, але історія документів " +
                   "має некоректні поля. Жоден рух не проведено.";

        var output = new StringBuilder();
        output.AppendLine("Історія рухів майна (довідково)");
        output.AppendLine("Формат: MAVKA_UNIT_MOVEMENTS_V1");
        output.AppendLine("Підрозділ: " + journal.Unit);
        output.AppendLine("Станом на: " +
            (string.IsNullOrWhiteSpace(journal.AsOf) ? "не вказано" : journal.AsOf));
        output.AppendLine("Записів: " + journal.Entries.Count);
        output.AppendLine();
        int line = 0;
        foreach (var movement in journal.Entries)
        {
            var type = movement.Kind switch
            {
                "invoice" => "Накладна",
                "writeoff" => "Акт списання",
                "transfer" => "Переміщення",
                "adjustment" => "Коригування",
                _ => "Документ"
            };
            output.AppendLine(++line + ". " + type + " № " + movement.DocumentNumber);
            output.AppendLine("   Дата: " + movement.Date);
            output.AppendLine("   Майно: " + movement.Name);
            output.AppendLine("   Кількість: " + Number(movement.Quantity) + " " + movement.Unit);
            if (!string.IsNullOrWhiteSpace(movement.Nsn))
                output.AppendLine("   NSN: " + movement.Nsn);
            if (movement.UnitPrice.HasValue)
            {
                output.AppendLine("   Ціна: " + Money(movement.UnitPrice.Value));
                output.AppendLine("   Вартість: " +
                    Money(movement.UnitPrice.Value * movement.Quantity));
            }
            else output.AppendLine("   Ціна: не вказана");
            output.AppendLine();
        }
        output.AppendLine("Лише довідковий перегляд. Ці записи не змінюють залишки.");
        return LimitPreview(output);
    }

    private static string FormatDocument(JsonElement root)
    {
        var output = new StringBuilder();
        output.AppendLine("Документ МАВКИ (перегляд)");
        output.AppendLine("Формат: MAVKA_DOCUMENT_V1");
        output.AppendLine("Тип: " + (Value(root, "type") ?? "не вказано"));
        output.AppendLine("Номер: " + (Value(root, "documentNumber") ?? "не вказано"));
        output.AppendLine("ID: " + (Value(root, "documentId") ?? "не вказано"));
        output.AppendLine("Дата: " +
            (Value(root, "documentDate") ?? Value(root, "date") ?? "не вказано"));
        output.AppendLine("Отримувач: " + (Value(root, "recipient") ?? "не вказано"));

        if (!root.TryGetProperty("positions", out var items))
            root.TryGetProperty("items", out items);
        if (items.ValueKind == JsonValueKind.Array)
        {
            output.AppendLine();
            output.AppendLine("Позиції (" + items.GetArrayLength() + "):");
            int line = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (line++ >= 200)
                {
                    output.AppendLine("… Інші позиції приховано.");
                    break;
                }
                output.AppendLine(line + ". " + (Value(item, "name") ?? "Без назви"));
                output.AppendLine("   Кількість: " + (Value(item, "quantity") ?? "?") +
                                  " " + (Value(item, "unit") ?? ""));
                var nsn = Value(item, "nsn");
                if (!string.IsNullOrWhiteSpace(nsn))
                    output.AppendLine("   NSN: " + nsn);
                if (item.TryGetProperty("price", out var price) &&
                    price.ValueKind == JsonValueKind.Number &&
                    price.TryGetDecimal(out var unitPrice) && unitPrice >= 0)
                {
                    output.AppendLine("   Ціна: " + Money(unitPrice));
                    if (item.TryGetProperty("quantity", out var q) &&
                        q.TryGetDecimal(out var quantity))
                        output.AppendLine("   Вартість: " + Money(quantity * unitPrice));
                }
                output.AppendLine();
            }
        }
        output.AppendLine("Лише попередній перегляд. Дані не проведено й не імпортовано.");
        return LimitPreview(output);
    }

    private static string? Value(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(property, out var child)) return null;
        return child.ValueKind switch
        {
            JsonValueKind.String => Limit(child.GetString(), 500),
            JsonValueKind.Number => child.GetRawText(),
            JsonValueKind.True => "так",
            JsonValueKind.False => "ні",
            _ => null
        };
    }

    private static string Number(decimal value) => value.ToString("0.###", UiCulture);
    private static string Money(decimal value) => value.ToString("N2", UiCulture) + " грн";

    private static string? Limit(string? value, int count) =>
        value is not null && value.Length > count ? value[..count] + "…" : value;

    private static string LimitPreview(StringBuilder output)
    {
        var result = output.ToString();
        return result.Length > MaxPreviewCharacters
            ? result[..MaxPreviewCharacters] + "\n[Скорочено]"
            : result;
    }

    private static string Pretty(JsonElement root)
    {
        // This encoder prints readable Ukrainian text instead of \u04xx escapes.
        // This output is display-only and never used as an import source.
        var result = JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        return result.Length > MaxPreviewCharacters
            ? result[..MaxPreviewCharacters] + "\n[Скорочено]"
            : result;
    }
}
