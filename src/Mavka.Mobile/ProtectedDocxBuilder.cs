using System.Security.Cryptography;
using Android.Content;
using ExcelEncryptor;

namespace Mavka.Mobile;

/// <summary>
/// Produces Microsoft Office Agile-encrypted DOCX (an OLE compound file).
/// The unencrypted OOXML bytes are created only in volatile memory.
/// All exported bytes are checked by a password-authenticated round trip.
/// </summary>
internal static class ProtectedDocxBuilder
{
    private static readonly byte[] OleHeader =
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static byte[] CreateInvoice(Context context, IncomingInvoice invoice, string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 128)
            throw new ArgumentException("Пароль Word: від 12 до 128 символів.");
        var folder = context.CacheDir?.AbsolutePath ??
            throw new InvalidOperationException("Приватна папка Android недоступна.");
        var encryptedPath = Path.Combine(folder,
            "mavka-encrypted-word-" + Guid.NewGuid().ToString("N") + ".docx");
        byte[]? plain = null, recovered = null;
        try
        {
            plain = DocxInvoiceBuilder.Create(invoice);
            // Office Agile Encryption supports OOXML packages, including DOCX.
            Encrypt.FromBytesToFile(plain, encryptedPath, password);
            var encrypted = File.ReadAllBytes(encryptedPath);
            if (encrypted.Length < 512 || !encrypted.AsSpan(0, 8).SequenceEqual(OleHeader))
                throw new InvalidOperationException(
                    "Word не має очікуваного парольно-зашифрованого Office-контейнера.");
            recovered = Encrypt.Decrypt(encryptedPath, password);
            if (recovered.Length != plain.Length ||
                !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(plain), SHA256.HashData(recovered)))
                throw new InvalidOperationException("Парольний Word не пройшов перевірку відкриття.");
            return encrypted;
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (recovered is not null) CryptographicOperations.ZeroMemory(recovered);
            try { if (File.Exists(encryptedPath)) File.Delete(encryptedPath); }
            catch { /* Only already encrypted bytes could remain in private cache. */ }
        }
    }
}
