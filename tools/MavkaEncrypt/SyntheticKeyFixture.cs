using System.Security.Cryptography;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// A synthetic, isolated cryptographic fixture generator for preview testing.
/// This is deliberately not a production key provisioner: it has no Gmail
/// credentials, transport, account provisioning or durable raw-key storage.
/// </summary>
internal static class SyntheticKeyFixture
{
    internal const string DemoClientId = "DEMO-PREVIEW-CLIENT-2026";

    public static string Create(string previewClientId, string password)
    {
        if (string.IsNullOrWhiteSpace(previewClientId) ||
            previewClientId.Length is < 10 or > 100 ||
            previewClientId != previewClientId.Trim() ||
            previewClientId.Any(char.IsControl))
            throw new ArgumentException("Preview Client ID must be 10–100 printable characters.");
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 128)
            throw new ArgumentException("Use a dedicated 12–128 character test password.");

        var current = RandomNumberGenerator.GetBytes(32);
        var future = RandomNumberGenerator.GetBytes(32);
        try
        {
            return MavkaKeyPackage.Create(
                new MavkaKeyPair(1, previewClientId, current, future), password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(current);
            CryptographicOperations.ZeroMemory(future);
        }
    }

    public static void SelfTest()
    {
        const string password = "Demo-Package-Only-2026!";
        const string wrongPassword = "Wrong-Demo-Package-2026!";
        var package = Create(DemoClientId, password);
        var other = Create(DemoClientId, password);
        if (package == other || !MavkaKeyPackage.IsPackage(package))
            throw new Exception("Synthetic fixtures should be independently randomized.");

        using var parsed = JsonDocument.Parse(package);
        if (parsed.RootElement.GetProperty("format").GetString() !=
            MavkaKeyPackage.Format)
            throw new Exception("Wrong provisioning envelope format.");
        if (package.Contains(DemoClientId, StringComparison.Ordinal))
            throw new Exception("Preview Client ID leaked outside encrypted payload.");
        if (package.Contains(password, StringComparison.Ordinal))
            throw new Exception("Password leaked into synthetic package.");

        var opened = MavkaKeyPackage.Open(package, password);
        try
        {
            if (opened.Generation != 1 ||
                opened.RecipientClientId != DemoClientId ||
                opened.CurrentKey.Length != 32 || opened.FutureKey.Length != 32 ||
                CryptographicOperations.FixedTimeEquals(opened.CurrentKey, opened.FutureKey))
                throw new Exception("Synthetic fixture round-trip failed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(opened.CurrentKey);
            CryptographicOperations.ZeroMemory(opened.FutureKey);
        }

        bool rejected = false;
        try
        {
            var bad = MavkaKeyPackage.Open(package, wrongPassword);
            CryptographicOperations.ZeroMemory(bad.CurrentKey);
            CryptographicOperations.ZeroMemory(bad.FutureKey);
        }
        catch (CryptographicException) { rejected = true; }
        if (!rejected)
            throw new Exception("Synthetic package opened with wrong password.");

        // A single flipped bit in the authenticated ciphertext must not be
        // accepted. Tests encryption integrity without any email traffic.
        var envelope = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(package)!;
        var ciphertext = Convert.FromBase64String(envelope["ciphertext"].GetString()!);
        try
        {
            ciphertext[0] ^= 1;
            var replacement = JsonSerializer.SerializeToElement(
                Convert.ToBase64String(ciphertext));
            envelope["ciphertext"] = replacement;
            var alteredPackage = JsonSerializer.Serialize(envelope);
            rejected = false;
            try
            {
                var bad = MavkaKeyPackage.Open(alteredPackage, password);
                CryptographicOperations.ZeroMemory(bad.CurrentKey);
                CryptographicOperations.ZeroMemory(bad.FutureKey);
            }
            catch (CryptographicException) { rejected = true; }
            if (!rejected)
                throw new Exception("Corrupted synthetic package was accepted.");
        }
        finally { CryptographicOperations.ZeroMemory(ciphertext); }
        Console.WriteLine(
            "Synthetic key fixture: 2 distinct AES-256 keys, password and tamper rejection PASS.");
    }
}
