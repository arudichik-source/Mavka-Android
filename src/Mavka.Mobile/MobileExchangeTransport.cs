using Android.Content;
using System.Text.Json;

namespace Mavka.Mobile;
internal static class MobileExchangeTransport
{
    public static bool RelayEnabled(Context context) => new RelayIdentity(context).Enabled;
    public static async Task SendReceiptAsync(Context context, string sender, string password,
        string recipient, UserProfile profile, IncomingInvoice invoice, ExchangeKeyStore keys)
    {
        if (!string.IsNullOrWhiteSpace(sender) && !string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(recipient))
        {
            try { await RegistrationMailService.SendReceiptAsync(sender, password, recipient, profile, invoice, keys); return; }
            catch (Exception) when (RelayEnabled(context)) { /* Fall back to durable Relay. */ }
        }
        var relay = new RelaySync(context);
        var group = relay.Queue("receipt:" + invoice.DocumentId, RelaySync.Receipt(profile, invoice));
        await relay.Synchronize();
        if (!relay.Submitted(group)) throw new IOException("Підтвердження збережене в черзі Relay; буде надіслано повторно.");
    }
    public static async Task SendFilePartAsync(Context context, string sender, string password,
        string recipient, string fileName, string packet)
    {
        if (!string.IsNullOrWhiteSpace(sender) && !string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(recipient))
        {
            try { await RegistrationMailService.SendFilePartAsync(sender, password, recipient, fileName, packet); return; }
            catch (Exception) when (RelayEnabled(context)) { }
        }
        var documentPassword = new DocumentPasswordStore(context).Load() ?? throw new InvalidOperationException("Потрібен пароль документів.");
        var inner = MavkaPasswordedEnvelope.Unwrap(packet, documentPassword);
        using var doc = JsonDocument.Parse(inner);
        var metadata = doc.RootElement.GetProperty("metadata");
        var logical = "filepart:" + metadata.GetProperty("transferId").GetString() + ":" + metadata.GetProperty("index").GetInt32();
        var relay = new RelaySync(context); var group = relay.Queue(logical, packet);
        await relay.Synchronize();
        if (!relay.Submitted(group)) throw new IOException("Частина файла залишається в надійній черзі Relay.");
    }
    public static async Task SendProtectedFileAsync(Context context, string sender, string password,
        string recipient, string fileName, string packet)
    {
        if (!MavkaPasswordedEnvelope.IsWrapped(packet)) throw new ArgumentException("Потрібен захищений файл.");
        if (!string.IsNullOrWhiteSpace(sender) && !string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(recipient))
        {
            try { await RegistrationMailService.SendProtectedFileAsync(sender, password, recipient, fileName, packet); return; }
            catch (Exception) when (RelayEnabled(context)) { }
        }
        var relay = new RelaySync(context);
        var group = relay.Queue("file:" + RelayProtocol.Hash(packet), packet);
        await relay.Synchronize();
        if (!relay.Submitted(group)) throw new IOException("Файл збережений у черзі Relay; синхронізацію буде продовжено.");
    }
}
