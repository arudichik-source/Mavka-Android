#if MAVKA_BOOTSTRAP_QA
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace Mavka.Mobile;
public sealed partial class StorageQaActivity
{
    private string RunProtectedFileChecks()
    {
        var path=Path.Combine(CacheDir!.AbsolutePath,"synthetic-secure-"+Guid.NewGuid().ToString("N")+".xlsx");
        try
        {
            if(new UserProfileStore(this).Load()?.FullName!="SYNTHETIC-QA-USER" || new GmailCredentialsStore(this).HasCredentials || new RelayIdentity(this).Enabled || new ExchangeKeyStore(this).HasKeyPair)throw new InvalidOperationException("Synthetic offline QA only.");
            if(!(Android.OS.Build.Model?.Contains("sdk",StringComparison.OrdinalIgnoreCase)==true || Android.OS.Build.Fingerprint?.Contains("generic",StringComparison.OrdinalIgnoreCase)==true))throw new InvalidOperationException("Emulator only.");
            var checks=0;void Check(bool ok,string text){if(!ok)throw new InvalidDataException(text);checks++;}
            const string password="SYNTHETIC-QA-FILE-PASSWORD";
            byte[] plain;using(var memory=new MemoryStream()){using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)){using(var writer=new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open(),Encoding.UTF8))writer.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");using(var writer=new StreamWriter(zip.CreateEntry("xl/workbook.xml").Open(),Encoding.UTF8))writer.Write("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />");}plain=memory.ToArray();}
            ExcelEncryptor.Encrypt.FromBytesToFile(plain,path,password);var restored=ExcelEncryptor.Encrypt.Decrypt(path,password);
            Check(SHA256.HashData(plain).SequenceEqual(SHA256.HashData(restored)),"XLSX decrypted bytes differ");CryptographicOperations.ZeroMemory(restored);CryptographicOperations.ZeroMemory(plain);
            var denied=false;try{ExcelEncryptor.Encrypt.Decrypt(path,"wrong");}catch(Exception){denied=true;}Check(denied,"XLSX accepts incorrect password");
            using var pdf=new PdfDocument();pdf.AddPage();pdf.SecuritySettings.UserPassword=password;pdf.SecuritySettings.OwnerPassword="SYNTHETIC-OWNER-ONLY";pdf.SecurityHandler.SetEncryptionToV5();using var output=new MemoryStream();pdf.Save(output,false);var encrypted=output.ToArray();
            using(var input=new MemoryStream(encrypted))using(var opened=PdfReader.Open(input,password,PdfDocumentOpenMode.Import))Check(opened.PageCount==1,"PDF valid password cannot open");
            denied=false;try{using var input=new MemoryStream(encrypted);using var opened=PdfReader.Open(input,"wrong",PdfDocumentOpenMode.Import);}catch(Exception){denied=true;}Check(denied,"PDF accepts incorrect password");
            return $"NATIVE PROTECTED FILE QA PASS {checks}: encrypted XLSX byte roundtrip, encrypted PDF, wrong-password rejection. NO NETWORK.";
        }
        catch(Exception error){return "NATIVE PROTECTED FILE QA FAIL: "+error;}
        finally{if(File.Exists(path))File.Delete(path);}
    }
}
#endif
