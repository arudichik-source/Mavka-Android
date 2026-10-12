using Mavka.Mobile;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

int completed = 0;
void Check(bool ok, string what)
{
    if (!ok) throw new InvalidOperationException(what);
    completed++;
}
void Reject<T>(Action run, string why) where T : Exception
{
    try { run(); }
    catch (T) { completed++; return; }
    throw new Exception("Expected rejection: " + why);
}
var current = RandomNumberGenerator.GetBytes(32);
var future = RandomNumberGenerator.GetBytes(32);
try
{
    var minimalPdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% SYNTHETIC SAFE TEST\n%%EOF");
    var minimalXlsx = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0, 1, 2, 3, 4 };
    var sealedPdf = MavkaEncryptedFileEnvelope.Encrypt("test.pdf", minimalPdf, current);
    var sealedXlsx = MavkaEncryptedFileEnvelope.Encrypt("table.xlsx", minimalXlsx, future);
    var currentId = Convert.ToHexString(SHA256.HashData(current))[..16].ToLowerInvariant();
    var futureId = Convert.ToHexString(SHA256.HashData(future))[..16].ToLowerInvariant();
    byte[]? Resolver(string id) => id == currentId ? (byte[])current.Clone() :
        id == futureId ? (byte[])future.Clone() : null;

    {
        var pdf = MavkaEncryptedFileEnvelope.Decrypt(sealedPdf, Resolver);
        Check(pdf.Content.SequenceEqual(minimalPdf), "PDF round trip");
        Check(pdf.FileName == "test.pdf", "Authenticated PDF filename");
    }
    {
        var xlsx = MavkaEncryptedFileEnvelope.Decrypt(sealedXlsx, Resolver);
        Check(xlsx.Content.SequenceEqual(minimalXlsx), "XLSX round trip with future key");
        Check(xlsx.MimeType.EndsWith("sheet"), "XLSX MIME");
    }
    Reject<CryptographicException>(() => MavkaEncryptedFileEnvelope.Decrypt(
        sealedPdf, id => (byte[])future.Clone()), "Wrong key");
    string Rewrite(string encrypted, string field, string newValue)
    {
        var tree = JsonNode.Parse(encrypted)!.AsObject();
        tree[field] = newValue;
        return tree.ToJsonString();
    }
    Reject<CryptographicException>(() => MavkaEncryptedFileEnvelope.Decrypt(
        Rewrite(sealedPdf, "fileName", "other.pdf"), Resolver), "Tampered filename AAD");
    Reject<CryptographicException>(() => MavkaEncryptedFileEnvelope.Decrypt(
        Rewrite(sealedPdf, "sha256", new string('0', 64)), Resolver), "Tampered digest AAD");
    var tampered = JsonNode.Parse(sealedPdf)!.AsObject();
    var ciphertext = Convert.FromBase64String(tampered["ciphertext"]!.GetValue<string>());
    ciphertext[0] ^= 0x40;
    tampered["ciphertext"] = Convert.ToBase64String(ciphertext);
    Reject<CryptographicException>(() => MavkaEncryptedFileEnvelope.Decrypt(
        tampered.ToJsonString(), Resolver), "Tampered ciphertext");
    Reject<ArgumentException>(() => MavkaEncryptedFileEnvelope.Encrypt(
        "../unsafe.pdf", minimalPdf, current), "Path traversal");
    Reject<ArgumentException>(() => MavkaEncryptedFileEnvelope.Encrypt(
        "unsafe.exe", minimalPdf, current), "Unsupported extension");
    Reject<ArgumentException>(() => MavkaEncryptedFileEnvelope.Encrypt(
        "fake.pdf", minimalXlsx, current), "Mismatched content signature");
    Reject<ArgumentException>(() => MavkaEncryptedFileEnvelope.Encrypt(
        "too-large.pdf", new byte[MavkaEncryptedFileEnvelope.MaxFileBytes+1], current),
        "Excessive attachment size");
    Check(sealedPdf.Contains("MAVKA_ENCRYPTED_FILE_V1"), "Distinct file format");
    if (completed != 13) throw new Exception("Missing file transport regression assertions.");
    Console.WriteLine($"Generic encrypted PDF/XLSX transport tests: {completed}/13 PASS");
}
finally
{
    CryptographicOperations.ZeroMemory(current);
    CryptographicOperations.ZeroMemory(future);
}
