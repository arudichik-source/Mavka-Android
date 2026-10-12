using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Mavka.Mobile;

internal static class MavkaFileParts
{
    public const string Format = "MAVKA_FILE_PART_V1";
    public const string Extension = ".mavkapart";
    public const int ChunkBytes = 256_000;
    public const int MaxFileBytes = 32_000_000;
    public const int MaxPartBytes = 360_000;
    public const int MaxWireBytes = 510_000;
    public const int MaxBundleBytes = 46_000_000;
    internal sealed record Header(string TransferId, string FileName, string Mime,
        int Size, string Sha256, int Index, int Count);
    internal sealed record Packet(string Format, string Algorithm, string KeyId,
        Header Metadata, byte[] Nonce, byte[] Ciphertext, byte[] Tag);
    internal sealed record Part(Header Metadata, byte[] Content) : IDisposable
    { public void Dispose() => CryptographicOperations.ZeroMemory(Content); }
    internal static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 8 };
    public static bool IsId(string? id) => id is { Length: 32 } &&
        id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string KeyId(byte[] key) => Convert.ToHexString(SHA256.HashData(key))[..16].ToLowerInvariant();
    private static byte[] Aad(Header h, string keyId) =>
        JsonSerializer.SerializeToUtf8Bytes(new { format = Format, keyId, metadata = h }, Json);
    public static void Validate(Header h)
    {
        if (!IsId(h.TransferId) || h.Size is < 8 or > MaxFileBytes ||
            h.Mime != MavkaEncryptedFileEnvelope.MimeFor(h.FileName) ||
            h.Sha256.Length != 64 || !h.Sha256.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F') ||
            h.Count != (h.Size + ChunkBytes - 1) / ChunkBytes || h.Index < 0 || h.Index >= h.Count)
            throw new ArgumentException("Некоректні метадані частини файла.");
    }
    public static Header Describe(string name, ReadOnlySpan<byte> content, string? transferId = null)
    {
        if (content.Length is < 8 or > MaxFileBytes) throw new ArgumentException("Файл перевищує 32 МБ.");
        var mime = MavkaEncryptedFileEnvelope.MimeFor(name);
        MavkaEncryptedFileEnvelope.ValidateContents(mime, content);
        var h = new Header(transferId ?? Guid.NewGuid().ToString("N"), name, mime,
            content.Length, Convert.ToHexString(SHA256.HashData(content)), 0,
            (content.Length + ChunkBytes - 1) / ChunkBytes);
        Validate(h); return h;
    }
    public static string Encrypt(Header metadata, ReadOnlySpan<byte> content, byte[] key)
    {
        Validate(metadata);
        int expected = Math.Min(ChunkBytes, metadata.Size - metadata.Index * ChunkBytes);
        if (key.Length != 32 || content.Length != expected) throw new ArgumentException("Некоректний розмір частини.");
        byte[] nonce = RandomNumberGenerator.GetBytes(12), cipher = new byte[content.Length], tag = new byte[16];
        string id = KeyId(key);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, content, cipher, tag, Aad(metadata, id));
        return JsonSerializer.Serialize(new Packet(Format, "AES-256-GCM", id, metadata, nonce, cipher, tag), Json);
    }
    public static Part Decrypt(string envelope, Func<string, byte[]?> resolve)
    {
        if (Encoding.UTF8.GetByteCount(envelope) > MaxPartBytes) throw new ArgumentException("Частина завелика.");
        var p = JsonSerializer.Deserialize<Packet>(envelope, Json) ?? throw new ArgumentException("Порожня частина.");
        if (p.Format != Format || p.Algorithm != "AES-256-GCM" || p.Metadata is null ||
            p.KeyId is not { Length: 16 } || !p.KeyId.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            p.Nonce is not { Length: 12 } || p.Tag is not { Length: 16 } || p.Ciphertext is null)
            throw new ArgumentException("Некоректний контейнер частини.");
        Validate(p.Metadata);
        if (p.Ciphertext.Length != Math.Min(ChunkBytes, p.Metadata.Size - p.Metadata.Index * ChunkBytes))
            throw new ArgumentException("Неправильна довжина частини.");
        var key = resolve(p.KeyId) ?? throw new KeyNotFoundException("Немає ключа для частини файла.");
        byte[] plain = new byte[p.Ciphertext.Length];
        try
        {
            if (key.Length != 32 || KeyId(key) != p.KeyId) throw new CryptographicException("Key mismatch.");
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(p.Nonce, p.Ciphertext, p.Tag, plain, Aad(p.Metadata, p.KeyId));
            return new Part(p.Metadata, plain);
        }
        catch { CryptographicOperations.ZeroMemory(plain); throw; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static bool SameFile(Header a, Header b) => (a with { Index = 0 }) == (b with { Index = 0 });
}
