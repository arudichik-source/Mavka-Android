
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace Mavka.Mobile;

internal static class GmailConnectionService
{
    public static async Task TestImapAsync(
        string email,
        string appPassword,
        Action<string>? progress = null)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(25));

        using var client = new ImapClient();

        progress?.Invoke("1/5: Підключення до Gmail...");

        await client.ConnectAsync(
            "imap.gmail.com",
            993,
            SecureSocketOptions.SslOnConnect,
            timeout.Token);

        progress?.Invoke("2/5: SSL/TLS підключено");

        progress?.Invoke("3/5: Авторизація Gmail...");

        await client.AuthenticateAsync(
            email,
            appPassword,
            timeout.Token);

        progress?.Invoke("4/5: Відкриття вхідних...");

        await client.Inbox.OpenAsync(
            FolderAccess.ReadOnly,
            timeout.Token);

        await client.DisconnectAsync(
            true,
            timeout.Token);
    }
}
