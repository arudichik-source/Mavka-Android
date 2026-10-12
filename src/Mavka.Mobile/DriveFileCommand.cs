using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Mavka.Mobile;

internal sealed record DriveFileCommand(string Format, string FileId, string DownloadUrl,
    int EncryptedSize, string Sha256, string TransferId)
{
    public const string Kind = "MAVKA_DRIVE_FILE_V1";
    public static bool TryRead(string? authenticatedJson, out DriveFileCommand? command)
    {
        command = null;
        try
        {
            var c = JsonSerializer.Deserialize<DriveFileCommand>(authenticatedJson ?? "", MavkaFileParts.Json);
            if (c is null || c.Format != Kind || !MavkaFileParts.IsId(c.TransferId) ||
                c.FileId.Length is < 10 or > 200 || !c.FileId.All(x => char.IsAsciiLetterOrDigit(x) || x is '_' or '-') ||
                c.EncryptedSize is < 100 or > MavkaFileParts.MaxBundleBytes || c.Sha256.Length != 64 ||
                !c.Sha256.All(x => x is >= '0' and <= '9' or >= 'A' and <= 'F') ||
                !Uri.TryCreate(c.DownloadUrl, UriKind.Absolute, out var url) || !Allowed(url) ||
                !(url.AbsolutePath.Contains(c.FileId, StringComparison.Ordinal) ||
                  url.Query.Split('&').Any(q => q.TrimStart('?') == "id=" + c.FileId))) return false;
            command = c; return true;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NullReferenceException) { return false; }
    }
    private static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && (uri.Host == "drive.google.com" || uri.Host == "drive.usercontent.google.com" ||
            uri.Host.EndsWith(".googleusercontent.com", StringComparison.Ordinal));
    public async Task<string[]> DownloadAsync(CancellationToken cancellation = default, HttpMessageHandler? transport = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var handler = transport ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler);
        // Resume a dropped encrypted download with an HTTP Range request.
        // This buffer contains only the already encrypted Drive bundle; no
        // plaintext document ever reaches external storage.
        var url = new Uri(DownloadUrl);
        using var buffer = new MemoryStream(EncryptedSize);
        byte[] chunk = new byte[64 * 1024];
        int redirects = 0, retries = 0;
        while (buffer.Length < EncryptedSize)
        {
            if (!Allowed(url)) throw new IOException("Недозволена адреса Google Drive.");
            int offset = checked((int)buffer.Length);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (offset > 0)
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
            try
            {
                using var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (++redirects > 5) throw new IOException("Забагато перенаправлень Google Drive.");
                    var next = response.Headers.Location ??
                        throw new IOException("Відсутній URL перенаправлення.");
                    url = next.IsAbsoluteUri ? next : new Uri(url, next);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType?.MediaType == "text/html")
                    throw new IOException("Google Drive потребує доступу або підтвердження завантаження.");
                if (offset > 0 && response.StatusCode == HttpStatusCode.OK)
                {
                    // Some shared-link endpoints ignore Range. Restart from
                    // their full 200 response instead of concatenating overlap.
                    buffer.SetLength(0);
                    offset = 0;
                }
                else if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (range?.From != offset || range.Length != EncryptedSize)
                        throw new IOException("Google Drive повернув неправильний діапазон.");
                }
                else if (offset > 0)
                    throw new IOException("Google Drive не підтримує відновлення діапазону.");

                if (response.Content.Headers.ContentLength is long length &&
                    length > EncryptedSize - offset)
                    throw new IOException("Google Drive повернув завеликий файл.");

                using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                int received;
                while ((received = await input.ReadAsync(chunk, timeout.Token)) > 0)
                {
                    if (received > EncryptedSize - buffer.Length)
                        throw new IOException("Завантаження перевищує очікуваний розмір.");
                    buffer.Write(chunk, 0, received);
                }
                if (buffer.Length != EncryptedSize)
                {
                    if (++retries > 3)
                        throw new IOException("Завантаження Google Drive неповне.");
                    continue;
                }
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or TaskCanceledException &&
                !timeout.IsCancellationRequested && ++retries <= 3)
            {
                // Retry missing ciphertext bytes only. A full SHA-256 and
                // individual AES-GCM tags are verified after reassembly.
                continue;
            }
        }
        var bytes = buffer.ToArray();
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(Sha256)))
                throw new CryptographicException("Контрольна сума файла Google Drive не збігається.");
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.GetProperty("format").GetString() != "MAVKA_FILE_BUNDLE_V1") throw new IOException("Drive file is not an encrypted bundle.");
            var parts = root.GetProperty("parts");
            if (parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() is < 1 or > 125)
                throw new IOException("Invalid Drive bundle parts.");
            return parts.EnumerateArray().Select(p => p.GetRawText()).ToArray();
    }
    internal static async Task<byte[]> BoundedReadAsync(Stream source, int exactBytes, CancellationToken token)
    {
        if (exactBytes is < 1 or > MavkaFileParts.MaxBundleBytes) throw new ArgumentException("Invalid download size.");
        using var memory = new MemoryStream(); byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, exactBytes + 1 - checked((int)memory.Length))), token)) > 0)
        {
            memory.Write(buffer, 0, read);
            if (memory.Length > exactBytes) throw new IOException("Завантажений файл завеликий.");
        }
        if (memory.Length != exactBytes) throw new IOException("Завантаження неповне.");
        return memory.ToArray();
    }
}
