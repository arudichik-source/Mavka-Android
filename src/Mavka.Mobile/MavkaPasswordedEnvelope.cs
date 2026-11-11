using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// Optional, backward-compatible outer password layer over an ALREADY
/// exchange-key-encrypted document/part. The original current/future key
/// scheme and the inner MAVKA_* wire formats are never modified.
/// Password is not serialized. Encrypted attachments are never plaintext ZIP.
/// </summary>
internal static class MavkaPasswordedEnvelope
{
    public const string Format = "MAVKA_PASSWORD_WRAPPED_V1";
    public const int Iterations = 350_000;
    public const int MaxWrappedBytes = 64_000_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] Aad = StrictUtf8.GetBytes(Format);

    public static string Wrap(string encryptedDocument, string password)
    {
        ValidatePassword(password);
        if (string.IsNullOrWhiteSpace(encryptedDocument) ||
            StrictUtf8.GetByteCount(encryptedDocument) > 46_000_000)
            throw new ArgumentException("Некоректний розмір захищеного файла.");
        ValidateInnerFormat(encryptedDocument);
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt,
            Iterations, HashAlgorithmName.SHA256, 32);
        byte[] plaintext = StrictUtf8.GetBytes(encryptedDocument);
        byte[] ciphertext = new byte[plaintext.Length], tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Aad);
            return JsonSerializer.Serialize(new
            {
                format = Format, algorithm = "AES-256-GCM",
                kdf = "PBKDF2-SHA256", iterations = Iterations,
                salt = Convert.ToBase64String(salt),
                nonce = Convert.ToBase64String(nonce),
                ciphertext = Convert.ToBase64String(ciphertext),
                tag = Convert.ToBase64String(tag)
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public static string Unwrap(string envelope, string password)
    {
        ValidatePassword(password);
        if (string.IsNullOrWhiteSpace(envelope) ||
            StrictUtf8.GetByteCount(envelope) > MaxWrappedBytes)
            throw new ArgumentException("Неприпустимий розмір парольного контейнера.");
        using var doc = JsonDocument.Parse(envelope, new JsonDocumentOptions
        {
            MaxDepth = 4, AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        var root = doc.RootElement;
        string Str(string field) => root.TryGetProperty(field, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : throw new ArgumentException("Missing " + field);
        if (Str("format") != Format || Str("algorithm") != "AES-256-GCM" ||
            Str("kdf") != "PBKDF2-SHA256" ||
            !root.TryGetProperty("iterations", out var count) ||
            !count.TryGetInt32(out int iterations) || iterations != Iterations)
            throw new ArgumentException("Невідомий формат парольного шифрування.");
        var salt = Convert.FromBase64String(Str("salt"));
        var nonce = Convert.FromBase64String(Str("nonce"));
        var tag = Convert.FromBase64String(Str("tag"));
        var ciphertext = Convert.FromBase64String(Str("ciphertext"));
        if (salt.Length != 16 || nonce.Length != 12 || tag.Length != 16 ||
            ciphertext.Length is < 20 or > 46_000_000)
            throw new ArgumentException("Парольний контейнер пошкоджений.");
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations,
            HashAlgorithmName.SHA256, 32);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Aad);
            var recovered = StrictUtf8.GetString(plaintext);
            ValidateInnerFormat(recovered);
            return recovered;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static bool IsWrapped(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var doc = JsonDocument.Parse(payload,
                new JsonDocumentOptions { MaxDepth = 4 });
            return doc.RootElement.TryGetProperty("format", out var f) &&
                f.GetString() == Format;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void ValidateInnerFormat(string ciphertextJson)
    {
        using var doc = JsonDocument.Parse(ciphertextJson,
            new JsonDocumentOptions { MaxDepth = 8 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("format", out var format) ||
            format.ValueKind != JsonValueKind.String ||
            format.GetString() is not (
                "MAVKA_ENCRYPTED_V1" or "MAVKA_ENCRYPTED_FILE_V1" or
                "MAVKA_FILE_PART_V1"))
            throw new ArgumentException("Password layer requires an exchange-key encrypted envelope.");
    }

    private static void ValidatePassword(string password)
    {
        if (password is null || password.Length is < 6 or > 128)
            throw new ArgumentException("Пароль має містити від 6 до 128 символів.");
    }
}
