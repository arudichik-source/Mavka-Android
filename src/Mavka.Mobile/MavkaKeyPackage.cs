using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

// Both sides use exactly the same versioned, password-encrypted file format.
// WARNING: A shared six-digit password is for test use only and can be guessed offline.
internal sealed record MavkaKeyPair(
    int Generation, string RecipientClientId, byte[] CurrentKey, byte[] FutureKey);

internal static class MavkaKeyPackage
{
    public const string Format = "MAVKA_KEY_PACKAGE_V1";
    public const string MailSubject = "MAVKA_KEY_PACKAGE_V1";
    public const int Iterations = 350_000;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly byte[] Context = Encoding.ASCII.GetBytes(Format);

    public static bool IsPackage(string input)
    {
        try
        {
            using var json = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 6 });
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                Get(json.RootElement, "format") == Format;
        }
        catch (JsonException) { return false; }
    }

    public static string Create(MavkaKeyPair pair, string password)
    {
        ValidatePair(pair);
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Document password is required.");

        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var plain = Utf8.GetBytes(JsonSerializer.Serialize(new
        {
            format = "MAVKA_KEY_PAIR_V1",
            generation = pair.Generation,
            recipientClientId = pair.RecipientClientId,
            currentKey = Convert.ToBase64String(pair.CurrentKey),
            futureKey = Convert.ToBase64String(pair.FutureKey)
        }));
        try
        {
            var data = new byte[plain.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(derived, 16))
                aes.Encrypt(nonce, plain, data, tag, Context);
            return JsonSerializer.Serialize(new
            {
                format = Format,
                kdf = "PBKDF2-SHA256",
                iterations = Iterations,
                cipher = "AES-256-GCM",
                salt = Convert.ToBase64String(salt),
                nonce = Convert.ToBase64String(nonce),
                ciphertext = Convert.ToBase64String(data),
                tag = Convert.ToBase64String(tag)
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static MavkaKeyPair Open(string envelope, string password)
    {
        if (envelope.Length is < 10 or > 20000)
            throw new ArgumentException("Invalid key package length.");
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Document password must be configured.");

        using var json = JsonDocument.Parse(envelope, new JsonDocumentOptions
        {
            MaxDepth = 6, AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        var root = json.RootElement;
        if (Get(root, "format") != Format ||
            Get(root, "kdf") != "PBKDF2-SHA256" ||
            Get(root, "cipher") != "AES-256-GCM" ||
            !root.TryGetProperty("iterations", out var iter) ||
            !iter.TryGetInt32(out var rounds) || rounds is < 250_000 or > 900_000)
            throw new ArgumentException("Unsupported key-package configuration.");

        var salt = Convert.FromBase64String(Get(root, "salt") ?? "");
        var nonce = Convert.FromBase64String(Get(root, "nonce") ?? "");
        var data = Convert.FromBase64String(Get(root, "ciphertext") ?? "");
        var tag = Convert.FromBase64String(Get(root, "tag") ?? "");
        if (salt.Length != 16 || nonce.Length != 12 || tag.Length != 16 ||
            data.Length is < 25 or > 8192)
            throw new ArgumentException("Malformed key-package data.");

        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, rounds, HashAlgorithmName.SHA256, 32);
        var plaintext = new byte[data.Length];
        try
        {
            using (var aes = new AesGcm(derived, 16))
                aes.Decrypt(nonce, data, tag, plaintext, Context);
            using var inner = JsonDocument.Parse(plaintext, new JsonDocumentOptions { MaxDepth = 6 });
            var payload = inner.RootElement;
            if (Get(payload, "format") != "MAVKA_KEY_PAIR_V1" ||
                !payload.TryGetProperty("generation", out var generation) ||
                !generation.TryGetInt32(out var number))
                throw new ArgumentException("Invalid key-pair metadata.");

            var pair = new MavkaKeyPair(
                number,
                Get(payload, "recipientClientId") ?? "",
                Convert.FromBase64String(Get(payload, "currentKey") ?? ""),
                Convert.FromBase64String(Get(payload, "futureKey") ?? ""));
            try { ValidatePair(pair); }
            catch
            {
                CryptographicOperations.ZeroMemory(pair.CurrentKey);
                CryptographicOperations.ZeroMemory(pair.FutureKey);
                throw;
            }
            return pair;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string Id(byte[] key) =>
        Convert.ToHexString(SHA256.HashData(key))[..16].ToLowerInvariant();

    private static void ValidatePair(MavkaKeyPair pair)
    {
        if (pair.Generation is < 1 or > 2_000_000_000 ||
            pair.RecipientClientId.Length is < 10 or > 100 ||
            pair.CurrentKey.Length != 32 || pair.FutureKey.Length != 32 ||
            CryptographicOperations.FixedTimeEquals(pair.CurrentKey, pair.FutureKey))
            throw new ArgumentException("Invalid key pair.");
    }

    private static string? Get(JsonElement obj, string field) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(field, out var element) &&
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
