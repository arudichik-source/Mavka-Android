using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace Mavka.Mobile;

internal static class GmailConnectionService
{
    public static async Task TestImapAsync(string email, string appPassword)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = new ImapClient();
        await client.ConnectAsync("imap.gmail.com", 993, SecureSocketOptions.SslOnConnect, timeout.Token);
        await client.AuthenticateAsync(email, appPassword, timeout.Token);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }
}
