using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// Read-only display of JSON attachments. Never imports, approves, or changes accounting records.
/// </summary>
internal static class MavkaJsonPreview
{
    private const int MaxCharacters = 65000;

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

            if (!string.Equals(Value(root, "format"), "MAVKA_DOCUMENT_V1",
                    StringComparison.Ordinal))
            {
                return "Коректний JSON. Формат обміну Мавки не визначений.\n" +
                       "Показано вміст для перегляду, без імпорту:\n\n" + Pretty(root);
            }

            var output = new StringBuilder();
            output.AppendLine("Документ МАВКИ (перегляд)");
            output.AppendLine("Формат: MAVKA_DOCUMENT_V1");
            output.AppendLine("Тип: " + (Value(root, "type") ?? "не вказано"));
            output.AppendLine("Номер: " + (Value(root, "documentNumber") ?? "не вказано"));
            output.AppendLine("Дата: " + (Value(root, "date") ?? "не вказано"));
            output.AppendLine("Отримувач: " + (Value(root, "recipient") ?? "не вказано"));

            if (root.TryGetProperty("items", out var items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                output.AppendLine();
                output.AppendLine("Позиції (" + items.GetArrayLength() + "):");
                var shown = 0;
                foreach (var item in items.EnumerateArray())
                {
                    if (shown++ >= 200)
                    {
                        output.AppendLine("… Інші позиції приховано.");
                        break;
                    }
                    output.AppendLine(shown + ". " + (Value(item, "name") ?? "Без назви"));
                    output.AppendLine("   Кількість: " + (Value(item, "quantity") ?? "?") +
                                      " " + (Value(item, "unit") ?? ""));
                }
            }
            output.AppendLine();
            output.AppendLine("Лише попередній перегляд. Дані не проведено й не імпортовано.");
            var rendered = output.ToString();
            return rendered.Length > 30000 ? rendered[..30000] + "\n[Скорочено]" : rendered;
        }
        catch (JsonException)
        {
            return "Некоректний JSON. Перевір синтаксис надісланого файлу.";
        }
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

    private static string? Limit(string? value, int count) =>
        value is not null && value.Length > count ? value[..count] + "…" : value;

    private static string Pretty(JsonElement root)
    {
        var result = JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        return result.Length > 30000
            ? result[..30000] + "\n[Скорочено]"
            : result;
    }
}
