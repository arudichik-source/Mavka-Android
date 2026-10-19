using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Mavka.Mobile;
internal sealed class RelayApiException(int status, string code) : IOException
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public override string Message => Status switch
    {
        403 when Code == "country_not_allowed" => "Cloudflare доступний лише з української IP-адреси.",
        403 => "Доступ Relay не надано або відкликано. Перевір реєстрацію на ПК.",
        401 => "Сервер відхилив підпис. Перевір дату й час телефону та реєстрацію.",
        429 => "Ліміт запитів Relay. Повторна спроба доступна через хвилину.",
        409 => "Сервер виявив конфлікт пакета. Дані залишені локально.",
        _ => "Relay тимчасово недоступний (HTTP " + Status + "). Дані залишені локально."
    };
}
internal sealed class RelayApiClient(string deviceId, Func<byte[], byte[]> sign) : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(35) };
    public async Task<JsonElement> Call(string method, string path, string body, CancellationToken cancellation)
    {
        if (Encoding.UTF8.GetByteCount(body) > 49152) throw new ArgumentException("Пакет перевищує ліміт Relay.");
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nonce = RelayProtocol.B64(RandomNumberGenerator.GetBytes(16));
        var signature = sign(RelayProtocol.Canonical(method, path, deviceId, time, nonce, body));
        if (signature.Length != 64) throw new CryptographicException("Потрібен підпис P1363.");
        using var request = new HttpRequestMessage(new HttpMethod(method), RelayProtocol.Origin + path);
        request.Headers.Add("X-Mavka-Device", deviceId);
        request.Headers.Add("X-Mavka-Time", time);
        request.Headers.Add("X-Mavka-Nonce", nonce);
        request.Headers.Add("X-Mavka-Signature", RelayProtocol.B64(signature));
        if (method == "POST") request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.Content.Headers.ContentLength > 393216) throw new IOException("Відповідь Relay завелика.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var memory = new MemoryStream(); var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellation)) != 0)
        {
            if (memory.Length + read > 393216) throw new IOException("Відповідь Relay завелика.");
            memory.Write(buffer, 0, read);
        }
        using var doc = JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 14 });
        if (!response.IsSuccessStatusCode)
        {
            var code = doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() ?? "" : "";
            throw new RelayApiException((int)response.StatusCode, code);
        }
        return doc.RootElement.Clone();
    }
    public void Dispose() => _http.Dispose();
}
