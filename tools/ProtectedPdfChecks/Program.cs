using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

const string password = "Demo-Password-2026-AES256";
using var document = new PdfDocument();
document.AddPage();
document.SecuritySettings.UserPassword = password;
document.SecuritySettings.OwnerPassword = "Different-Owner-Password-2026";
document.SecurityHandler.SetEncryptionToV5();
using var stream = new MemoryStream();
document.Save(stream, false);
var encrypted = stream.ToArray();

if (encrypted.Length < 200 || System.Text.Encoding.ASCII.GetString(encrypted, 0, 5) != "%PDF-")
    throw new InvalidOperationException("Password PDF is not a PDF document.");

bool rejected = false;
try
{
    using var incorrect = new MemoryStream(encrypted);
    using var invalid = PdfReader.Open(incorrect, "Wrong-Demo-Password", PdfDocumentOpenMode.Import);
}
catch (Exception) { rejected = true; }
if (!rejected)
    throw new InvalidOperationException("Wrong PDF password unexpectedly accepted.");

using var correct = new MemoryStream(encrypted);
using var reader = PdfReader.Open(correct, password, PdfDocumentOpenMode.Import);
if (reader.PageCount != 1)
    throw new InvalidOperationException("Valid password must open the protected PDF.");

Console.WriteLine("AES-256 encrypted PDF regression: PASS (bad password rejected; good password opens)");
