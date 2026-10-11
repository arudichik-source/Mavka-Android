using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Mavka.Mobile;

internal static class RegistrationMailService
{
    public static string BuildBody(UserProfile profile, string email)
    {
        // Versioned text format: the desktop Mavka will need a matching parser.
        return "MAVKA_MOBILE_REGISTRATION_V1\n" +
               "ClientId: " + profile.ClientId + "\n" +
               "Email: " + email + "\n" +
               "ПІБ: " + profile.FullName + "\n" +
               "Звання: " + profile.Rank + "\n" +
               "Посада: " + profile.Position + "\n" +
               "Підрозділ: " + profile.Unit + "\n" +
               "МВО: " + (profile.IsMvo ? "+" : "-") + "\n";
    }

    public static async Task SendAsync(
        string sender, string appPassword, string recipient, UserProfile profile)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(sender));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = "MAVKA_MOBILE_REGISTRATION_V1";
        message.Body = new TextPart("plain") { Text = BuildBody(profile, sender) };

        var connection = await GmailTlsConnection.OpenSmtpAsync(timeout.Token);
        using var client = connection.Client;
        await client.AuthenticateAsync(sender, appPassword, timeout.Token);
        await client.SendAsync(message, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }
}
