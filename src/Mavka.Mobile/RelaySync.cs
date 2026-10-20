using Android.Content;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;
internal sealed class RelaySync
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Context _context;
    private readonly UserProfile _profile;
    private readonly RelayIdentity _identity;
    private readonly ExchangeKeyStore _keys;
    public RelaySync(Context context)
    {
        _context = context;
        _profile = new UserProfileStore(context).Load() ?? throw new InvalidOperationException("Спочатку заповни профіль.");
        if(new MobileAccessStore(context).Load() is {} granted)_profile=_profile with{Unit=granted.units[0].unit,IsMvo=granted.isMvo};
        _identity = new RelayIdentity(context); _keys = new ExchangeKeyStore(context);
    }
    private RelayStore Store() => new(_context, _identity.DeviceId + "|" + _profile.ClientId);
    public (int Incoming, int Waiting, int Server) Counts() { using var db = Store(); return db.Counts(); }
    public async Task Activate(CancellationToken cancellation = default)
    {
        using var api = new RelayApiClient(_identity.DeviceId, _identity.Sign);
        var reply = await api.Call("POST", "/v1/devices/activate", "{}", cancellation);
        if (reply.GetProperty("deviceId").GetString() != _identity.DeviceId ||
            reply.GetProperty("status").GetString() != "active") throw new CryptographicException("Некоректна відповідь активації.");
        _identity.SetEnabled(true); _identity.Status("Пристрій активний");
        RelayBackground.SetEnabled(_context, true);
    }
    public string Queue(string logicalId, string json)
    {
        if (!_identity.Enabled) throw new InvalidOperationException("Спочатку активуй Relay.");
        if (!_keys.HasKeyPair) throw new InvalidOperationException("Спочатку встанови поточний і майбутній ключі обміну.");
        var group = RelayProtocol.Hash(logicalId);
        using var db = Store();
        if (db.HasGroup(group)) return group;
        var packets = new List<(string Id, string Body)>();
        var expires = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
        var password=new DocumentPasswordStore(_context).Load()??throw new InvalidOperationException("Введи спільний пароль обміну вручну.");
        var original=json;
        using(var check=JsonDocument.Parse(json))
        {
            var format=check.RootElement.GetProperty("format").GetString();
            if(format is not ("MAVKA_ENCRYPTED_V1" or "MAVKA_PASSWORD_WRAPPED_V1" or "MAVKA_ENCRYPTED_FILE_V1" or "MAVKA_FILE_PART_V1"))
                original=MavkaEncryptedEnvelope.Encrypt(json,_keys);
        }
        if(!MavkaPasswordedEnvelope.IsWrapped(original))original=MavkaPasswordedEnvelope.Wrap(original,password);
        // Even a small logical message uses password-protected content parts;
        // each server packet remains a client-bound MAVKA_ENCRYPTED_V1 envelope.
        var contents=RelayProtocol.Split(original,_profile.ClientId);
        foreach (var part in contents)
        {
            var envelope = MavkaEncryptedEnvelope.Encrypt(part, _keys);
            using var parsed = JsonDocument.Parse(envelope);
            var payload = RelayProtocol.Envelope(parsed.RootElement);
            var id = Guid.NewGuid().ToString("D");
            packets.Add((id, RelayProtocol.Serialize(new { messageId = id,
                recipientDeviceId = RelayProtocol.MainId, expiresAt = expires, payload })));
        }
        db.Queue(group, packets);
        if (!db.HasGroup(group)) throw new IOException("Не вдалося зберегти вихідну чергу.");
        return group;
    }
    public bool Submitted(string group) { using var db = Store(); return db.Submitted(group); }
    private async Task<int> Flush(RelayApiClient api, RelayStore db, CancellationToken cancellation)
    {
        int sent = 0;
        foreach (var row in db.Outbox())
        {
            cancellation.ThrowIfCancellationRequested();
            using var doc = JsonDocument.Parse(row.Body);
            if (doc.RootElement.GetProperty("expiresAt").GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                throw new IOException("Строк вихідного пакета Relay минув. Локальна копія збережена.");
            var reply = await api.Call("POST", "/v1/messages", row.Body, cancellation);
            var state = reply.GetProperty("status").GetString();
            if (reply.GetProperty("messageId").GetString() != row.Id || state is not ("queued" or "delivered"))
                throw new CryptographicException("Некоректне підтвердження надсилання.");
            db.Sent(row.Id, state == "delivered"); sent++;
        }
        return sent;
    }
    public async Task<string> Synchronize(CancellationToken cancellation = default)
    {
        if (!_identity.Enabled) throw new InvalidOperationException("Relay не активовано.");
        if (_identity.RetryAfter > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            throw new IOException("Після ліміту Relay зачекай хвилину.");
        if (!await Gate.WaitAsync(0, cancellation)) return "Синхронізація Relay вже триває.";
        try
        {
            using var api = new RelayApiClient(_identity.DeviceId, _identity.Sign);
            using var db = Store();
            if(new BootstrapStore(_context).Confirmation is string bootstrapAck)
                Queue("bootstrap-installed:"+RelayProtocol.Hash(bootstrapAck),bootstrapAck);
            int received = 0, sent = 0, acked = 0, deferred = 0;
            string path = "/v1/messages?limit=10";
            for (int page = 0; page < 3; page++)
            {
                var reply = await api.Call("GET", path, "", cancellation);
                foreach (var packet in reply.GetProperty("messages").EnumerateArray())
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var verified = RelayProtocol.Verify(packet, _identity.DeviceId, DateTimeOffset.UtcNow);
                        if(new BootstrapStore(_context).TryInstall(verified.Envelope,messageId:verified.Id))
                        {
                            var confirmation=new BootstrapStore(_context).Confirmation!;
                            db.SaveIncoming(verified,confirmation);db.Imported(new[]{verified.Id});
                            Queue("bootstrap-installed:"+RelayProtocol.Hash(confirmation),confirmation);received++;continue;
                        }
                        var plain = MavkaEncryptedEnvelope.Decrypt(verified.Envelope, _keys);
                        _ = RelayProtocol.Part(plain, _profile.ClientId);
                        db.SaveIncoming(verified, plain); received++;
                    }
                    catch (Exception ex) when (ex is CryptographicException or KeyNotFoundException or ArgumentException or JsonException)
                    { deferred++; } // No ACK for failed signature, wrong key or malformed content.
                }
                if (!reply.TryGetProperty("next", out var next) || next.ValueKind == JsonValueKind.Null) break;
                var afterId = next.GetProperty("afterId").GetString()!; RelayProtocol.Id(afterId);
                path = "/v1/messages?limit=10&afterTime=" + next.GetProperty("afterTime").GetInt64() + "&afterId=" + Uri.EscapeDataString(afterId);
            }
            deferred += await ImportPending(db, cancellation);
            foreach (var row in db.Inbox(false).Take(30))
            {
                // Direct documents require domain import; transport chunks may be
                // ACKed after durable save, independently of whole-file completion.
                if (!db.IsImported(row.Id) && RelayProtocol.Part(row.Plain) is null) continue;
                if (row.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) { db.Acked(row.Id); continue; }
                try
                {
                    var result = await api.Call("POST", "/v1/messages/" + Uri.EscapeDataString(row.Id) + "/ack",
                        RelayProtocol.Serialize(new { payloadSha256 = row.Hash }), cancellation);
                    if (result.GetProperty("messageId").GetString() != row.Id ||
                        result.GetProperty("status").GetString() != "delivered" ||
                        !result.GetProperty("deleted").GetBoolean()) throw new CryptographicException("Некоректний ACK.");
                    db.Acked(row.Id); acked++;
                }
                catch (RelayApiException ex) when (ex.Status == 410) { db.Acked(row.Id); }
            }
            db.Cleanup();
            // MVO confirmation remains local; there is no business receipt to PC.
            using(var ledger=new PropertyLedgerStore(_context))
                foreach(var id in ledger.ListPendingReceiptIds().Take(100))ledger.MarkMailSent(id);
            sent=await Flush(api,db,cancellation);
            MavkaHomeWidget.Refresh(_context);
            var counts = db.Counts();
            var report = $"Отримано: {received}; підтверджено збереження: {acked}; спроб надсилання: {sent}. " +
                $"Локальна черга: {counts.Waiting}; на сервері очікують доставки: {counts.Server}; відкладено: {deferred}.";
            _identity.Status(report); return report;
        }
        catch (RelayApiException ex)
        {
            if (ex.Status == 429) _identity.RateLimited();
            _identity.Status(ex.Message); throw;
        }
        finally { Gate.Release(); }
    }
    private async Task<int> ImportPending(RelayStore db, CancellationToken cancellation)
    {
        int deferred = 0;
        var rows = db.Inbox();
        foreach (var row in rows.Where(r => RelayProtocol.Part(r.Plain) is null))
        {
            try
            {
                if (await Import(row.Plain, cancellation)) db.Imported(new[] { row.Id });
                else deferred++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { deferred++; }
        }
        var groups = rows.Select(r => (Row: r, Part: RelayProtocol.Part(r.Plain)))
            .Where(x => x.Part is not null).GroupBy(x => x.Part!.transferId);
        foreach (var group in groups)
        {
            try
            {
                var json = RelayProtocol.Assemble(group.Select(x => x.Part!).ToArray());
                if (json is not null && await Import(json, cancellation)) db.Imported(group.Select(x => x.Row.Id));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { deferred++; }
        }
        return deferred;
    }
    private async Task<bool> Import(string json, CancellationToken cancellation)
    {
        if (MavkaEncryptedEnvelope.IsEnvelope(json))
            json = RelayContentCipher.Decrypt(json, key => _keys.GetKey(key));
        if(_keys.RequiresPassword && !MavkaPasswordedEnvelope.IsWrapped(json))throw new CryptographicException("Повідомлення не захищене спільним паролем.");
        if (MavkaPasswordedEnvelope.IsWrapped(json))
        {
            var password = new DocumentPasswordStore(_context).Load() ?? throw new KeyNotFoundException("Потрібен пароль документів.");
            json = MavkaPasswordedEnvelope.Unwrap(json, password);
            if (MavkaEncryptedEnvelope.IsEnvelope(json)) json = RelayContentCipher.Decrypt(json, key => _keys.GetKey(key));
        }
        if (MavkaNoticeProtocol.TryRead(json, _profile.Unit, DateTimeOffset.UtcNow, out var notice) && notice is not null)
        {
            using var notices = new MavkaNoticeStore(_context);
            bool added = notices.Add(notice, json);
            if (notices.Find(notice.NoticeId, _profile.Unit) is null) throw new IOException("Сповіщення не збережено.");
            if (added) MavkaNoticeAlerts.ShowNew(_context, notice.NoticeId);
            return true;
        }
        using(var policyJson=JsonDocument.Parse(json))
        {
            if(policyJson.RootElement.TryGetProperty("format",out var kind) && kind.GetString()=="MAVKA_ACCESS_UPDATE_V1")
            {
                if(policyJson.RootElement.GetProperty("recipientClientId").GetString()!=_profile.ClientId)throw new CryptographicException("Інший користувач.");
                new MobileAccessStore(_context).Accept(policyJson.RootElement.GetProperty("access"));return true;
            }
        }
        if (new MobileAccessStore(_context).TryDocument(json, out var document) && document is not null)
        {
            using var archive = new DocumentArchiveStore(_context);
            archive.SaveDocument(document.DocumentId, document.Kind, document.Number, document.Unit,
                document.DocumentDate, "relay.json", document.Json);
            if (archive.FindDocument(document.DocumentId) is null) throw new IOException("Документ не збережено.");
            new MobileAccessStore(_context).AutoPost(document.Json);
            return true;
        }
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
        var format = parsed.RootElement.TryGetProperty("format", out var f) ? f.GetString() : "";
        var files = new PrivateAttachmentStore(_context);
        if (format == MavkaEncryptedFileEnvelope.Format)
        {
            using var file = MavkaEncryptedFileEnvelope.Decrypt(json, key => _keys.GetKey(key));
            var saved = files.Save(file); using var readback = files.Load(saved.Id);
            if (readback.Sha256 != file.Sha256) throw new IOException("Помилка збереження файла.");
            return true;
        }
        var scope = RelayProtocol.Hash("relay:" + _identity.DeviceId + ":" + _profile.ClientId);
        var transfers = new PendingFileTransferStore(Path.Combine(_context.FilesDir!.AbsolutePath,
            "relay_received_file_parts_v1", scope), TransferKeystoreCipher.Seal, TransferKeystoreCipher.Open);
        if (format == MavkaFileParts.Format)
        {
            var progress = transfers.Accept(json, key => _keys.GetKey(key), null);
            using var complete = transfers.Assemble(progress.Id);
            if (complete is not null) { var saved = files.Save(complete); transfers.MarkSaved(progress.Id, saved.Id); }
            return true;
        }
        if (DriveFileCommand.TryRead(json, out var command) && command is not null)
        {
            foreach (var packet in await command.DownloadAsync(cancellation))
            {
                var part = MavkaPasswordedEnvelope.IsWrapped(packet)
                    ? MavkaPasswordedEnvelope.Unwrap(packet, new DocumentPasswordStore(_context).Load() ?? throw new KeyNotFoundException("Потрібен пароль.")) : packet;
                var progress = transfers.Accept(part, key => _keys.GetKey(key), null, command.TransferId);
                using var complete = transfers.Assemble(progress.Id);
                if (complete is not null) { var saved = files.Save(complete); transfers.MarkSaved(progress.Id, saved.Id); }
            }
            return true;
        }
        return false; // Keep unknown or wrong-unit records locally; no direct ACK.
    }
    internal static string Receipt(UserProfile profile, IncomingInvoice invoice) => RelayProtocol.Serialize(new
    {
        format = "MAVKA_MOBILE_RECEIPT_V1", clientId = profile.ClientId, documentId = invoice.DocumentId,
        documentNumber = invoice.Number, unit = profile.Unit, fullName = profile.FullName,
        isMvo = profile.IsMvo, status = "RECEIVED", confirmedAtUtc = DateTimeOffset.UtcNow
    });
}
