using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Mavka.Mobile;

/// <summary>
/// Explicitly user-initiated HTTPS download into app-private staging.
/// Supports interrupted downloads via bounded HTTP ranges. No automatic
/// installation, no deletion of previous APK/data, no arbitrary hosts.
/// </summary>
internal static class DriveApkDownloader
{
    public sealed record VerifiedDownload(long Bytes, string Sha256);
    private const int MaximumRetries = 3;

    public static async Task<VerifiedDownload> DownloadAsync(
        string link, string privateDestination,
        IProgress<(long Bytes, long? Total)>? progress = null,
        CancellationToken cancellation = default, HttpMessageHandler? transport = null)
    {
        Uri url = ApkUpdatePolicy.NormalizeDriveLink(link);
        if (!Path.IsPathFullyQualified(privateDestination))
            throw new ArgumentException("Private destination must be absolute.");
        var parent = Path.GetDirectoryName(privateDestination)!;
        Directory.CreateDirectory(parent);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        using var handler = transport ?? new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false
        };
        using var client = new HttpClient(handler);
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mavka-Mobile-Android/1");
        int redirects = 0, retries = 0;
        bool complete = false;
        long? expectedTotal = null;
        try
        {
            using var output = new FileStream(privateDestination, FileMode.Create,
                FileAccess.ReadWrite, FileShare.None, 65536, useAsync: true);
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (!ApkUpdatePolicy.IsTrustedDriveUri(url))
                    throw new IOException("Недозволене перенаправлення завантаження APK.");
                long offset = output.Length;
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (offset > 0)
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                try
                {
                    using var response = await client.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if ((int)response.StatusCode is >= 300 and < 400)
                    {
                        if (++redirects > 5)
                            throw new IOException("Забагато перенаправлень Google Drive.");
                        var target = response.Headers.Location ??
                            throw new IOException("Google Drive не повернув адресу файла.");
                        url = target.IsAbsoluteUri ? target : new Uri(url, target);
                        if (!ApkUpdatePolicy.IsTrustedDriveUri(url))
                            throw new IOException("Google Drive перенаправив на сторонній сайт.");
                        continue;
                    }
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("Google Drive відповів HTTP " +
                            (int)response.StatusCode);
                    if (response.Content.Headers.ContentType?.MediaType is
                        "text/html" or "text/plain" or "application/json")
                        throw new IOException(
                            "Google Drive повернув сторінку замість APK. " +
                            "Відкрий доступ до файла за посиланням.");

                    if (offset != 0 && response.StatusCode == HttpStatusCode.OK)
                    {
                        // A Drive endpoint may ignore Range. Never append
                        // duplicated bytes; restart safely from byte zero.
                        output.SetLength(0);
                        offset = 0;
                        expectedTotal = null;
                    }
                    else if (offset > 0)
                    {
                        if (response.StatusCode != HttpStatusCode.PartialContent ||
                            response.Content.Headers.ContentRange?.From != offset)
                            throw new IOException("Google Drive повернув некоректний діапазон.");
                        var total = response.Content.Headers.ContentRange.Length;
                        if (total.HasValue && expectedTotal.HasValue &&
                            total.Value != expectedTotal.Value)
                            throw new IOException("Файл Google Drive змінився під час завантаження.");
                        if (total.HasValue) expectedTotal = total;
                    }
                    if (response.Content.Headers.ContentLength is long size)
                    {
                        if (offset + size > ApkUpdatePolicy.MaxApkBytes)
                            throw new IOException("APK перевищує допустимий розмір 220 МБ.");
                        if (!expectedTotal.HasValue) expectedTotal = offset + size;
                    }
                    if (expectedTotal is > ApkUpdatePolicy.MaxApkBytes)
                        throw new IOException("APK завеликий.");
                    output.Position = offset;
                    using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                    var chunk = new byte[65536];
                    int count;
                    while ((count = await input.ReadAsync(chunk, timeout.Token)) > 0)
                    {
                        if (output.Position + count > ApkUpdatePolicy.MaxApkBytes)
                            throw new IOException("APK перевищує ліміт 220 МБ.");
                        await output.WriteAsync(chunk.AsMemory(0, count), timeout.Token);
                        progress?.Report((output.Position, expectedTotal));
                    }
                    await output.FlushAsync(timeout.Token);
                    if (expectedTotal.HasValue && output.Length != expectedTotal.Value)
                    {
                        if (++retries > MaximumRetries)
                            throw new IOException("APK завантажено не повністю.");
                        continue;
                    }
                    if (output.Length < 4096)
                        throw new IOException("APK надто малий або Google Drive повернув HTML.");
                    output.Position = 0;
                    byte[] magic = new byte[4];
                    if (await output.ReadAsync(magic, timeout.Token) != 4 ||
                        !magic.AsSpan().SequenceEqual(new byte[] {0x50,0x4b,0x03,0x04}))
                        throw new IOException("Посилання не веде на файл APK.");
                    complete = true;
                    break;
                }
                catch (Exception ex) when (
                    (ex is HttpRequestException or IOException) &&
                    !timeout.IsCancellationRequested && ++retries <= MaximumRetries)
                {
                    // A transient interrupted transfer can resume from its
                    // last committed byte. Persistent errors still fail.
                    continue;
                }
            }
        }
        finally
        {
            if (!complete)
                try { File.Delete(privateDestination); } catch { }
        }
        // Verify Android ZIP structure and calculate full-file SHA-256.
        try
        {
            using (var zip = ZipFile.OpenRead(privateDestination))
            {
                if (zip.GetEntry("AndroidManifest.xml") is null ||
                    !zip.Entries.Any(e => e.FullName == "classes.dex"))
                    throw new IOException("Завантажений файл не є повним Android APK.");
            }
            using var source = File.OpenRead(privateDestination);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellation));
            return new VerifiedDownload(source.Length, hash);
        }
        catch
        {
            try { File.Delete(privateDestination); } catch { }
            throw;
        }
    }
}
