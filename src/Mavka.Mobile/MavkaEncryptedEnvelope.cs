using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// File format MAVKA_ENCRYPTED_V1:
/// {"format":"MAVKA_ENCRYPTED_V1","algorithm":"AES-256-GCM",
///  "keyId":"16 hex chars","nonce":"12 bytes Base64",
///  "ciphertext":"Base64","tag":"16 bytes Base64"}
/// AAD = UTF8("MAVKA_ENCRYPTED_V1:" + keyId).
/// </summary>
internal static class MavkaEncryptedEnvelope
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool IsEnvelope(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 8, AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            return parsed.RootElement.ValueKind == JsonValueKind.Object &&
                parsed.RootElement.TryGetProperty("format", out var format) &&
                format.ValueKind == JsonValueKind.String &&
                format.GetString() == "MAVKA_ENCRYPTED_V1";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string Decrypt(string envelope, ExchangeKeyStore store)
    {
        if (envelope.Length > 65000)
            throw new ArgumentException("Зашифрований файл завеликий.");

        using var parsed = JsonDocument.Parse(envelope, new JsonDocumentOptions { MaxDepth = 8 });
        var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            Field(root, "format") != "MAVKA_ENCRYPTED_V1" ||
            Field(root, "algorithm") != "AES-256-GCM")
            throw new ArgumentException("Непідтримуваний формат шифрування.");

        string keyId = Field(root, "keyId") ?? "";
        if (keyId.Length != 16 || !keyId.All(Uri.IsHexDigit))
            throw new ArgumentException("Некоректний ідентифікатор ключа.");

        byte[]? key = store.GetKey(keyId);
        if (key is null)
            throw new KeyNotFoundException("Немає відповідного ключа. Відкрий меню «Ключ обміну».");

        try
        {
            var nonce = Convert.FromBase64String(Field(root, "nonce") ?? "");
            var ciphertext = Convert.FromBase64String(Field(root, "ciphertext") ?? "");
            var tag = Convert.FromBase64String(Field(root, "tag") ?? "");
            if (nonce.Length != 12 || tag.Length != 16 ||
                ciphertext.Length < 1 || ciphertext.Length > 32768)
                throw new ArgumentException("Неприпустимий розмір зашифрованих даних.");

            var plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, ciphertext, tag, plaintext,
                    Encoding.UTF8.GetBytes("MAVKA_ENCRYPTED_V1:" + keyId));
                return StrictUtf8.GetString(plaintext);
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static string Encrypt(string plaintextJson, ExchangeKeyStore store)
    {
        if (!store.HasKeyPair)
            throw new InvalidOperationException("No active exchange-key package.");
        if (plaintextJson.Length is < 1 or > 18000)
            throw new ArgumentException("Outgoing message exceeds the supported size.");

        using (var check = JsonDocument.Parse(plaintextJson))
            if (check.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Expected JSON object.");

        byte[]? key = store.GetCurrentKey() ??
            throw new InvalidOperationException("Active exchange key is unavailable.");
        try
        {
            var keyId = store.CurrentKeyId!;
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plain = StrictUtf8.GetBytes(plaintextJson);
            try
            {
                var ciphertext = new byte[plain.Length];
                var tag = new byte[16];
                using (var aes = new AesGcm(key, 16))
                    aes.Encrypt(nonce, plain, ciphertext, tag,
                        Encoding.UTF8.GetBytes("MAVKA_ENCRYPTED_V1:" + keyId));
                return JsonSerializer.Serialize(new
                {
                    format = "MAVKA_ENCRYPTED_V1",
                    algorithm = "AES-256-GCM",
                    keyId,
                    nonce = Convert.ToBase64String(nonce),
                    ciphertext = Convert.ToBase64String(ciphertext),
                    tag = Convert.ToBase64String(tag)
                });
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static string? Field(JsonElement root, string name) =>
        root.TryGetProperty(name, out var field) &&
        field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
