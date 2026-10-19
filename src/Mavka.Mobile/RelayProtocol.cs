using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mavka.Mobile;

internal sealed record RelayJwk(string kty, string crv, string x, string y);
internal sealed record RelayVerified(string Id, string Hash, long ExpiresAt, string Envelope);
internal sealed record RelayPart(string clientId, string transferId, int index, int count, int size, string sha256, string data);
internal static class RelayProtocol
{
    public const string Origin = "https://mavka-relay.arudichik.workers.dev";
    public const string MainId = "5110f288-ed5f-4d7f-afb5-2c2501f50076";
    public static readonly RelayJwk MainKey = new("EC", "P-256",
        "CnlXB4fypB0siFfCtSrgL5NoRTcMF-ZUQo6_SuINXpo",
        "0XW_nZEm7LIg51pc5N0KTl1QrQStQZ3NTEFSndG5j_o");
    public static readonly JsonSerializerOptions Json = new()
    { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = 12 };
    public const int ChunkBytes = 12000, MaxContentBytes = 6_000_000;
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] UnB64(string data, int size)
    {
        if (data.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new CryptographicException("Некоректний підпис.");
        var normalized = data.Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
        if (bytes.Length != size || B64(bytes) != data) throw new CryptographicException("Некоректний розмір ключа або підпису.");
        return bytes;
    }
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    public static void Id(string id)
    {
        if (id.Length is < 16 or > 80 || !char.IsAsciiLetterOrDigit(id[0]) || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Некоректний ID пристрою або пакета.");
    }
    public static ECDsa Public(RelayJwk jwk)
    {
        if (jwk.kty != "EC" || jwk.crv != "P-256") throw new CryptographicException("Потрібен P-256.");
        return ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = UnB64(jwk.x, 32), Y = UnB64(jwk.y, 32) } });
    }
    public static RelayJwk Jwk(byte[] spki)
    {
        using var key = ECDsa.Create(); key.ImportSubjectPublicKeyInfo(spki, out var used);
        if (used != spki.Length || key.KeySize != 256) throw new CryptographicException("Некоректний публічний ключ.");
        var p = key.ExportParameters(false);
        return new("EC", "P-256", B64(p.Q.X!), B64(p.Q.Y!));
    }
    public static byte[] Canonical(string method, string path, string device, string time, string nonce, string body)
    {
        Id(device); _ = UnB64(nonce, 16);
        if (time.Length != 10 || !time.All(char.IsAsciiDigit) ||
            !path.StartsWith("/v1/", StringComparison.Ordinal) || path.Contains('\n') ||
            path.Contains('\r') || !new[] { "GET", "POST" }.Contains(method))
            throw new ArgumentException("Некоректний запит Relay.");
        return Encoding.UTF8.GetBytes(string.Join("\n", "MAVKA_RELAY_REQUEST_V1",
            Origin, method, path, device, time, nonce, Hash(body)));
    }
    public static byte[] RawSignature(byte[] der)
    {
        // AndroidKeyStore emits ASN.1 DER; the server accepts raw P1363 r||s.
        var reader = new System.Formats.Asn1.AsnReader(der, System.Formats.Asn1.AsnEncodingRules.DER);
        var sequence = reader.ReadSequence(); var r = sequence.ReadIntegerBytes().Span;
        var s = sequence.ReadIntegerBytes().Span;
        reader.ThrowIfNotEmpty(); sequence.ThrowIfNotEmpty();
        var output = new byte[64];
        void Copy(ReadOnlySpan<byte> input, int offset)
        {
            if (input.Length == 0 || (input[0] & 128) != 0) throw new CryptographicException("Некоректний ECDSA.");
            if (input.Length == 33 && input[0] == 0) input = input[1..];
            if (input.Length > 32) throw new CryptographicException("Некоректний ECDSA.");
            input.CopyTo(output.AsSpan(offset + 32 - input.Length));
        }
        Copy(r, 0); Copy(s, 32); return output;
    }
    private static string Field(JsonElement e, string name) => e.GetProperty(name).GetString() ??
        throw new CryptographicException("Неповний пакет.");
    public static JsonElement Envelope(JsonElement payload)
    {
        if (Field(payload, "format") != "MAVKA_ENCRYPTED_V1" ||
            Field(payload, "algorithm") != "AES-256-GCM") throw new CryptographicException("Потрібен шифрований пакет.");
        if (payload.EnumerateObject().Count() != 6 ||
            payload.EnumerateObject().Any(p => !new[] { "format", "algorithm", "keyId", "nonce", "ciphertext", "tag" }.Contains(p.Name)))
            throw new CryptographicException("Некоректні поля конверта.");
        var keyId = Field(payload, "keyId");
        if (keyId.Length != 16 || !keyId.All(Uri.IsHexDigit)) throw new CryptographicException("Некоректний keyId.");
        var nonce = Convert.FromBase64String(Field(payload, "nonce"));
        var cipher = Convert.FromBase64String(Field(payload, "ciphertext"));
        var tag = Convert.FromBase64String(Field(payload, "tag"));
        if (nonce.Length != 12 || cipher.Length is < 1 or > 32768 || tag.Length != 16 ||
            Convert.ToBase64String(nonce) != Field(payload, "nonce") ||
            Convert.ToBase64String(cipher) != Field(payload, "ciphertext") ||
            Convert.ToBase64String(tag) != Field(payload, "tag"))
            throw new CryptographicException("Розмір пакета перевищує ліміт Relay.");
        return JsonSerializer.SerializeToElement(new { format = "MAVKA_ENCRYPTED_V1",
            algorithm = "AES-256-GCM", keyId,
            nonce = Convert.ToBase64String(nonce), ciphertext = Convert.ToBase64String(cipher),
            tag = Convert.ToBase64String(tag) }, Json);
    }
    public static string Fingerprint(string sender, JsonElement body)
    {
        return Hash(Serialize(new { senderDeviceId = sender, messageId = Field(body, "messageId"),
            recipientDeviceId = Field(body, "recipientDeviceId"),
            expiresAt = body.GetProperty("expiresAt").GetInt64(), payload = Envelope(body.GetProperty("payload")) }));
    }
    public static RelayVerified Verify(JsonElement delivery, string recipient, DateTimeOffset now) =>
        VerifyFrom(delivery, recipient, now, MainId, MainKey);
    internal static RelayVerified VerifyFrom(JsonElement delivery, string recipient, DateTimeOffset now, string senderId, RelayJwk trustedKey)
    {
        var packet = delivery.GetProperty("packet");
        if (Field(packet, "format") != "MAVKA_RELAY_STORED_V1" ||
            Field(packet, "senderDeviceId") != senderId || Field(delivery, "senderDeviceId") != senderId)
            throw new CryptographicException("Відправник не є довіреною ГОЛОВНОЮ МАВКОЮ.");
        var text = Field(packet, "requestBody");
        if (Encoding.UTF8.GetByteCount(text) > 49152) throw new CryptographicException("Пакет завеликий.");
        using var key = Public(trustedKey);
        if (!key.VerifyData(Canonical("POST", "/v1/messages", senderId,
            Field(packet, "time"), Field(packet, "nonce"), text),
            UnB64(Field(packet, "signature"), 64), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new CryptographicException("Не пройдено перевірку підпису відправника.");
        using var parsed = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 10 });
        var body = parsed.RootElement;
        var id = Field(body, "messageId"); Id(id);
        var expiry = body.GetProperty("expiresAt").GetInt64();
        var hash = Fingerprint(senderId, body);
        if (id != Field(delivery, "messageId") || Field(body, "recipientDeviceId") != recipient ||
            expiry != delivery.GetProperty("expiresAt").GetInt64() || expiry <= now.ToUnixTimeSeconds() ||
            hash != Field(delivery, "payloadSha256"))
            throw new CryptographicException("Маршрут, строк або хеш пакета не збігається.");
        return new(id, hash, expiry, Serialize(Envelope(body.GetProperty("payload"))));
    }
    private static bool LowerHex(string text, int length) =>
        text.Length == length && text.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
    private static void Client(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 128 || clientId.Any(char.IsControl))
            throw new CryptographicException("Некоректний ClientId передачі.");
    }
    private static void EncryptedContent(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 14 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("format", out var format) ||
            format.GetString() is not ("MAVKA_ENCRYPTED_V1" or "MAVKA_PASSWORD_WRAPPED_V1" or "MAVKA_ENCRYPTED_FILE_V1" or "MAVKA_FILE_PART_V1"))
            throw new CryptographicException("Частини мають містити оригінальний зашифрований JSON.");
    }
    public static IReadOnlyList<string> Split(string encryptedJson, string clientId)
    {
        Client(clientId);
        var bytes = Encoding.UTF8.GetBytes(encryptedJson);
        if (bytes.Length is < 1 or > MaxContentBytes) throw new ArgumentException("Завеликий вміст для Relay.");
        EncryptedContent(encryptedJson);
        var result = new List<string>(); var hash = Hash(bytes); var transfer = Guid.NewGuid().ToString("N");
        var count = (bytes.Length + ChunkBytes - 1) / ChunkBytes;
        for (int i = 0; i < count; i++)
            result.Add(Serialize(new { format = "MAVKA_RELAY_CONTENT_PART_V1", clientId, transferId = transfer,
                index = i, count, size = bytes.Length, sha256 = hash,
                data = Convert.ToBase64String(bytes.AsSpan(i * ChunkBytes, Math.Min(ChunkBytes, bytes.Length - i * ChunkBytes))) }));
        CryptographicOperations.ZeroMemory(bytes);
        return result;
    }
    private static byte[] PartData(RelayPart p)
    {
        Client(p.clientId);
        if (!LowerHex(p.transferId, 32) || !LowerHex(p.sha256, 64) ||
            p.size is < 1 or > MaxContentBytes || p.count != (p.size + ChunkBytes - 1) / ChunkBytes ||
            p.index < 0 || p.index >= p.count || p.data.Length > 16000)
            throw new CryptographicException("Некоректна частина передачі.");
        var bytes = Convert.FromBase64String(p.data);
        if (Convert.ToBase64String(bytes) != p.data ||
            bytes.Length != Math.Min(ChunkBytes, p.size - p.index * ChunkBytes))
            throw new CryptographicException("Некоректний Base64 або розмір частини.");
        return bytes;
    }
    public static RelayPart? Part(string json, string? expectedClientId = null)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 14 });
        var e = doc.RootElement;
        if (!e.TryGetProperty("format", out var format) || format.GetString() != "MAVKA_RELAY_CONTENT_PART_V1") return null;
        var fields = new[] { "format", "clientId", "transferId", "index", "count", "size", "sha256", "data" };
        if (e.EnumerateObject().Count() != fields.Length || e.EnumerateObject().Any(p => !fields.Contains(p.Name)))
            throw new CryptographicException("Некоректні поля частини.");
        var p = new RelayPart(Field(e,"clientId"), Field(e,"transferId"), e.GetProperty("index").GetInt32(),
            e.GetProperty("count").GetInt32(), e.GetProperty("size").GetInt32(), Field(e,"sha256"), Field(e,"data"));
        if (expectedClientId is not null && p.clientId != expectedClientId)
            throw new CryptographicException("Частина адресована іншому профілю.");
        var data = PartData(p); CryptographicOperations.ZeroMemory(data);
        return p;
    }

    public static string? Assemble(IReadOnlyList<RelayPart> parts)
    {
        if (parts.Count == 0) return null;
        var h = parts[0];
        if (parts.Any(p => p.clientId != h.clientId || p.transferId != h.transferId || p.sha256 != h.sha256 ||
            p.size != h.size || p.count != h.count))
            throw new CryptographicException("Конфлікт метаданих передачі.");
        var unique = new Dictionary<int, byte[]>();
        try
        {
            foreach (var p in parts)
            {
                var data = PartData(p);
                if (unique.TryGetValue(p.index, out var old))
                {
                    if (!old.SequenceEqual(data)) throw new CryptographicException("Конфлікт частин.");
                    CryptographicOperations.ZeroMemory(data);
                }
                else unique.Add(p.index, data);
            }
            if (unique.Count != h.count) return null;
            var full = new byte[h.size];
            try
            {
                foreach (var p in unique) p.Value.CopyTo(full, p.Key * ChunkBytes);
                if (Hash(full) != h.sha256) throw new CryptographicException("Контрольна сума передачі не збігається.");
                var json = new UTF8Encoding(false, true).GetString(full);
                EncryptedContent(json);
                return json;
            }
            finally { CryptographicOperations.ZeroMemory(full); }
        }
        finally { foreach (var bytes in unique.Values) CryptographicOperations.ZeroMemory(bytes); }
    }
}
