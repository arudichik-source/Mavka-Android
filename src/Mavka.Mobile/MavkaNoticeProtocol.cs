using System.Globalization;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// A notice is an authenticated JSON attachment, not a Gmail body. The caller
/// must also verify exact configured sender and AES-GCM before parsing.
/// Suggested reminder time NEVER schedules an alarm without local consent.
/// </summary>
internal sealed record MavkaNotice(
    string NoticeId, string Unit, string Title, string Body,
    DateTimeOffset IssuedUtc, DateTimeOffset? SuggestedReminderUtc,
    string Priority);

internal static class MavkaNoticeProtocol
{
    public const string Format = "MAVKA_NOTICE_V1";

    public static bool TryRead(string? json, string expectedUnit,
        DateTimeOffset now, out MavkaNotice? notice)
    {
        notice = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 12000 ||
            string.IsNullOrWhiteSpace(expectedUnit)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 5, AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                Str(root, "format") != Format) return false;
            var id = Str(root, "noticeId");
            var unit = Str(root, "unit");
            var title = Str(root, "title");
            var body = Str(root, "body");
            var priority = Str(root, "priority") ?? "normal";
            var issued = Str(root, "issuedUtc");
            var suggested = Str(root, "suggestedReminderUtc");
            if (id is null || !Guid.TryParse(id, out _) ||
                unit is null || !string.Equals(unit.Trim(), expectedUnit.Trim(),
                    StringComparison.OrdinalIgnoreCase) ||
                title is null || title.Length > 140 ||
                body is null || body.Length > 3000 ||
                priority is not ("normal" or "important") ||
                issued is null ||
                !DateTimeOffset.TryParse(issued, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var issuedAt) ||
                issuedAt > now.AddMinutes(5) || issuedAt < now.AddDays(-180))
                return false;

            DateTimeOffset? remind = null;
            if (suggested is not null)
            {
                if (!DateTimeOffset.TryParse(suggested, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var candidate) ||
                    candidate < now.AddMinutes(-1) ||
                    candidate > now.AddYears(1)) return false;
                remind = candidate;
            }
            notice = new MavkaNotice(id.ToLowerInvariant(), unit.Trim(), title,
                body, issuedAt, remind, priority);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException)
        {
            return false;
        }
    }

    public static string CreateJson(string unit, string title, string body,
        DateTimeOffset now, DateTimeOffset? suggestedReminderUtc = null,
        string priority = "normal")
    {
        var json = JsonSerializer.Serialize(new
        {
            format = Format, noticeId = Guid.NewGuid().ToString("D"),
            unit, title, body, priority, issuedUtc = now.ToUniversalTime().ToString("O"),
            suggestedReminderUtc = suggestedReminderUtc?.ToUniversalTime().ToString("O")
        });
        if (!TryRead(json, unit, now, out _))
            throw new ArgumentException("Invalid notice content or time.");
        return json;
    }

    private static string? Str(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var item) ||
            item.ValueKind != JsonValueKind.String) return null;
        var value = item.GetString()?.Trim();
        return value is { Length: > 0 and <= 4000 } ? value : null;
    }
}
