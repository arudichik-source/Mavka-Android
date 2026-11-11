using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// Offline QR is an invitation, NOT authorization to read documents or keys.
/// Main Mavka proves possession of the 256-bit QR secret by sending a
/// short-lived HMAC acknowledgment over the already configured Gmail channel.
/// </summary>
internal static class QrPairingProtocol
{
    public const string Format = "MAVKA_QR_PAIR_V1";
    public const string AckFormat = "MAVKA_QR_PAIR_ACK_V1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public sealed record Invitation(string MainEmail, string SessionId,
        string SharedSecret, DateTimeOffset ExpiresUtc);
    public sealed record Ack(string SessionId, string ClientId, string Mac);

    public static string Generate(string mainEmail, DateTimeOffset now)
    {
        ValidateMail(mainEmail);
        return JsonSerializer.Serialize(new
        {
            format = Format,
            mainEmail = mainEmail.Trim().ToLowerInvariant(),
            sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            expiresUtc = now.Add(Lifetime).ToString("O")
        });
    }

    public static Invitation ParseInvitation(string json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1400)
            throw new ArgumentException("Недопустимий QR-код.");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        var root = doc.RootElement;
        if (root.GetProperty("format").GetString() != Format)
            throw new ArgumentException("Це не QR-код реєстрації Мавки.");
        var email = root.GetProperty("mainEmail").GetString() ?? "";
        ValidateMail(email);
        var id = root.GetProperty("sessionId").GetString() ?? "";
        var secret = root.GetProperty("secret").GetString() ?? "";
        var expires = DateTimeOffset.Parse(
            root.GetProperty("expiresUtc").GetString() ?? "",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        if (id.Length != 32 || !id.All(Uri.IsHexDigit) ||
            Convert.FromBase64String(secret).Length != 32 ||
            expires <= now || expires > now.Add(Lifetime))
            throw new ArgumentException("QR-код прострочений або містить некоректні дані.");
        return new Invitation(email, id, secret, expires);
    }

    public static string CreateAck(Invitation invitation, string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 100)
            throw new ArgumentException("Client ID required.");
        var key = Convert.FromBase64String(invitation.SharedSecret);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(
                AckFormat + ":" + invitation.SessionId + ":" + clientId);
            var mac = HMACSHA256.HashData(key, bytes);
            return JsonSerializer.Serialize(new
            {
                format = AckFormat, sessionId = invitation.SessionId,
                clientId, mac = Convert.ToBase64String(mac)
            });
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static bool VerifyAck(string ackJson, Invitation invitation,
        string clientId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ackJson) || ackJson.Length > 2000 ||
            now >= invitation.ExpiresUtc) return false;
        try
        {
            using var doc = JsonDocument.Parse(ackJson, new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.GetProperty("format").GetString() != AckFormat ||
                root.GetProperty("sessionId").GetString() != invitation.SessionId ||
                root.GetProperty("clientId").GetString() != clientId)
                return false;
            var actual = Convert.FromBase64String(root.GetProperty("mac").GetString() ?? "");
            var secret = Convert.FromBase64String(invitation.SharedSecret);
            try
            {
                var expected = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(
                    AckFormat + ":" + invitation.SessionId + ":" + clientId));
                return actual.Length == expected.Length &&
                    CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            finally { CryptographicOperations.ZeroMemory(secret); }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or ArgumentException) { return false; }
    }

    public static bool IsAck(string? text) =>
        text is not null && text.Length < 2000 &&
        text.Contains(AckFormat, StringComparison.Ordinal);

    private static void ValidateMail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 ||
            !System.Net.Mail.MailAddress.TryCreate(email.Trim(), out var parsed) ||
            !string.Equals(parsed?.Address, email.Trim(),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Поштову адресу ГОЛОВНОЇ МАВКИ не підтверджено.");
    }
}
