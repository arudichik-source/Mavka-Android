using Mavka.Mobile;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var url = ApkUpdatePolicy.NormalizeDriveLink(
    "https://drive.google.com/file/d/AbcdEf0123456789_-/view?usp=sharing");
Check(url.Host == "drive.google.com" &&
    url.Query.Contains("id=AbcdEf0123456789_-"), "Shared Drive link normalized.");
Check(ApkUpdatePolicy.NormalizeDriveLink(
    "https://drive.google.com/open?id=AbcdEf0123456789").Query.Contains(
        "AbcdEf0123456789"), "Open?id= accepted");
foreach (var malicious in new[]{
    "http://drive.google.com/file/d/AbcdEf0123456789/view",
    "https://drive.google.com.evil.net/file/d/AbcdEf0123456789/view",
    "https://evil.net/evil.apk", "file:///sdcard/update.apk",
    "https://drive.google.com:8443/file/d/AbcdEf0123456789/view"})
{
    bool rejected = false;
    try { ApkUpdatePolicy.NormalizeDriveLink(malicious); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "Host/TLS fail-closed: " + malicious);
}
byte[] identity = SHA256.HashData("correct installed release certificate"u8);
byte[] rogue = SHA256.HashData("rogue release certificate"u8);
ApkUpdatePolicy.CheckApkMetadata(ApkUpdatePolicy.ProductionPackage,
    101, 102, identity, identity);
foreach (var candidate in new[]{
    (Package: "ua.mavka.mobile.qa31", Old: 101L, Next: 102L, Cert: identity),
    (Package: "ua.mavka.mobile", Old: 101L, Next: 101L, Cert: identity),
    (Package: "ua.mavka.mobile", Old: 101L, Next: 102L, Cert: rogue)
})
{
    bool blocked = false;
    try { ApkUpdatePolicy.CheckApkMetadata(
        candidate.Package, candidate.Old, candidate.Next, identity, candidate.Cert); }
    catch (InvalidOperationException) { blocked = true; }
    Check(blocked, "Mismatched package, signing key or downgraded version rejected.");
}

using var data = new MemoryStream();
using (var zip = new ZipArchive(data, ZipArchiveMode.Create, leaveOpen: true))
{
    foreach (var part in new[]{ "AndroidManifest.xml", "classes.dex", "assets/dummy.bin" })
    {
        using var file = zip.CreateEntry(part, CompressionLevel.NoCompression).Open();
        byte[] bytes = RandomNumberGenerator.GetBytes(9000);
        file.Write(bytes);
    }
}
byte[] expected = data.ToArray();
var parent = Path.Combine(Path.GetTempPath(), "mavka-updater-" +
    Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(parent);
var destination = Path.Combine(parent, "download.apk");
try
{
    var simulated = new PartialDriveApkReply(expected);
    var transfer = await DriveApkDownloader.DownloadAsync(
        "https://drive.google.com/file/d/AbcdEf0123456789/view", destination,
        transport: simulated);
    Check(simulated.Requests == 2, "Interrupted download must resume only missing range");
    Check(transfer.Bytes == expected.Length &&
        transfer.Sha256 == Convert.ToHexString(SHA256.HashData(expected)),
        "SHA-256 and entire APK bytes preserved");
    Check(File.ReadAllBytes(destination).SequenceEqual(expected),
        "APK preserved byte-for-byte after resumed transfer");
    Check(!ApkUpdatePolicy.IsTrustedDriveUri(
        new Uri("https://drive.google.com.evil.net/redirect")),
        "Redirects may not leave Google Drive allowlist");
    Console.WriteLine("Google Drive APK update: 11 policy classes + resumable ZIP roundtrip PASS");
}
finally
{
    if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
}

internal sealed class PartialDriveApkReply(byte[] full) : HttpMessageHandler
{
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        int cut = full.Length / 2;
        if (Requests == 1)
        {
            if (request.Headers.Range is not null)
                throw new Exception("Initial request unexpectedly used Range");
            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(full[..cut])
            };
            // Advertise the correct full size but terminate the first
            // response halfway through, simulating an interrupted connection.
            message.Content.Headers.ContentLength = full.Length;
            return Task.FromResult(message);
        }
        if (Requests != 2 ||
            request.Headers.Range?.Ranges.Single().From != cut)
            throw new Exception("Download did not resume at the correct byte offset");
        var resumed = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(full[cut..])
        };
        resumed.Content.Headers.ContentRange =
            new ContentRangeHeaderValue(cut, full.Length - 1, full.Length);
        return Task.FromResult(resumed);
    }
}
