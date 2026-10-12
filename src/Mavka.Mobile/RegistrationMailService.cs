using System.Text;
using System.Text.Json;
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
               "МВО: " + (profile.IsMvo ? "+" : "-") + "\n" +
               "Телефон: " + profile.PhoneNumber + "\n";
    }

    public static async Task SendAsync(
        string sender, string appPassword, string recipient, UserProfile profile,
        ExchangeKeyStore? exchangeKeys = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(sender));
        message.To.Add(MailboxAddress.Parse(recipient));
        SetSecureBody(message, "MAVKA_MOBILE_REGISTRATION_V1",
            BuildBody(profile, sender), new
            {
                format = "MAVKA_MOBILE_REGISTRATION_V1",
                clientId = profile.ClientId,
                email = sender,
                fullName = profile.FullName,
                rank = profile.Rank,
                position = profile.Position,
                unit = profile.Unit,
                isMvo = profile.IsMvo,
                phoneNumber = profile.PhoneNumber
            }, exchangeKeys, allowInitialRegistration: true);

        var connection = await GmailTlsConnection.OpenSmtpAsync(timeout.Token);
        using var client = connection.Client;
        await client.AuthenticateAsync(sender, appPassword, timeout.Token);
        await client.SendAsync(message, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }

    public static async Task SendReceiptAsync(
        string sender, string password, string recipient,
        UserProfile profile, IncomingInvoice invoice,
        ExchangeKeyStore? exchangeKeys = null)
    {
        var body = "MAVKA_MOBILE_RECEIPT_V1\n" +
                   "ClientId: " + profile.ClientId + "\n" +
                   "DocumentId: " + invoice.DocumentId + "\n" +
                   "DocumentNumber: " + invoice.Number + "\n" +
                   "Підрозділ: " + profile.Unit + "\n" +
                   "ПІБ: " + profile.FullName + "\n" +
                   "МВО: " + (profile.IsMvo ? "+" : "-") + "\n" +
                   "Status: RECEIVED\n" +
                   "ConfirmedAtUtc: " + DateTimeOffset.UtcNow.ToString("O") + "\n";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(sender));
        message.To.Add(MailboxAddress.Parse(recipient));
        SetSecureBody(message, "MAVKA_MOBILE_RECEIPT_V1 " + invoice.Number,
            body, new
            {
                format = "MAVKA_MOBILE_RECEIPT_V1",
                clientId = profile.ClientId,
                documentId = invoice.DocumentId,
                documentNumber = invoice.Number,
                unit = profile.Unit,
                fullName = profile.FullName,
                isMvo = profile.IsMvo,
                status = "RECEIVED",
                confirmedAtUtc = DateTimeOffset.UtcNow
            }, exchangeKeys);

        var opened = await GmailTlsConnection.OpenSmtpAsync(timeout.Token);
        using var client = opened.Client;
        await client.AuthenticateAsync(sender, password, timeout.Token);
        await client.SendAsync(message, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }

    /// <summary>
    /// Explicit user-initiated encrypted PDF/XLSX transfer. Sends only the
    /// authenticated ciphertext envelope, never plaintext file bytes.
    /// The address is the configured authorized Gmail correspondent.
    /// </summary>
    public static async Task SendFilePartAsync(string sender, string password, string recipient,
        string fileName, string part)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(part) > MavkaFileParts.MaxPartBytes)
            throw new ArgumentException("Частина завелика.");
        var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(sender)); mail.To.Add(MailboxAddress.Parse(recipient));
        mail.Subject = MavkaFileParts.Format;
        var body = new BodyBuilder { TextBody = "Захищена частина файла Мавки." };
        body.Attachments.Add(fileName + MavkaFileParts.Extension, Encoding.UTF8.GetBytes(part), new ContentType("application", "json"));
        mail.Body = body.ToMessageBody();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var opened = await GmailTlsConnection.OpenSmtpAsync(timeout.Token);
        using var client = opened.Client;
        await client.AuthenticateAsync(sender, password, timeout.Token);
        await client.SendAsync(mail, timeout.Token); await client.DisconnectAsync(true, timeout.Token);
    }

    public static async Task SendProtectedFileAsync(
        string sender, string appPassword, string recipient,
        string originalName, string encryptedEnvelope)
    {
        if (!System.Net.Mail.MailAddress.TryCreate(sender, out var from) ||
            !System.Net.Mail.MailAddress.TryCreate(recipient, out var to) ||
            from.Address != sender || to.Address != recipient)
            throw new ArgumentException("Некоректна адреса електронної пошти.");
        _ = MavkaEncryptedFileEnvelope.MimeFor(originalName);
        if (encryptedEnvelope.Length is < 100 or > MavkaEncryptedFileEnvelope.MaxEnvelopeBytes)
            throw new ArgumentException("Зашифрований документ завеликий.");
        using (var parsed = JsonDocument.Parse(encryptedEnvelope))
        {
            if (parsed.RootElement.GetProperty("format").GetString() !=
                MavkaEncryptedFileEnvelope.Format)
                throw new ArgumentException("Непідтримуваний тип пакета.");
        }

        var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(sender));
        mail.To.Add(MailboxAddress.Parse(recipient));
        mail.Subject = "MAVKA_ENCRYPTED_FILE_V1";
        var body = new BodyBuilder
        {
            TextBody = "Захищене вкладення PDF/XLSX. Доступне лише з ключем обміну."
        };
        body.Attachments.Add(originalName + MavkaEncryptedFileEnvelope.Extension,
            Encoding.UTF8.GetBytes(encryptedEnvelope),
            new ContentType("application", "json"));
        mail.Body = body.ToMessageBody();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var connection = await GmailTlsConnection.OpenSmtpAsync(timeout.Token);
        using var client = connection.Client;
        await client.AuthenticateAsync(sender, appPassword, timeout.Token);
        await client.SendAsync(mail, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }

    private static void SetSecureBody(
        MimeMessage message, string legacySubject, string legacyBody,
        object structuredData, ExchangeKeyStore? keys, bool allowInitialRegistration = false)
    {
        if (keys?.HasKeyPair != true)
        {
            if (!allowInitialRegistration)
                throw new KeyNotFoundException("Підтвердження залишено в черзі до отримання ключів.");
            // Only initial registration, before keys have arrived, uses the old protocol.
            message.Subject = legacySubject;
            message.Body = new TextPart("plain") { Text = legacyBody };
            return;
        }

        string encryptedJson = MavkaEncryptedEnvelope.Encrypt(
            JsonSerializer.Serialize(structuredData), keys);
        var builder = new BodyBuilder
        {
            TextBody = "MAVKA_ENCRYPTED_V1. Захищене службове повідомлення Мавки Mobile."
        };
        builder.Attachments.Add("mavka-exchange.json",
            Encoding.UTF8.GetBytes(encryptedJson),
            new ContentType("application", "json"));
        message.Subject = "MAVKA_ENCRYPTED_V1";
        message.Body = builder.ToMessageBody();
    }
}

