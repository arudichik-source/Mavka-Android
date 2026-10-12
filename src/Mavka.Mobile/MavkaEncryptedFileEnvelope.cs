using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// Opt-in binary PDF/XLSX transport; does not change the already-approved
/// MAVKA_ENCRYPTED_V1 JSON protocol or any exchange-key lifecycle rule.
/// Wire format: MAVKA_ENCRYPTED_FILE_V1 as a .mavkafile JSON attachment.
/// Authenticated associated data binds all cleartext metadata to the payload.
/// The recipient must still verify the authorized Gmail From address.
/// </summary>
internal static class MavkaEncryptedFileEnvelope
{
    public const string Format = "MAVKA_ENCRYPTED_FILE_V1";
    public const string Extension = ".mavkafile";
    public const int MaxFileBytes = 32_000_000;
    public const int MaxSingleFileBytes = 2_000_000;
    public const int MaxEnvelopeBytes = 44_000_000;
    public const int MaxSingleEnvelopeBytes = 3_100_000;
    public const int MaxSingleWireBytes = 4_300_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal sealed record ReceivedFile(string FileName, string MimeType,
        byte[] Content, string Sha256) : IDisposable
    {
        public long Length => Content.LongLength;
        public void Dispose() => CryptographicOperations.ZeroMemory(Content);
    }

