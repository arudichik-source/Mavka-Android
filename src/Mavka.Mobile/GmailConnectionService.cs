using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Mavka.Mobile;

internal sealed record MavkaTextAttachment(
    string FileName, string Text, string? RawJson = null,
    bool InstalledKeyPackage = false, string? SecureFileEnvelope = null,
    string? SecureFilePart = null, bool AuthenticatedJson = false,
    byte[]? ImageBytes = null);

internal sealed record MavkaMailItem(
    string Subject, string DateText, string Body,
    IReadOnlyList<MavkaTextAttachment> Attachments, uint MessageUid, uint UidValidity,
    bool AttachmentsComplete = false, string? ProcessingIssue = null,
    bool CachedPreview = false, string? CachedInvoiceNumber = null,
    string? CachedInvoiceRecipient = null, bool SingleSecureFile = false,
    bool PreviouslyArchived = false, bool AllSecureFileAttachments = false);

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
        string? recipientClientId = null,
        Func<uint, IReadOnlyDictionary<uint, string>>? processedIndex = null,
        bool noticesOnly = false,CancellationToken cancellation=default)
    {
        if (string.IsNullOrWhiteSpace(allowedSender) ||
            !System.Net.Mail.MailAddress.TryCreate(allowedSender.Trim(), out var parsed) ||
            !string.Equals(parsed.Address, allowedSender.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Не задана коректна адреса головної Мавки.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var connection = await GmailTlsConnection.OpenImapAsync(timeout.Token);
        using var client = connection.Client;
        await client.AuthenticateAsync(account, appPassword, timeout.Token);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, timeout.Token);
        // UIDVALIDITY protects against UID reuse after a mailbox reset. The
        // index contains only durable processing acknowledgments.
        var processed = processedIndex?.Invoke(client.Inbox.UidValidity) ??
            new Dictionary<uint, string>();
        bool IsTrashed(uint id) =>
            GmailProcessingSelectionPolicy.IsTrashed(processed, id);
        bool IsReady(uint id) =>
            GmailProcessingSelectionPolicy.IsReady(processed, id);

        // Server-side search minimizes access to unrelated mail.
        var matched = await client.Inbox.SearchAsync(
            noticesOnly
                ? SearchQuery.FromContains(allowedSender.Trim()).And(
                    SearchQuery.SubjectContains(MavkaNoticeProtocol.Format))
                : SearchQuery.FromContains(allowedSender.Trim()), timeout.Token);
        if (matched.Count == 0)
        {
            await client.DisconnectAsync(true, timeout.Token);
            return Array.Empty<MavkaMailItem>();
        }

        // Search key provisioning mails separately, because the first key package
        // may predate the latest 15 ordinary emails.
        var keyMatches = noticesOnly
            ? Array.Empty<UniqueId>()
            : (await client.Inbox.SearchAsync(
                SearchQuery.FromContains(allowedSender.Trim())
                    .And(SearchQuery.SubjectContains(MavkaKeyPackage.MailSubject).Or(SearchQuery.SubjectContains("MAVKA_BOOTSTRAP_KEYS_V2"))),
                timeout.Token)).ToArray();
        var keyUids = keyMatches.Reverse().Take(10).Select(uid => uid.Id).ToHashSet();

        var fileMatches = noticesOnly
            ? Array.Empty<UniqueId>()
            : (await client.Inbox.SearchAsync(
                SearchQuery.FromContains(allowedSender.Trim())
                    .And(SearchQuery.SubjectContains(MavkaFileParts.Format)),
                timeout.Token)).ToArray();
        var fileUids = fileMatches.Reverse().Take(150).Select(u => u.Id).ToHashSet();
        var changeMatches=noticesOnly?Array.Empty<UniqueId>():(await client.Inbox.SearchAsync(SearchQuery.FromContains(allowedSender.Trim()).And(SearchQuery.SubjectContains(MobileChangePart.Format)),timeout.Token)).ToArray();
        var changeUids=changeMatches.Reverse().Take(100).Select(u=>u.Id).ToHashSet();
        // A completed archive item is never fetched again merely to retry
        // Gmail Trash. The remote operation still revalidates the exact From
        // field and UIDVALIDITY before moving the UID.
        var readyForTrash = GmailProcessingSelectionPolicy.ReadyForTrash(
            matched.Reverse().Select(u => u.Id), processed, 10);
        // Fetch ordinary message metadata in small batches rather than all
        // 300 candidates up front. The exact sender check still determines
        // when 15 actual authorized messages have been found; spoofed display
        // names never count toward that number.
        const MessageSummaryItems summaryItems =
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope |
            MessageSummaryItems.BodyStructure;
        bool ExactSender(IMessageSummary summary)
        {
            var from=summary.Envelope?.From?.Mailboxes.ToArray()??[];
            return from.Length==1 && from[0].Address.Equals(allowedSender.Trim(),StringComparison.OrdinalIgnoreCase);
        }

        var fetched = new Dictionary<uint, IMessageSummary>();
        var ordinaryUids = GmailProcessingSelectionPolicy.PendingPayloads(
            matched.Reverse().Select(u => u.Id), processed, 300);
        foreach (var batch in ordinaryUids
                     .Select(id => new UniqueId(id)).Chunk(40))
        {
            var batchSummaries = await client.Inbox.FetchAsync(
                batch, summaryItems, timeout.Token);
            foreach (var summary in batchSummaries)
                fetched[summary.UniqueId.Id] = summary;
            if (fetched.Values.Count(ExactSender) >= 15)
                break;
        }

        // Independently include all recent encrypted-file parts and key
        // provisioning messages, even when they are older than the 15 ordinary
        // display mails. Never skip multipart messages for speed.
        var special = keyMatches.Reverse().Take(10)
            .Concat(fileMatches.Reverse().Take(150)).Concat(changeMatches.Reverse().Take(100))
            .Distinct()
            .Where(uid => !IsTrashed(uid.Id) && !IsReady(uid.Id) &&
                !fetched.ContainsKey(uid.Id))
            .ToList();
        if (special.Count > 0)
        {
            var specialSummaries = await client.Inbox.FetchAsync(
                special, summaryItems, timeout.Token);
            foreach (var summary in specialSummaries)
                fetched[summary.UniqueId.Id] = summary;
        }

        // Exact sender re-check. Process key messages in UID order before
        // the rest so even an old provisioning letter unlocks recent files.
        var verified = fetched.Values.Where(ExactSender).ToList();
        var candidates = verified
            .OrderByDescending(s => s.UniqueId.Id).Take(15)
            .Concat(verified.Where(s => fileUids.Contains(s.UniqueId.Id))
                .OrderByDescending(s => s.UniqueId.Id).Take(150))
            .Concat(verified.Where(s=>changeUids.Contains(s.UniqueId.Id)).OrderBy(s=>s.UniqueId.Id).Take(100))
            .Concat(verified.Where(s => keyUids.Contains(s.UniqueId.Id))
                .OrderByDescending(s => s.UniqueId.Id).Take(10))
            .GroupBy(s => s.UniqueId.Id).Select(group => group.First())
            .OrderBy(s => s.UniqueId.Id)
            .ToList();
        // Returning one bounded portion lets the Activity durably import/trash
        // it before downloading more. Previously 100 changes x 50 attachments
        // could exhaust the whole connection timeout before returning ANY data.
        // Every remaining UID stays in Gmail for the next requested cycle.
        var batchIds=GmailProcessingSelectionPolicy.PayloadBatch(candidates.Select(s=>s.UniqueId.Id),keyUids,changeUids,fileUids);
        var selected=batchIds.Select(id=>candidates.Single(s=>s.UniqueId.Id==id)).ToArray();

        var result = new List<MavkaMailItem>();
        foreach (var uid in readyForTrash)
            result.Add(new MavkaMailItem("Раніше оброблений лист", "",
                "Документ уже перевірено. Повторюється лише перенесення до кошика.",
                Array.Empty<MavkaTextAttachment>(), uid, client.Inbox.UidValidity,
                PreviouslyArchived: true));
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
            // Keep the entire bounded attachment set; truncating after three
            // caused imported documents and Gmail Trash to diverge.
            const int MaxAttachmentsPerMail = 50;
            var attachmentCount = summary.Attachments.Count();
            bool attachmentsComplete = attachmentCount > 0 &&
                attachmentCount <= MaxAttachmentsPerMail;
            foreach (var attachment in summary.Attachments.Take(MaxAttachmentsPerMail))
            {
                var fileName = attachment.FileName;
                // A .mavkafile is an AES-256-GCM JSON container, not a raw
                // PDF/XLSX. Only retain ciphertext in memory until user saves.
                // Keep the Gmail letter until durable file verification.
                if (!string.IsNullOrWhiteSpace(fileName) &&
                    fileName.EndsWith(MavkaEncryptedFileEnvelope.Extension,
                        StringComparison.OrdinalIgnoreCase))
                {
                    attachmentsComplete = false;
                    if (attachment.Octets is > 0 and <= 8_000_000)
                    {
                        try
                        {
                            var binaryPart = await client.Inbox.GetBodyPartAsync(
                                summary.UniqueId, attachment, timeout.Token);
                            if (binaryPart is MimePart securedPart)
                            {
                                using var ciphertextStream = new BoundedWriteStream(5_000_000);
                                securedPart.Content.DecodeTo(ciphertextStream);
                                if (ciphertextStream.Length > 0 &&
                                    ciphertextStream.Length <= 5_000_000)
                                {
                                    string envelope = new System.Text.UTF8Encoding(false, true)
                                        .GetString(ciphertextStream.ToArray())
                                        .TrimStart('\uFEFF');
                                    if(!MavkaPasswordedEnvelope.IsWrapped(envelope))throw new System.Security.Cryptography.CryptographicException("Немає захисту спільним паролем.");
                                    if (MavkaPasswordedEnvelope.IsWrapped(envelope))
                                    {
                                        var savedPassword = documentPasswords?.Load();
                                        if (string.IsNullOrWhiteSpace(savedPassword))
                                            throw new InvalidOperationException(
                                                "Налаштуй пароль документів.");
                                        envelope = MavkaPasswordedEnvelope.Unwrap(
                                            envelope, savedPassword);
                                    }
                                    textAttachments.Add(new MavkaTextAttachment(
                                        Path.GetFileName(fileName),
                                        "🔐 Захищений PDF/XLSX: потрібне підтвердження збереження.",
                                        SecureFileEnvelope: envelope));
                                }
                            }
                        }
                        catch (Exception ex) when (ex is System.Text.DecoderFallbackException or
                            System.IO.IOException or InvalidOperationException or
                            System.Security.Cryptography.CryptographicException or
                            ArgumentException or FormatException or
                            System.Text.Json.JsonException)
                        {
                            textAttachments.Add(new MavkaTextAttachment(
                                Path.GetFileName(fileName),
                                "🔒 Пошкоджений або недоступний файл."));
                        }
                    }
                    else
                        textAttachments.Add(new MavkaTextAttachment(
                            Path.GetFileName(fileName),
                            "🔒 Розмір захищеного вкладення не підтримується."));
                    if (textAttachments.Count >= MaxAttachmentsPerMail) break;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(fileName) &&
                    fileName.EndsWith(MavkaFileParts.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    attachmentsComplete = false;
                    if (attachment.Octets is > 0 and <= 850_000)
                    {
                        try
                        {
                            var item = await client.Inbox.GetBodyPartAsync(
                                summary.UniqueId, attachment, timeout.Token);
                            if (item is MimePart chunk)
                            {
                                using var chunkStream = new BoundedWriteStream(700_000);
                                chunk.Content.DecodeTo(chunkStream);
                                var encrypted = new System.Text.UTF8Encoding(false, true)
                                    .GetString(chunkStream.ToArray());
                                if(!MavkaPasswordedEnvelope.IsWrapped(encrypted))throw new System.Security.Cryptography.CryptographicException("Немає захисту спільним паролем.");
                                if (MavkaPasswordedEnvelope.IsWrapped(encrypted))
                                {
                                    var savedPassword = documentPasswords?.Load();
                                    if (string.IsNullOrWhiteSpace(savedPassword))
                                        throw new InvalidOperationException("Налаштуй пароль документів.");
                                    encrypted = MavkaPasswordedEnvelope.Unwrap(
                                        encrypted, savedPassword);
                                }
                                textAttachments.Add(new MavkaTextAttachment(
                                    Path.GetFileName(fileName),
                                    "Захищена частина файла · автоматичне складання",
                                    SecureFilePart: encrypted));
                            }
                        }
                        catch (Exception ex) when (ex is System.IO.IOException or
                            InvalidOperationException or System.Security.Cryptography.CryptographicException or
                            ArgumentException or FormatException or System.Text.DecoderFallbackException or
                            System.Text.Json.JsonException)
                        {
                            textAttachments.Add(new MavkaTextAttachment(
                                Path.GetFileName(fileName),
                                "🔒 Не вдалося перевірити пароль або частину файла."));
                        }
                    }
                    else
                        textAttachments.Add(new MavkaTextAttachment(
                            Path.GetFileName(fileName), "Частина файла завелика."));
                    if (textAttachments.Count >= MaxAttachmentsPerMail) break;
                    continue;
                }
                // Photos are preview-only and never enter the accounting archive.
                // No automatic Trash: an ordinary image is not a protected document.
                if (MailImagePreviewPolicy.IsImage(fileName))
                {
                    attachmentsComplete = false;
                    if (attachment.Octets is > 0 and <= MailImagePreviewPolicy.MaxBytes)
                    {
                        try
                        {
                            var imageEntity = await client.Inbox.GetBodyPartAsync(
                                summary.UniqueId, attachment, timeout.Token);
                            if (imageEntity is MimePart picture)
                            {
                                using var imageStream = new BoundedWriteStream(
                                    MailImagePreviewPolicy.MaxBytes);
                                picture.Content.DecodeTo(imageStream);
                                var bytes = imageStream.ToArray();
                                if (MailImagePreviewPolicy.HasValidSignature(fileName!, bytes))
                                    textAttachments.Add(new MavkaTextAttachment(
                                        Path.GetFileName(fileName!), "Фото для перегляду без збереження",
                                        ImageBytes: bytes));
                                else
                                    textAttachments.Add(new MavkaTextAttachment(
                                        Path.GetFileName(fileName!), "Формат зображення пошкоджений або не підтримується."));
                            }
                        }
                        catch (Exception ex) when (ex is IOException or InvalidOperationException)
                        {
                            textAttachments.Add(new MavkaTextAttachment(
                                Path.GetFileName(fileName!), "Не вдалося отримати зображення."));
                        }
                    }
                    else
                        textAttachments.Add(new MavkaTextAttachment(
                            Path.GetFileName(fileName!), "Зображення завелике (максимум 2 МБ)."));
                    continue;
                }
                var officePreview = MailAttachmentDisplayPolicy.Detect(fileName);
                if (officePreview != MailAttachmentDisplayPolicy.PreviewType.None)
                {
                    // A PDF/XLSX is visible in the inbox as metadata, never
                    // decoded, auto-imported or made eligible for automatic Trash.
                    attachmentsComplete = false;
                    textAttachments.Add(new MavkaTextAttachment(fileName!,
                        MailAttachmentDisplayPolicy.Description(
                            fileName!, attachment.Octets)));
                    if (textAttachments.Count >= MaxAttachmentsPerMail) break;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(fileName) ||
                    !(fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                      fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
                    attachment.Octets > 370000)
                {
                    attachmentsComplete = false;
                    continue;
                }

                var entity = await client.Inbox.GetBodyPartAsync(
                    summary.UniqueId, attachment, timeout.Token);
                if (entity is not MimePart part)
                {
                    attachmentsComplete = false;
                    continue;
                }

                using var stream = new BoundedWriteStream(500000);
                part.Content.DecodeTo(stream);
                if (stream.Length > 500000)
                {
                    attachmentsComplete = false;
                    continue;
                }

                // Never interpret attachment content as HTML or executable code.
                var contents = System.Text.Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF');
                var isJson = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                var passwordProtected=MavkaPasswordedEnvelope.IsWrapped(contents);
                if (passwordProtected)
                {
                    try
                    {
                        var filePassword = documentPasswords?.Load() ??
                            throw new InvalidOperationException("Налаштуй пароль документів.");
                        contents = MavkaPasswordedEnvelope.Unwrap(contents, filePassword);
                    }
                    catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or
                        InvalidOperationException or ArgumentException or FormatException or
                        System.Text.Json.JsonException)
                    {
                        attachmentsComplete = false;
                        textAttachments.Add(new MavkaTextAttachment(
                            Path.GetFileName(fileName),
                            "🔒 Неправильний пароль або пошкоджене зашифроване вкладення."));
                        continue;
                    }
                }
                string? usableJson = isJson ? contents : null;
                string display = isJson ? MavkaJsonPreview.Format(contents) : contents;
                bool installedKey = false;
                bool authenticatedJson = false;

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
                            // An installed package must also be readable from Keystore.
                            byte[]? current = null, future = null;
                            try
                            {
                                current = exchangeKeys.CurrentKeyId is string cid
                                    ? exchangeKeys.GetKey(cid) : null;
                                future = exchangeKeys.FutureKeyId is string fid
                                    ? exchangeKeys.GetKey(fid) : null;
                                installedKey = exchangeKeys.HasKeyPair &&
                                    current is { Length: 32 } && future is { Length: 32 };
                            }
                            finally
                            {
                                // Never leave an unwrapped key in memory if reading
                                // the second key throws halfway through validation.
                                if (current is not null)
                                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(current);
                                if (future is not null)
                                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(future);
                            }
                            if (!installedKey)
                                throw new InvalidOperationException("Cannot verify persisted keys.");
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
                        if(new BootstrapStore(exchangeKeys.BootstrapContext).TryInstall(contents,allowedSender))
                        {
                            installedKey=true;authenticatedJson=true;usableJson=null;
                            display="Поточний і майбутній ключі та дозволи встановлено. Введений вручну спільний пароль не передавався. Активуй Relay після дозволу ПК.";
                            var bootstrap=new BootstrapStore(exchangeKeys.BootstrapContext);
                            try
                            {
                                var relay=new RelaySync(exchangeKeys.BootstrapContext);
                                if(!new RelayIdentity(exchangeKeys.BootstrapContext).Enabled)await relay.Activate(timeout.Token);
                                relay.Queue("bootstrap-installed:"+RelayProtocol.Hash(bootstrap.Confirmation!),bootstrap.Confirmation!);
                                await relay.Synchronize(timeout.Token);
                                display="Ключі та дозволи збережено. Gmail і Relay підключено. Спільний пароль не передавався.";
                            }
                            catch(Exception activationError)when(activationError is not OperationCanceledException)
                            {
                                display="Ключі та дозволи збережено. Relay очікує реєстрації або з'єднання: натисни «Активувати після дозволу ПК» у Cloudflare. Код: "+activationError.GetType().Name;
                            }
                        }
                        else
                        {
                        if(!passwordProtected)throw new System.Security.Cryptography.CryptographicException("Немає захисту спільним паролем.");
                        usableJson = MavkaEncryptedEnvelope.Decrypt(contents, exchangeKeys);
                        authenticatedJson = true;
                        display = "🔐 Зашифрований файл успішно розшифровано.\n\n" +
                                  MavkaJsonPreview.Format(usableJson);
                        }
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
                // Once the key pair has been installed, NEVER import raw
                // JSON as an accounting document. Only the AES-GCM verified
                // MAVKA_ENCRYPTED_V1 payload is permitted. The password-sealed
                // key package above is a separate enrollment operation.
                if (IncomingAttachmentSecurityPolicy.RejectPlaintext(
                    exchangeKeys?.HasKeyPair == true, authenticatedJson,
                    isJson && MavkaKeyPackage.IsPackage(contents)))
                {
                    usableJson = null;
                    display = "🔒 Незашифроване TXT/JSON-вкладення відхилено. " +
                        "Після встановлення ключів облікові документи " +
                        "мають надходити у MAVKA_ENCRYPTED_V1.";
                }
                textAttachments.Add(new MavkaTextAttachment(
                    Path.GetFileName(fileName), display, usableJson, installedKey, AuthenticatedJson: authenticatedJson));
                if (textAttachments.Count >= MaxAttachmentsPerMail) break;
            }

            result.Add(new MavkaMailItem(
                summary.Envelope?.Subject ?? "(без теми)", dateText, body, textAttachments,
                summary.UniqueId.Id, client.Inbox.UidValidity,
                attachmentsComplete && textAttachments.Count == summary.Attachments.Count(),
                SingleSecureFile: summary.Attachments.Count() == 1 &&
                    textAttachments.Count == 1 &&
                    textAttachments[0].SecureFileEnvelope is not null,
                AllSecureFileAttachments: summary.Attachments.Count() > 0 &&
                    summary.Attachments.Count() <= MaxAttachmentsPerMail &&
                    textAttachments.Count == summary.Attachments.Count() &&
                    textAttachments.All(a => a.SecureFileEnvelope is not null)));
        }

        try{await client.DisconnectAsync(true, timeout.Token);}
        catch(Exception ex)when(ex is IOException or OperationCanceledException){cancellation.ThrowIfCancellationRequested();}
        result.Reverse(); // Display latest first without breaking key rotation processing order.
        return result;
    }

    // Move only a previously displayed message to Gmail Trash, never permanently expunge it.
    // UIDVALIDITY and the *exact* authorized sender are rechecked immediately before moving.
    public static async Task MoveToTrashAsync(
        string account, string password, string authorizedSender, MavkaMailItem mail,CancellationToken cancellation=default)
    {
        if (mail.MessageUid == 0 || mail.UidValidity == 0)
            throw new InvalidOperationException("Не вдалося підтвердити ідентифікатор листа.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(35));
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

        if (summary is null)
        { await client.DisconnectAsync(true, timeout.Token); return; } // Already absent; no destructive action.
        if (summary.Envelope?.From?.Mailboxes.Any(m =>
                string.Equals(m.Address, authorizedSender,
                    StringComparison.OrdinalIgnoreCase)) != true)
            throw new InvalidOperationException("Відправник не пройшов повторну перевірку.");

        var trash = client.GetFolder(SpecialFolder.Trash);
        await client.Inbox.MoveToAsync(uid, trash, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }
}

