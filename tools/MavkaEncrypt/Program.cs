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
else if (args.Length == 4 && args[0] == "file-parts")
{
    var key = Convert.FromBase64String(File.ReadAllText(args[1]).Trim());
    if (new FileInfo(args[2]).Length > MavkaFileParts.MaxFileBytes) throw new ArgumentException("File exceeds 32 MB.");
    if (Directory.Exists(args[3])) throw new IOException("Output directory already exists.");
    byte[] plain = File.ReadAllBytes(args[2]);
    try
    {
        var h = MavkaFileParts.Describe(Path.GetFileName(args[2]), plain);
        Directory.CreateDirectory(args[3]);
        var packets = new List<string>();
        for (int i = 0; i < h.Count; i++)
        {
            var packet = MavkaFileParts.Encrypt(h with { Index = i }, plain.AsSpan(i * MavkaFileParts.ChunkBytes,
                Math.Min(MavkaFileParts.ChunkBytes, plain.Length - i * MavkaFileParts.ChunkBytes)), key);
            File.WriteAllText(Path.Combine(args[3], $"{h.TransferId}-{i+1}-{h.Count}.mavkapart"), packet, new UTF8Encoding(false));
            packets.Add(packet);
        }
        File.WriteAllText(Path.Combine(args[3], "encrypted-drive-bundle.json"),
            "{\"format\":\"MAVKA_FILE_BUNDLE_V1\",\"parts\":[" + string.Join(",",packets) + "]}", new UTF8Encoding(false));
        Console.WriteLine($"Encrypted parts: {h.Count}; transfer ID: {h.TransferId}. Send parts with subject MAVKA_FILE_PART_V1.");
    }
    finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
}
else if (args.Length == 6 && args[0] == "drive-command")
{
    if (File.Exists(args[5])) throw new IOException("Command output already exists.");
    var key = Convert.FromBase64String(File.ReadAllText(args[1]).Trim());
    try
    {
        if (new FileInfo(args[2]).Length > MavkaFileParts.MaxBundleBytes) throw new ArgumentException("Bundle too large.");
        var bundle = File.ReadAllBytes(args[2]); using var doc = JsonDocument.Parse(bundle);
        if (doc.RootElement.GetProperty("format").GetString() != "MAVKA_FILE_BUNDLE_V1") throw new ArgumentException("Encrypted bundle expected.");
        var id = doc.RootElement.GetProperty("parts")[0].GetProperty("metadata").GetProperty("transferId").GetString()!;
        var command = new DriveFileCommand(DriveFileCommand.Kind,args[3],args[4],bundle.Length,Convert.ToHexString(SHA256.HashData(bundle)),id);
        string json = JsonSerializer.Serialize(command,MavkaFileParts.Json);
        if (!DriveFileCommand.TryRead(json,out _)) throw new ArgumentException("Invalid Drive download link or metadata.");
        byte[] plain = Encoding.UTF8.GetBytes(json), nonce=RandomNumberGenerator.GetBytes(12), cipher=new byte[plain.Length], tag=new byte[16];
        try
        {
            string kid=KeyId(key); using var aes = new AesGcm(key,16);
            aes.Encrypt(nonce,plain,cipher,tag,Encoding.UTF8.GetBytes("MAVKA_ENCRYPTED_V1:"+kid));
            File.WriteAllText(args[5],JsonSerializer.Serialize(new { format="MAVKA_ENCRYPTED_V1",algorithm="AES-256-GCM",keyId=kid,
                nonce=Convert.ToBase64String(nonce),ciphertext=Convert.ToBase64String(cipher),tag=Convert.ToBase64String(tag) }),new UTF8Encoding(false));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        Console.WriteLine("Encrypted Drive command ready. No plaintext file was uploaded or written.");
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
else if (args.Length == 2 && args[0] == "demo-package")
{
    // The preview client ID is entered locally, never uploaded to CI or chat.
    // This tool does not transmit email or provision a production identity.
    if (Console.IsInputRedirected)
        throw new InvalidOperationException(
            "Local interactive console required. Demo packages cannot run unattended.");
    if (File.Exists(args[1]))
        throw new InvalidOperationException(
            "Output file exists. Refusing to replace an earlier test package.");
    Console.WriteLine("ISOLATED PREVIEW ONLY — use fabricated profile data, never production.");
    Console.Write("Preview Client ID (from the test application): ");
    var previewClientId = Console.ReadLine()?.Trim() ?? "";
    var password = ReadDocumentPassword();
    var json = SyntheticKeyFixture.Create(previewClientId, password);
    // CreateNew prevents accidental replacement even if the file appeared
    // during prompt entry. Raw synthetic keys never touch the filesystem.
    using (var file = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write,
               FileShare.None))
    using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
        writer.Write(json);
    Console.WriteLine("Synthetic encrypted test package created (generation 1).");
    Console.WriteLine("No unencrypted keys were written. Do not use in production.");
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
        SyntheticKeyFixture.SelfTest();
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
    Console.WriteLine("  dotnet run --project tools/MavkaEncrypt -- demo-package output.json");
    Console.WriteLine("  dotnet run --project tools/MavkaEncrypt -- selftest");
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