    public static string Encrypt(string fileName, ReadOnlySpan<byte> content, byte[] key)
    {
        var mime = MimeFor(fileName);
        if (key.Length != 32) throw new ArgumentException("AES-256 key expected.");
        ValidateContents(mime, content);
        var id = KeyId(key);
        var sha = Convert.ToHexString(SHA256.HashData(content));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[content.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, content, ciphertext, tag,
                AAD(id, fileName, mime, content.Length, sha));
            return JsonSerializer.Serialize(new
            {
                format = Format,
                algorithm = "AES-256-GCM",
                keyId = id,
                fileName,
                mimeType = mime,
                size = content.Length,
                sha256 = sha,
                nonce = Convert.ToBase64String(nonce),
                ciphertext = Convert.ToBase64String(ciphertext),
                tag = Convert.ToBase64String(tag)
            });
        }
        finally { CryptographicOperations.ZeroMemory(ciphertext); }
    }

    public static ReceivedFile Decrypt(string envelope,
        Func<string, byte[]?> keyResolver)
    {
        if (envelope.Length is < 100 or > MaxEnvelopeBytes)
            throw new ArgumentException("Розмір зашифрованого вкладення недопустимий.");
        using var doc = JsonDocument.Parse(envelope, new JsonDocumentOptions
        {
            MaxDepth = 4, AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            String(root, "format") != Format ||
            String(root, "algorithm") != "AES-256-GCM")
            throw new ArgumentException("Непідтримуваний формат захищеного файла.");

        var keyId = String(root, "keyId");
        if (keyId.Length != 16 || !keyId.All(IsLowerHex))
            throw new ArgumentException("Некоректний ідентифікатор ключа.");
        var fileName = String(root, "fileName");
        var expectedMime = MimeFor(fileName);
        var mime = String(root, "mimeType");
        if (!string.Equals(mime, expectedMime, StringComparison.Ordinal))
            throw new ArgumentException("Тип файлу не відповідає назві.");

        if (!root.TryGetProperty("size", out var sizeValue) ||
            sizeValue.ValueKind != JsonValueKind.Number ||
            !sizeValue.TryGetInt32(out int size) ||
            size is < 8 or > MaxFileBytes)
            throw new ArgumentException("Недопустимий розмір вкладення.");

        var digest = String(root, "sha256");
        if (digest.Length != 64 || !digest.All(IsUpperHex))
            throw new ArgumentException("Некоректна контрольна сума.");

        var nonce = Convert.FromBase64String(String(root, "nonce"));
        var ciphertext = Convert.FromBase64String(String(root, "ciphertext"));
        var tag = Convert.FromBase64String(String(root, "tag"));
        if (nonce.Length != 12 || tag.Length != 16 || ciphertext.Length != size)
            throw new ArgumentException("Некоректна структура зашифрованого файла.");

        var key = keyResolver(keyId) ??
            throw new KeyNotFoundException("Відповідний ключ обміну недоступний.");
        byte[]? plain = null;
        try
        {
            if (key.Length != 32 || !string.Equals(KeyId(key), keyId,
                    StringComparison.Ordinal))
                throw new CryptographicException("Key identifier mismatch.");
            plain = new byte[size];
            using (var aes = new AesGcm(key, 16))
                aes.Decrypt(nonce, ciphertext, tag, plain,
                    AAD(keyId, fileName, mime, size, digest));
            ValidateContents(mime, plain);
            var actualDigest = SHA256.HashData(plain);
            if (!CryptographicOperations.FixedTimeEquals(
                    actualDigest, Convert.FromHexString(digest)))
                throw new CryptographicException("Content checksum mismatch.");
            var result = new ReceivedFile(fileName, mime, plain, digest);
            plain = null; // Ownership transferred only after authenticated validation.
            return result;
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static string MimeFor(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 160 ||
            fileName is "." or ".." ||
            fileName.Any(c => char.IsControl(c) || c is '/' or '\\' or ':'))
            throw new ArgumentException("Некоректна назва файла.");
        if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return "application/pdf";
        if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        if (fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        if (fileName.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)) return "application/msword";
        if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return "image/png";
        if (fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) return "image/jpeg";
        if (fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return "image/webp";
        throw new ArgumentException("Підтримуються PDF, XLSX, DOCX, DOC та PNG/JPEG/WebP.");
    }

    internal static void ValidateContents(string mime, ReadOnlySpan<byte> data)
    {
        if (data.Length is < 8 or > MaxFileBytes)
            throw new ArgumentException("Розмір PDF/XLSX за межами дозволеного.");
        if (mime == "application/pdf")
        {
            if (!data.StartsWith("%PDF-"u8))
                throw new ArgumentException("Вміст не має заголовка PDF.");
            return;
        }
        // Photos are accepted only with a valid magic number. Their entire
        // content is still authenticated by AES-GCM and checked with SHA-256.
        if (mime is "image/png" or "image/jpeg" or "image/webp")
        {
            bool valid = mime switch
            {
                "image/png" => data.StartsWith(new byte[] {137,80,78,71,13,10,26,10}),
                "image/jpeg" => data.Length >= 12 && data[0] == 0xff &&
                    data[1] == 0xd8 && data[2] == 0xff,
                _ => data.Length >= 12 && data.Slice(0,4).SequenceEqual("RIFF"u8) &&
                    data.Slice(8,4).SequenceEqual("WEBP"u8)
            };
            if (!valid) throw new ArgumentException("Некоректний формат зображення.");
            return;
        }
        // OOXML XLSX = ZIP, or password-encrypted Excel = OLE compound file.
        ReadOnlySpan<byte> compound =
            [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        ReadOnlySpan<byte> zip = [0x50, 0x4B, 0x03, 0x04];
        if (mime == "application/msword" ? !data.StartsWith(compound) :
            !data.StartsWith(zip) && !data.StartsWith(compound))
            throw new ArgumentException("Вміст не має заголовка XLSX.");
    }

    private static string String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? "" : throw new ArgumentException("Відсутнє поле " + name);

    private static byte[] AAD(string id, string name, string mime, int length, string hash) =>
        StrictUtf8.GetBytes(Format + ":" + id + ":" + name + ":" + mime + ":" +
            length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":" + hash);

    private static string KeyId(byte[] key) =>
        Convert.ToHexString(SHA256.HashData(key))[..16].ToLowerInvariant();
    private static bool IsLowerHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';
    private static bool IsUpperHex(char c) => c is >= '0' and <= '9' or >= 'A' and <= 'F';
}

