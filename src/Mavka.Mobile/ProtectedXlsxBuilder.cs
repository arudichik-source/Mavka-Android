using System.Security.Cryptography;
using Android.Content;
using ExcelEncryptor;

namespace Mavka.Mobile;

/// <summary>
/// Password-protects an OOXML workbook using Excel-compatible Office Agile
/// Encryption. The unprotected ZIP exists only in process memory. Any
/// intermediate file in the application cache is already encrypted.
/// </summary>
internal static class ProtectedXlsxBuilder
{
    private static readonly byte[] OleHeader =
    [
        0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1
    ];

    public static byte[] CreateInvoice(Context context, IncomingInvoice invoice,
        string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 128)
            throw new ArgumentException("Пароль Excel: від 12 до 128 символів.");
        if (invoice.Positions.Count is < 1 or > 200)
            throw new ArgumentException("Неприпустима кількість позицій.");

        var privateCache = context.CacheDir?.AbsolutePath ??
            throw new InvalidOperationException("Приватна папка Android недоступна.");
        var encryptedPath = Path.Combine(privateCache,
            "mavka-encrypted-workbook-" + Guid.NewGuid().ToString("N") + ".xlsx");
        byte[]? plaintext = null;
        byte[]? roundTrip = null;

        try
        {
            // The existing OOXML builder emits a genuine numeric/typed .xlsx.
            plaintext = XlsxInvoiceBuilder.Create(invoice);

            // ExcelEncryptor supports full Office Agile Encryption. The only
            // written intermediate is an encrypted OLE compound file in the
            // private application cache, never an unencrypted .xlsx on disk.
            Encrypt.FromBytesToFile(plaintext, encryptedPath, password);

            var encrypted = File.ReadAllBytes(encryptedPath);
            if (encrypted.Length < 100 ||
                !encrypted.AsSpan(0, OleHeader.Length).SequenceEqual(OleHeader) ||
                !encrypted.AsSpan(0, OleHeader.Length).SequenceEqual(OleHeader))
                throw new InvalidOperationException(
                    "Захищений Excel не має очікуваного формату Microsoft Office.");

            // Verify that the encrypted output genuinely opens with the user
            // password before publishing it to shared Downloads.
            roundTrip = Encrypt.Decrypt(encryptedPath, password);
            if (roundTrip.Length != plaintext.Length ||
                !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(roundTrip), SHA256.HashData(plaintext)))
                throw new InvalidOperationException(
                    "Парольний Excel не пройшов контроль відкриття.");

            return encrypted;
        }
        finally
        {
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
            if (roundTrip is not null)
                CryptographicOperations.ZeroMemory(roundTrip);
            try
            {
                if (File.Exists(encryptedPath)) File.Delete(encryptedPath);
            }
            catch
            {
                // Only encrypted bytes can remain in private app cache.
                // The OS may complete deletion during routine cache cleanup.
            }
        }
    }
}
