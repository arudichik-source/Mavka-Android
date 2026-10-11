using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length == 2 && args[0] == "create-key")
{
    if (File.Exists(args[1]))
        throw new InvalidOperationException("Key file exists. Refusing to overwrite it.");
    var key = RandomNumberGenerator.GetBytes(32);
    try
    {
        File.WriteAllText(args[1], Convert.ToBase64String(key));
        Console.WriteLine("Key created. ID: " + KeyId(key));
        Console.WriteLine("Keep the key file private. Do not email it with documents.");
    }
    finally { CryptographicOperations.ZeroMemory(key); }
}
else if (args.Length == 4 && args[0] == "encrypt")
{
    var key = Convert.FromBase64String(File.ReadAllText(args[1]).Trim());
    if (key.Length != 32) throw new ArgumentException("Key must contain 32 bytes.");
    try
    {
        if (File.Exists(args[3]))
            throw new InvalidOperationException("Output file exists. Refusing overwrite.");

        var plain = File.ReadAllBytes(args[2]);
        if (plain.Length is < 1 or > 18000)
            throw new ArgumentException("Input JSON must be 1–18000 bytes for Gmail Mobile.");

        // Validate the input before wrapping it into an encrypted envelope.
        using var doc = JsonDocument.Parse(plain);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Expected a JSON object.");

        var id = KeyId(key);
        var nonce = RandomNumberGenerator.GetBytes(12); // Fresh nonce for EVERY file.
        var ciphertext = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, plain, ciphertext, tag,
                Encoding.UTF8.GetBytes("MAVKA_ENCRYPTED_V1:" + id));

        var envelope = new
        {
            format = "MAVKA_ENCRYPTED_V1",
            algorithm = "AES-256-GCM",
            keyId = id,
            nonce = Convert.ToBase64String(nonce),
            ciphertext = Convert.ToBase64String(ciphertext),
            tag = Convert.ToBase64String(tag)
        };

        File.WriteAllText(args[3],
            JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Encrypted file created, key ID: " + id);
    }
    finally { CryptographicOperations.ZeroMemory(key); }
}
else
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/MavkaEncrypt -- create-key PRIVATE.mavka-key");
    Console.WriteLine("  dotnet run --project tools/MavkaEncrypt -- encrypt PRIVATE.mavka-key input.json encrypted.json");
}

static string KeyId(byte[] key) =>
    Convert.ToHexString(SHA256.HashData(key)).Substring(0, 16).ToLowerInvariant();
