using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mavka.Mobile;

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
else if (args.Length == 6 && args[0] == "package")
{
    // package current.key future.key CLIENT_ID GENERATION output.json
    var current = Convert.FromBase64String(File.ReadAllText(args[1]).Trim());
    var future = Convert.FromBase64String(File.ReadAllText(args[2]).Trim());
    try
    {
        if (!int.TryParse(args[4], out var generation) || generation < 1)
            throw new ArgumentException("Invalid generation.");
        if (File.Exists(args[5]))
            throw new InvalidOperationException("Output file already exists.");
        string password = ReadDocumentPassword();
        var json = MavkaKeyPackage.Create(
            new MavkaKeyPair(generation, args[3], current, future), password);
        File.WriteAllText(args[5], json);
        Console.WriteLine("Password-protected pair ready. Current ID: " +
            MavkaKeyPackage.Id(current) + ", future ID: " + MavkaKeyPackage.Id(future));
    }
    finally
    {
        CryptographicOperations.ZeroMemory(current);
        CryptographicOperations.ZeroMemory(future);
    }
}
else if (args.Length == 1 && args[0] == "selftest")
{
    var a = RandomNumberGenerator.GetBytes(32);
    var b = RandomNumberGenerator.GetBytes(32);
    try
    {
        var original = new MavkaKeyPair(1, "DEMO-CLIENT-123456", a, b);
        var blob = MavkaKeyPackage.Create(original, "090699");
        var restored = MavkaKeyPackage.Open(blob, "090699");
        try
        {
            if (restored.Generation != 1 ||
                restored.RecipientClientId != original.RecipientClientId ||
                !restored.CurrentKey.SequenceEqual(a) ||
                !restored.FutureKey.SequenceEqual(b))
                throw new Exception("Key package round-trip failed");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(restored.CurrentKey);
            CryptographicOperations.ZeroMemory(restored.FutureKey);
        }
        bool rejected = false;
        try { MavkaKeyPackage.Open(blob, "WRONG-PASSWORD"); }
        catch (CryptographicException) { rejected = true; }
        if (!rejected)
            throw new Exception("Wrong password was not rejected.");
        Console.WriteLine("Passworded key-pair package round-trip + wrong-password rejection: PASS");
    }
    finally
    {
        CryptographicOperations.ZeroMemory(a);
        CryptographicOperations.ZeroMemory(b);
    }
}
else
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/MavkaEncrypt -- create-key PRIVATE.mavka-key");
    Console.WriteLine("  dotnet run --project tools/MavkaEncrypt -- encrypt PRIVATE.mavka-key input.json encrypted.json");
}

static string KeyId(byte[] key) =>
    Convert.ToHexString(SHA256.HashData(key)).Substring(0, 16).ToLowerInvariant();


static string ReadDocumentPassword()
{
    // Use hidden console input. For unattended local tests only, an environment
    // variable can supply the password; never print or commit that value.
    var fromEnv = Environment.GetEnvironmentVariable("MAVKA_DOCUMENT_PASSWORD");
    if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;
    if (Console.IsInputRedirected)
        throw new InvalidOperationException("Interactive console required for password entry.");
    Console.Write("Document-file password: ");
    var input = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (input.Length > 0) input.Length--;
            continue;
        }
        if (!char.IsControl(key.KeyChar) && input.Length < 128)
            input.Append(key.KeyChar);
    }
    Console.WriteLine();
    return input.ToString();
}
