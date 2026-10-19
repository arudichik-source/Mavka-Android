using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;
// The existing MAVKA_ENCRYPTED_V1 format, with a bounded larger inner payload.
// This is not the outer Relay envelope (whose ciphertext stays <= 32768 bytes).
internal static class RelayContentCipher
{
    public static string Encrypt(string json, byte[] key, string keyId)
    {
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 14 });
        if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Потрібен JSON.");
        var plain = new UTF8Encoding(false, true).GetBytes(json);
        try
        {
            if (plain.Length is < 1 or > 4_400_000) throw new ArgumentException("Завеликий вміст для Relay.");
            var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plain.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes("MAVKA_ENCRYPTED_V1:" + keyId));
            var result = RelayProtocol.Serialize(new { format = "MAVKA_ENCRYPTED_V1", algorithm = "AES-256-GCM",
                keyId, nonce = Convert.ToBase64String(nonce), ciphertext = Convert.ToBase64String(cipher), tag = Convert.ToBase64String(tag) });
            if (Encoding.UTF8.GetByteCount(result) > RelayProtocol.MaxContentBytes) throw new ArgumentException("Завеликий зашифрований JSON.");
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public static string Decrypt(string json, Func<string, byte[]?> getKey)
    {
        if (Encoding.UTF8.GetByteCount(json) > RelayProtocol.MaxContentBytes) throw new ArgumentException("Завеликий зашифрований JSON.");
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var e = parsed.RootElement;
        string Field(string name) => e.GetProperty(name).GetString() ?? throw new CryptographicException("Неповний конверт.");
        if (Field("format") != "MAVKA_ENCRYPTED_V1" || Field("algorithm") != "AES-256-GCM")
            throw new CryptographicException("Непідтримуваний конверт.");
        var id = Field("keyId");
        if (id.Length != 16 || !id.All(Uri.IsHexDigit)) throw new CryptographicException("Некоректний keyId.");
        var nonce = Convert.FromBase64String(Field("nonce")); var tag = Convert.FromBase64String(Field("tag"));
        var cipher = Convert.FromBase64String(Field("ciphertext"));
        if (nonce.Length != 12 || tag.Length != 16 || cipher.Length is < 1 or > 4_500_000)
            throw new CryptographicException("Некоректний розмір конверта.");
        var key = getKey(id) ?? throw new KeyNotFoundException("Потрібен відповідний ключ обміну.");
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes("MAVKA_ENCRYPTED_V1:" + id));
            var text = new UTF8Encoding(false, true).GetString(plain);
            using var check = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 14 });
            if (check.RootElement.ValueKind != JsonValueKind.Object) throw new CryptographicException("Потрібен JSON.");
            return text;
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
}
