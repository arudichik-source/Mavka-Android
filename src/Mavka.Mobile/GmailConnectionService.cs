using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Mavka.Mobile;

internal sealed record MavkaTextAttachment(
    string FileName, string Text, string? RawJson = null);

internal sealed record MavkaMailItem(
    string Subject, string DateText, string Body,
    IReadOnlyList<MavkaTextAttachment> Attachments, uint MessageUid, uint UidValidity);

internal static class GmailConnectionService
{
    // Connectivity-only test. Does not use or transmit user credentials.
    // Certificate validation remains strict; the callback only collects diagnostics.
    public static async Task<string> DiagnoseTlsAsync()
    {
        var result = new List<string>
        {
            "Час UTC: " + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        };

        using (var http = new HttpClient())
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            try
            {
                using var response = await http.GetAsync(
                    "https://www.google.com/generate_204", timeout.Token);
                result.Add("HTTPS 443: HTTP " + (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                result.Add("HTTPS 443: " + ex.GetType().Name);
                if (ex.InnerException is not null)
                    result.Add("HTTPS причина: " + ex.InnerException.GetType().Name);
            }
        }

        string certificateErrors = "callback не викликано";
        string chainErrors = "немає даних";

        using var client = new ImapClient();
        client.ServerCertificateValidationCallback = (_, _, chain, errors) =>
        {
            certificateErrors = errors.ToString();
            chainErrors = chain is null || chain.ChainStatus.Length == 0
                ? "немає статусів"
                : string.Join(", ", chain.ChainStatus.Select(item => item.Status.ToString()));

            // DO NOT change this to true; invalid certificates must be rejected.
            return errors == System.Net.Security.SslPolicyErrors.None;
        };

        using var imapTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await client.ConnectAsync(
                "imap.gmail.com", 993, SecureSocketOptions.SslOnConnect, imapTimeout.Token);
            result.Add("IMAP 993: TLS успішно");
            await client.DisconnectAsync(true, imapTimeout.Token);
        }
        catch (MailKit.Security.SslHandshakeException ex)
        {
            result.Add("IMAP 993: SslHandshakeException");
            result.Add("Сертифікат: " + certificateErrors);
            result.Add("Ланцюг: " + chainErrors);
            result.Add("Внутрішня помилка: " +
                (ex.InnerException?.GetType().Name ?? "не вказано"));
            var explanation = string.Join(" ", ex.Message.Split(
                new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            result.Add("Причина: " + explanation[..Math.Min(explanation.Length, 700)]);
        }
        catch (Exception ex)
        {
            result.Add("IMAP 993: " + ex.GetType().Name);
            result.Add("Сертифікат: " + certificateErrors);
            result.Add("Внутрішня помилка: " +
                (ex.InnerException?.GetType().Name ?? "не вказано"));
        }

        // A strict handshake may fail when the device cannot query CRL/OCSP.
        // Run the same narrowly-scoped compatibility path used by the mail reader.
        if (result.Any(line => line.StartsWith("IMAP 993: SslHandshakeException", StringComparison.Ordinal)))
        {
            try
            {
                using var retryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var retryResult = await GmailTlsConnection.OpenImapAsync(retryTimeout.Token);
                using var retryClient = retryResult.Client;
                result.Add(retryResult.RevocationFallback
                    ? "Резервна перевірка: TLS успішно, без недоступної CRL/OCSP перевірки. Довіра та ім'я сервера перевіряються."
                    : "Резервна перевірка: TLS успішно зі стандартною перевіркою.");
                await retryClient.DisconnectAsync(true, retryTimeout.Token);
            }
            catch (Exception ex)
            {
                result.Add("Резервна перевірка: " + ex.GetType().Name);
            }
        }

        return string.Join("\n", result);
    }

    public static async Task<bool> TestImapAsync(
        string email, string appPassword, Action<string>? progress = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        progress?.Invoke("1/5: Підключення до Gmail...");
        var connection = await GmailTlsConnection.OpenImapAsync(timeout.Token);
        using var client = connection.Client;
        progress?.Invoke(connection.RevocationFallback
            ? "2/5: SSL/TLS підключено. OCSP/CRL недоступний; перевірка довіри та імені збережена."
            : "2/5: SSL/TLS підключено");
        progress?.Invoke("3/5: Авторизація Gmail...");
        await client.AuthenticateAsync(email, appPassword, timeout.Token);
        progress?.Invoke("4/5: Відкриття вхідних...");
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
        return connection.RevocationFallback;
    }

    // Read-only: server-side sender search followed by exact From-header matching.
    // Fetch plaintext and small TXT/JSON attachments; never execute or render HTML.
    public static async Task<IReadOnlyList<MavkaMailItem>> ReadFromSenderAsync(
        string account, string appPassword, string allowedSender,
        ExchangeKeyStore? exchangeKeys = null,
        DocumentPasswordStore? documentPasswords = null,
        string? recipientClientId = null)
    {
        if (string.IsNullOrWhiteSpace(allowedSender) ||
            !System.Net.Mail.MailAddress.TryCreate(allowedSender.Trim(), out var parsed) ||
            !string.Equals(parsed.Address, allowedSender.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Не задана коректна адреса головної Мавки.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var connection = await GmailTlsConnection.OpenImapAsync(timeout.Token);
        using var client = connection.Client;
        await client.AuthenticateAsync(account, appPassword, timeout.Token);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, timeout.Token);

        // Server-side search minimizes access to unrelated mail.
        var matched = await client.Inbox.SearchAsync(
            SearchQuery.FromContains(allowedSender.Trim()), timeout.Token);
        if (matched.Count == 0)
        {
            await client.DisconnectAsync(true, timeout.Token);
            return Array.Empty<MavkaMailItem>();
        }

        var recent = matched.Reverse().Take(100).ToList();
        var summaries = await client.Inbox.FetchAsync(
            recent,
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.BodyStructure,
            timeout.Token);

        var selected = summaries
            .Where(s => s.Envelope?.From?.Mailboxes.Any(m =>
                string.Equals(m.Address, allowedSender.Trim(), StringComparison.OrdinalIgnoreCase)) == true)
            .OrderByDescending(s => s.UniqueId.Id)
            .Take(15)
            .OrderBy(s => s.UniqueId.Id) // Key mail must be processed before later encrypted documents.
            .ToList();

        var result = new List<MavkaMailItem>();
        foreach (var summary in selected)
        {
            string body = "Повідомлення не містить текстової версії (text/plain).";
            if (summary.TextBody is not null)
            {
                var entity = await client.Inbox.GetBodyPartAsync(
                    summary.UniqueId, summary.TextBody, timeout.Token);
                if (entity is TextPart textPart)
                {
                    body = textPart.Text ?? "";
                    if (body.Length > 25000)
                        body = body[..25000] + "\n\n[Текст скорочено]";
                }
            }
            var date = summary.Envelope?.Date;
            var dateText = date.HasValue
                ? date.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                : "Без дати";
            var textAttachments = new List<MavkaTextAttachment>();
            foreach (var attachment in summary.Attachments.Take(10))
            {
                var fileName = attachment.FileName;
                if (string.IsNullOrWhiteSpace(fileName) ||
                    !(fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                      fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
                    attachment.Octets > 32768)
                    continue;

                var entity = await client.Inbox.GetBodyPartAsync(
                    summary.UniqueId, attachment, timeout.Token);
                if (entity is not MimePart part) continue;

                using var stream = new MemoryStream();
                part.Content.DecodeTo(stream);
                if (stream.Length > 32768) continue;

                // Never interpret attachment content as HTML or executable code.
                var contents = System.Text.Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF');
                var isJson = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                string? usableJson = isJson ? contents : null;
                string display = isJson ? MavkaJsonPreview.Format(contents) : contents;

                if (isJson && MavkaKeyPackage.IsPackage(contents))
                {
                    usableJson = null; // Never expose installed key material as an importable document.
                    try
                    {
                        if (exchangeKeys is null || documentPasswords?.Load() is not string filePassword)
                        {
                            display = "🔒 Налаштуй пароль документів один раз у меню налаштувань.";
                        }
                        else if (string.IsNullOrWhiteSpace(recipientClientId))
                        {
                            display = "🔒 Для приймання ключів заповни профіль користувача.";
                        }
                        else
                        {
                            bool changed = exchangeKeys.ImportPackage(
                                contents, filePassword, recipientClientId);
                            display = changed
                                ? "🔐 Нову пару ключів збережено: поточний " +
                                  exchangeKeys.CurrentKeyId + ", майбутній " + exchangeKeys.FutureKeyId
                                : "🔐 Ця пара ключів уже встановлена.";
                        }
                    }
                    catch (System.Security.Cryptography.CryptographicException)
                    {
                        display = "🔒 Пароль документа неправильний або пакет ключів пошкоджено.";
                    }
                    catch (Exception ex) when (ex is ArgumentException or FormatException or
                        InvalidOperationException or System.Text.Json.JsonException)
                    {
                        display = "🔒 Пакет ключів відхилено: некоректний, застарілий або іншому користувачеві.";
                    }
                }
                else if (isJson && MavkaEncryptedEnvelope.IsEnvelope(contents))
                {
                    usableJson = null;
                    try
                    {
                        if (exchangeKeys is null)
                            throw new KeyNotFoundException("Ключ обміну не налаштовано.");
                        usableJson = MavkaEncryptedEnvelope.Decrypt(contents, exchangeKeys);
                        display = "🔐 Зашифрований файл успішно розшифровано.\n\n" +
                                  MavkaJsonPreview.Format(usableJson);
                    }
                    catch (KeyNotFoundException)
                    {
                        display = "🔒 Немає потрібного ключа. Відкрий ☰ → Ключ обміну.";
                    }
                    catch (System.Security.Cryptography.CryptographicException)
                    {
                        display = "🔒 Неможливо перевірити автентичність файлу: невірний ключ або файл змінено.";
                    }
                    catch (Exception ex) when (ex is ArgumentException or FormatException
                                                   or System.Text.Json.JsonException
                                                   or InvalidOperationException)
                    {
                        display = "🔒 Некоректний формат зашифрованого вкладення.";
                    }
                }
                textAttachments.Add(new MavkaTextAttachment(
                    Path.GetFileName(fileName), display, usableJson));
                if (textAttachments.Count >= 3) break;
            }

            result.Add(new MavkaMailItem(
                summary.Envelope?.Subject ?? "(без теми)", dateText, body, textAttachments,
                summary.UniqueId.Id, client.Inbox.UidValidity));
        }

        await client.DisconnectAsync(true, timeout.Token);
        result.Reverse(); // Display latest first without breaking key rotation processing order.
        return result;
    }

    // Move only a previously displayed message to Gmail Trash, never permanently expunge it.
    // UIDVALIDITY and the *exact* authorized sender are rechecked immediately before moving.
    public static async Task MoveToTrashAsync(
        string account, string password, string authorizedSender, MavkaMailItem mail)
    {
        if (mail.MessageUid == 0 || mail.UidValidity == 0)
            throw new InvalidOperationException("Не вдалося підтвердити ідентифікатор листа.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var opened = await GmailTlsConnection.OpenImapAsync(timeout.Token);
        using var client = opened.Client;
        await client.AuthenticateAsync(account, password, timeout.Token);
        await client.Inbox.OpenAsync(FolderAccess.ReadWrite, timeout.Token);

        if (client.Inbox.UidValidity != mail.UidValidity)
            throw new InvalidOperationException("Поштова папка змінилася. Онови список листів.");

        var uid = new UniqueId(mail.MessageUid);
        var summary = (await client.Inbox.FetchAsync(
            new[] { uid }, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope,
            timeout.Token)).FirstOrDefault();

        if (summary?.Envelope?.From?.Mailboxes.Any(m =>
                string.Equals(m.Address, authorizedSender,
                    StringComparison.OrdinalIgnoreCase)) != true)
            throw new InvalidOperationException("Відправник не пройшов повторну перевірку.");

        var trash = client.GetFolder(SpecialFolder.Trash);
        await client.Inbox.MoveToAsync(uid, trash, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }
}
