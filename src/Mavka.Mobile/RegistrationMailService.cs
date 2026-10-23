using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Mavka.Mobile;

internal static partial class RegistrationMailService
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
        ExchangeKeyStore? exchangeKeys = null,Action<RegistrationMailStage>? progress=null)
    {
        var stage=RegistrationMailStage.Message;
        try
        {
        ValidateRegistrationAddresses(sender,recipient);progress?.Invoke(stage);
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

        stage=RegistrationMailStage.Connect;progress?.Invoke(stage);
        var connection = await GmailTlsConnection.OpenSmtpAsync(timeout.Token);
        using var client = connection.Client;
        stage=RegistrationMailStage.Authenticate;progress?.Invoke(stage);
        await client.AuthenticateAsync(sender, appPassword, timeout.Token);
        stage=RegistrationMailStage.Submit;progress?.Invoke(stage);
        await client.SendAsync(message, timeout.Token);
        // SMTP accepted the message. QUIT/cleanup failure must not make the user
        // resend an already accepted registration.
        try{using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(3));await client.DisconnectAsync(true,cleanup.Token);}
        catch(Exception error)when(error is IOException or OperationCanceledException or MailKit.ProtocolException or MailKit.ServiceNotConnectedException or SmtpCommandException){}
        }
        catch(Exception error)when(error is not RegistrationMailFailure){throw new RegistrationMailFailure(stage,error);}
    }
    public static async Task SendEnrollmentAsync(string sender,string appPassword,string recipient,string wire,string requestId)
    {
        var stage=RegistrationMailStage.Message;
        try
        {
            ValidateRegistrationAddresses(sender,recipient);RelayProtocol.Id(requestId);
            using var mail=new MimeMessage();mail.From.Add(MailboxAddress.Parse(sender));mail.To.Add(MailboxAddress.Parse(recipient));mail.Subject=RegistrationV2Protocol.Request;mail.MessageId="mavka-registration-"+requestId+"@mavka.local";
            var builder=new BodyBuilder{TextBody="Захищена заявка реєстрації Мавки."};builder.Attachments.Add("registration.json",Encoding.UTF8.GetBytes(wire),new ContentType("application","json"));mail.Body=builder.ToMessageBody();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));stage=RegistrationMailStage.Connect;
            var opened=await GmailTlsConnection.OpenSmtpAsync(timeout.Token);using var client=opened.Client;stage=RegistrationMailStage.Authenticate;
            await client.AuthenticateAsync(sender,appPassword,timeout.Token);stage=RegistrationMailStage.Submit;await client.SendAsync(mail,timeout.Token);
            try{using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(3));await client.DisconnectAsync(true,cleanup.Token);}
            catch(Exception error)when(error is IOException or OperationCanceledException or MailKit.ProtocolException or MailKit.ServiceNotConnectedException or SmtpCommandException){}
        }
        catch(Exception error)when(error is not RegistrationMailFailure){throw new RegistrationMailFailure(stage,error);}
    }
    internal static void ValidateRegistrationAddresses(string sender,string recipient)
    {
        if(!System.Net.Mail.MailAddress.TryCreate(sender,out var from) || from.Address!=sender)throw new ArgumentException("Некоректна адреса користувача.");
        if(!System.Net.Mail.MailAddress.TryCreate(recipient,out var to) || to.Address!=recipient)throw new ArgumentException("Вкажи адресу ГОЛОВНОЇ МАВКИ в налаштуваннях Gmail.");
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
        string fileName, string part,CancellationToken cancellation=default)
    {
        if (!MavkaPasswordedEnvelope.IsWrapped(part) ||
            System.Text.Encoding.UTF8.GetByteCount(part) > 700_000)
            throw new ArgumentException("Потрібна зашифрована та парольна частина.");
        var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(sender)); mail.To.Add(MailboxAddress.Parse(recipient));
        mail.Subject = MavkaFileParts.Format;
        var body = new BodyBuilder { TextBody = "Захищена частина файла Мавки." };
        body.Attachments.Add(fileName + MavkaFileParts.Extension, Encoding.UTF8.GetBytes(part), new ContentType("application", "json"));
        mail.Body = body.ToMessageBody();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(90));
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
        if (encryptedEnvelope.Length is < 100 or > 5_000_000 ||
            !MavkaPasswordedEnvelope.IsWrapped(encryptedEnvelope))
            throw new ArgumentException("Документ повинен бути захищений ключем і паролем.");

        var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(sender));
        mail.To.Add(MailboxAddress.Parse(recipient));
        mail.Subject = "MAVKA_ENCRYPTED_FILE_V1";
        var body = new BodyBuilder
        {
            TextBody = "Захищене вкладення Мавки. Потрібні ключ обміну й пароль документів."
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
        if (keys is null || !keys.HasKeyPair)throw new InvalidOperationException("Для першої реєстрації скануй QR з ПК у профілі. Незашифровані заявки не надсилаються.");
        string encryptedJson = MavkaEncryptedEnvelope.Encrypt(
            JsonSerializer.Serialize(structuredData), keys);
        var shared=new DocumentPasswordStore(keys.BootstrapContext).Load()??throw new InvalidOperationException("Введи спільний пароль обміну вручну.");
        encryptedJson=MavkaPasswordedEnvelope.Wrap(encryptedJson,shared);
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

