using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Mavka.Mobile;

internal sealed record MavkaTextAttachment(string FileName, string Text); // TXT or JSON text preview

internal sealed record MavkaMailItem(
    string Subject, string DateText, string Body, IReadOnlyList<MavkaTextAttachment> Attachments);

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

        return string.Join("\n", result);
    }

    public static async Task TestImapAsync(
        string email, string appPassword, Action<string>? progress = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var client = new ImapClient();
        progress?.Invoke("1/5: Підключення до Gmail...");
        await client.ConnectAsync("imap.gmail.com", 993, SecureSocketOptions.SslOnConnect, timeout.Token);
        progress?.Invoke("2/5: SSL/TLS підключено");
        progress?.Invoke("3/5: Авторизація Gmail...");
        await client.AuthenticateAsync(email, appPassword, timeout.Token);
        progress?.Invoke("4/5: Відкриття вхідних...");
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }

    // Read-only: server-side sender search followed by exact From-header matching.
    // Fetch plaintext and small TXT/JSON attachments; never execute or render HTML.
    public static async Task<IReadOnlyList<MavkaMailItem>> ReadFromSenderAsync(
        string account, string appPassword, string allowedSender)
    {
        if (string.IsNullOrWhiteSpace(allowedSender) ||
            !System.Net.Mail.MailAddress.TryCreate(allowedSender.Trim(), out var parsed) ||
            !string.Equals(parsed.Address, allowedSender.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Не задана коректна адреса головної Мавки.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        using var client = new ImapClient();
        await client.ConnectAsync("imap.gmail.com", 993, SecureSocketOptions.SslOnConnect, timeout.Token);
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
                var display = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? MavkaJsonPreview.Format(contents)
                    : contents;
                textAttachments.Add(new MavkaTextAttachment(
                    Path.GetFileName(fileName), display));
                if (textAttachments.Count >= 3) break;
            }

            result.Add(new MavkaMailItem(
                summary.Envelope?.Subject ?? "(без теми)", dateText, body, textAttachments));
        }

        await client.DisconnectAsync(true, timeout.Token);
        return result;
    }
}
