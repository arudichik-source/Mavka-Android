using ExcelEncryptor;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

const string password = "Standalone-Demo-Pass-2026";
var encryptedPath = Path.Combine(Path.GetTempPath(),
    "mavka-encryption-test-" + Guid.NewGuid().ToString("N") + ".xlsx");
byte[] plain;
using (var memory = new MemoryStream())
{
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
    {
        using var types = new StreamWriter(
            zip.CreateEntry("[Content_Types].xml").Open(), Encoding.UTF8);
        types.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
        types.Flush();
        types.Dispose(); // ZipArchive requires the previous entry stream closed.
        using var workbook = new StreamWriter(
            zip.CreateEntry("xl/workbook.xml").Open(), Encoding.UTF8);
        workbook.Write("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />");
        workbook.Flush();
    }
    plain = memory.ToArray();
}
try
{
    Encrypt.FromBytesToFile(plain, encryptedPath, password);
    var cipher = File.ReadAllBytes(encryptedPath);
    ReadOnlySpan<byte> ole = [0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1];
    if (cipher.Length < 512 || !cipher.AsSpan(0,8).SequenceEqual(ole))
        throw new Exception("Missing native Office encrypted container.");
    bool rejected = false;
    try { var ignored = Encrypt.Decrypt(encryptedPath, "Wrong-Demo-Password"); }
    catch { rejected = true; }
    if (!rejected) throw new Exception("Wrong Excel password was accepted.");
    var restored = Encrypt.Decrypt(encryptedPath, password);
    try
    {
        if (!CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(restored), SHA256.HashData(plain)))
            throw new Exception("Decrypted OOXML content mismatch.");
    }
    finally { CryptographicOperations.ZeroMemory(restored); }
    CryptographicOperations.ZeroMemory(cipher);
    Console.WriteLine("Excel Agile encryption: PASS (wrong password denied; full byte roundtrip).");
}
finally
{
    CryptographicOperations.ZeroMemory(plain);
    if (File.Exists(encryptedPath)) File.Delete(encryptedPath);
}
