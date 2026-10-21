using Android.Content;
using Android.Database.Sqlite;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Security.Cryptography;
using System.Text;

namespace Mavka.Mobile;

internal sealed record ArchivedDocumentSummary(
    string DocumentId, string Kind, string Number, string Unit, string DocumentDate,
    string SavedUtc, string Status,
    string PostingStatus, string DeliveryStatus, string SyncStatus,
    string? DeletedUtc = null);

internal sealed record MailSyncEvent(string Utc, string State, string Detail);

internal sealed record ArchivedDocument(
    string DocumentId, string Kind, string Number, string Unit, string DocumentDate,
    string ReceivedUtc, string SavedUtc, string Status, string ContentSha256,
    string FileName, string Json,
    string PostingStatus, string DeliveryStatus, string SyncStatus,
    string? DeletedUtc = null);

/// <summary>
/// Independent on-device archive. A Gmail message may be moved only after every
/// relevant attachment is committed and can be read back from this store.
/// Saving is separate from ledger posting; the access policy determines whether
/// the authenticated document is posted automatically or awaits the MVO's action.
/// </summary>
internal sealed class DocumentArchiveStore : SQLiteOpenHelper
{
    private const string KeyAlias = "mavka_document_archive_aes_v1";

    public DocumentArchiveStore(Context context)
        : base(context, "mavka_document_archive_v1.db", null, 3) { }

    public override void OnCreate(SQLiteDatabase db)
    {
        db.ExecSQL(@"CREATE TABLE documents(
            document_id TEXT PRIMARY KEY NOT NULL,
            kind TEXT NOT NULL,
            number TEXT NOT NULL,
            unit TEXT NOT NULL,
            document_date TEXT NOT NULL,
            received_utc TEXT NOT NULL,
            saved_utc TEXT NOT NULL,
            status TEXT NOT NULL,
            content_sha256 TEXT NOT NULL,
            file_name TEXT NOT NULL,
            sealed_json TEXT NOT NULL,
            posting_status TEXT NOT NULL DEFAULT 'not_posted',
            delivery_status TEXT NOT NULL DEFAULT 'not_queued',
            sync_status TEXT NOT NULL DEFAULT 'not_synced',
            deleted_utc TEXT NULL)");
        db.ExecSQL("CREATE INDEX idx_document_unit_date ON documents(unit, document_date)");
        CreateStatusTable(db);
        CreateMailProcessingTable(db);
        CreateSyncJournal(db);
        db.ExecSQL("CREATE INDEX IF NOT EXISTS idx_document_trash ON documents(unit,deleted_utc)");
    }

    public override void OnUpgrade(SQLiteDatabase db, int oldVersion, int newVersion)
    {
        if (oldVersion is < 1 or > 2 || newVersion != 3)
            throw new InvalidOperationException("Unsupported document archive migration.");
        // v1 -> v2 -> v3. Never drop/recreate archived encrypted records.
        if (oldVersion == 1)
        {
            db.ExecSQL("ALTER TABLE documents ADD COLUMN posting_status TEXT NOT NULL DEFAULT 'not_posted'");
            db.ExecSQL("ALTER TABLE documents ADD COLUMN delivery_status TEXT NOT NULL DEFAULT 'not_queued'");
            db.ExecSQL("ALTER TABLE documents ADD COLUMN sync_status TEXT NOT NULL DEFAULT 'not_synced'");
            CreateStatusTable(db);
            db.ExecSQL(@"INSERT INTO document_status_history
                 (document_id, domain, state, changed_utc)
                 SELECT document_id, 'archive', status, saved_utc FROM documents");
            CreateMailProcessingTable(db);
        }
        db.ExecSQL("ALTER TABLE documents ADD COLUMN deleted_utc TEXT NULL");
        db.ExecSQL("CREATE INDEX IF NOT EXISTS idx_document_trash ON documents(unit,deleted_utc)");
        CreateMailProcessingTable(db);
        CreateSyncJournal(db);
    }

    private static void CreateMailProcessingTable(SQLiteDatabase db)
    {
        db.ExecSQL(@"CREATE TABLE IF NOT EXISTS mail_processing(
            uid_validity TEXT NOT NULL,
            uid TEXT NOT NULL,
            authorized_sender TEXT NOT NULL,
            status TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            PRIMARY KEY(uid_validity, uid, authorized_sender))");
    }

    private static void CreateSyncJournal(SQLiteDatabase db)
    {
        db.ExecSQL(@"CREATE TABLE IF NOT EXISTS sync_journal (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            changed_utc TEXT NOT NULL,
            state TEXT NOT NULL,
            detail TEXT NOT NULL)");
        db.ExecSQL("CREATE INDEX IF NOT EXISTS idx_sync_journal_id ON sync_journal(id DESC)");
    }

    public void RecordSyncEvent(string state, string detail)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Length > 50 ||
            string.IsNullOrWhiteSpace(detail) || detail.Length > 280)
            throw new ArgumentException("Invalid sync event.");
        using var db = WritableDatabase;
        var data = new ContentValues();
        data.Put("changed_utc", DateTimeOffset.UtcNow.ToString("O"));
        data.Put("state", state);
        data.Put("detail", detail);
        db.InsertOrThrow("sync_journal", null, data);
        // Retain only last 500 diagnostics; never store message bodies,
        // identities, passwords, key IDs or URLs in the event detail.
        db.ExecSQL(@"DELETE FROM sync_journal WHERE id NOT IN
            (SELECT id FROM sync_journal ORDER BY id DESC LIMIT 500)");
    }

    public IReadOnlyList<MailSyncEvent> ListSyncEvents(int maximum = 100)
    {
        var entries = new List<MailSyncEvent>();
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            "SELECT changed_utc,state,detail FROM sync_journal ORDER BY id DESC LIMIT ?",
            new[] { Math.Clamp(maximum, 1, 200).ToString() });
        while (cursor.MoveToNext())
            entries.Add(new MailSyncEvent(cursor.GetString(0),
                cursor.GetString(1), cursor.GetString(2)));
        return entries;
    }

    private static void CreateStatusTable(SQLiteDatabase db)
    {
        db.ExecSQL(@"CREATE TABLE document_status_history(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            document_id TEXT NOT NULL,
            domain TEXT NOT NULL,
            state TEXT NOT NULL,
            changed_utc TEXT NOT NULL)");
        db.ExecSQL("CREATE INDEX idx_document_status_id ON document_status_history(document_id,id)");
    }

    private void UpdateStatus(string documentId, string column, string state)
    {
        if (column is not ("posting_status" or "delivery_status" or "sync_status"))
            throw new ArgumentException("Invalid status field.");
        if (string.IsNullOrWhiteSpace(documentId) || state.Length > 64 ||
            string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("Invalid document status.");

        using var db = WritableDatabase;
        db.BeginTransaction();
        try
        {
            using var previous = db.RawQuery(
                "SELECT " + column + " FROM documents WHERE document_id = ?",
                new[] { documentId });
            if (!previous.MoveToFirst())
                throw new InvalidOperationException("Document not archived.");
            if (!string.Equals(previous.GetString(0), state, StringComparison.Ordinal))
            {
                var update = new ContentValues();
                update.Put(column, state);
                if (db.Update("documents", update, "document_id = ?", new[] { documentId }) != 1)
                    throw new InvalidOperationException("Cannot change archive status.");
                var eventData = new ContentValues();
                eventData.Put("document_id", documentId);
                eventData.Put("domain", column);
                eventData.Put("state", state);
                eventData.Put("changed_utc", DateTimeOffset.UtcNow.ToString("O"));
                db.InsertOrThrow("document_status_history", null, eventData);
            }
            db.SetTransactionSuccessful();
        }
        finally { db.EndTransaction(); }
    }

    /// <summary>
    /// After an explicitly confirmed local-ledger reset, reconcile the
    /// archive's *local* posting labels without deleting evidentiary documents
    /// or altering delivery/remote sync status. A second call is idempotent.
    /// </summary>
    public int ResetLocalPostingForUnit(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
            throw new ArgumentException("Підрозділ не визначено.", nameof(unit));
        using var db = WritableDatabase;
        db.BeginTransaction();
        try
        {
            var ids = new List<string>();
            using (var cursor = db.RawQuery(
                @"SELECT document_id FROM documents
                  WHERE unit = ? COLLATE NOCASE
                  AND posting_status = 'posted'",
                new[] { unit.Trim() }))
                while (cursor.MoveToNext()) ids.Add(cursor.GetString(0));
            var when = DateTimeOffset.UtcNow.ToString("O");
            foreach (var id in ids)
            {
                var update = new ContentValues();
                update.Put("posting_status", "not_posted");
                if (db.Update("documents", update, "document_id = ?",
                        new[] { id }) != 1)
                    throw new InvalidOperationException(
                        "Не вдалося оновити статус локального проведення.");
                var history = new ContentValues();
                history.Put("document_id", id);
                history.Put("domain", "posting_status");
                history.Put("state", "not_posted");
                history.Put("changed_utc", when);
                db.InsertOrThrow("document_status_history", null, history);
            }
            db.SetTransactionSuccessful();
            return ids.Count;
        }
        finally { db.EndTransaction(); }
    }

    public void MoveToTrash(string documentId, string unit)
    {
        if (string.IsNullOrWhiteSpace(unit) || string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Вкажи документ і підрозділ.");
        using var db = WritableDatabase;
        db.BeginTransaction();
        try
        {
            using var cursor = db.RawQuery(
                "SELECT posting_status, deleted_utc FROM documents WHERE document_id = ? AND unit = ? COLLATE NOCASE",
                new[] { documentId, unit.Trim() });
            if (!cursor.MoveToFirst()) throw new InvalidOperationException("Документ не знайдено.");
            if (!cursor.IsNull(1)) throw new InvalidOperationException("Документ уже в кошику.");
            if (!ArchiveTrashPolicy.CanMoveToTrash(cursor.GetString(0)))
                throw new InvalidOperationException(
                    "Проведений документ не можна видаляти з архіву. Спочатку потрібна окрема облікова операція скасування.");
            var now = DateTimeOffset.UtcNow.ToString("O");
            var values = new ContentValues();
            values.Put("deleted_utc", now);
            if (db.Update("documents", values,
                "document_id = ? AND unit = ? COLLATE NOCASE AND deleted_utc IS NULL",
                new[] { documentId, unit.Trim() }) != 1)
                throw new InvalidOperationException("Документ не переміщено до кошика.");
            InsertArchiveEvent(db, documentId, "trash", "moved", now);
            db.SetTransactionSuccessful();
        }
        finally { db.EndTransaction(); }
    }

    public void RestoreFromTrash(string documentId, string unit)
    {
        using var db = WritableDatabase;
        db.BeginTransaction();
        try
        {
            using var cursor = db.RawQuery(
                "SELECT deleted_utc FROM documents WHERE document_id = ? AND unit = ? COLLATE NOCASE",
                new[] { documentId, unit.Trim() });
            if (!cursor.MoveToFirst() || cursor.IsNull(0) ||
                !ArchiveTrashPolicy.CanRestore(cursor.GetString(0), DateTimeOffset.UtcNow))
                throw new InvalidOperationException("Строк відновлення документа минув або документ відсутній.");
            var values = new ContentValues();
            values.PutNull("deleted_utc");
            if (db.Update("documents", values, "document_id = ? AND unit = ? COLLATE NOCASE",
                new[] { documentId, unit.Trim() }) != 1)
                throw new InvalidOperationException("Не вдалося відновити документ.");
            InsertArchiveEvent(db, documentId, "trash", "restored", DateTimeOffset.UtcNow.ToString("O"));
            db.SetTransactionSuccessful();
        }
        finally { db.EndTransaction(); }
    }

    public int PurgeExpiredTrash(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Підрозділ не визначено.");
        using var db = WritableDatabase;
        db.BeginTransaction();
        try
        {
            var expired = new List<string>();
            using (var cursor = db.RawQuery(
                @"SELECT document_id, deleted_utc FROM documents
                  WHERE unit = ? COLLATE NOCASE AND deleted_utc IS NOT NULL
                    AND posting_status <> 'posted'",
                new[] { unit.Trim() }))
                while (cursor.MoveToNext())
                    if (ArchiveTrashPolicy.CanPurge(cursor.GetString(1), DateTimeOffset.UtcNow))
                        expired.Add(cursor.GetString(0));
            foreach (var id in expired)
            {
                db.Delete("documents", "document_id = ?", new[] { id });
                db.Delete("document_status_history", "document_id = ?", new[] { id });
            }
            db.SetTransactionSuccessful();
            return expired.Count;
        }
        finally { db.EndTransaction(); }
    }

    private static void InsertArchiveEvent(SQLiteDatabase db, string documentId,
        string domain, string state, string when)
    {
        var ev = new ContentValues();
        ev.Put("document_id", documentId);
        ev.Put("domain", domain);
        ev.Put("state", state);
        ev.Put("changed_utc", when);
        db.InsertOrThrow("document_status_history", null, ev);
    }

    public IReadOnlyList<ArchivedDocumentSummary> ListTrash(string unit)
    {
        var results = new List<ArchivedDocumentSummary>();
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT document_id, kind, number, unit, document_date, saved_utc,
                     status, posting_status, delivery_status, sync_status, deleted_utc
              FROM documents WHERE unit = ? COLLATE NOCASE AND deleted_utc IS NOT NULL
              ORDER BY deleted_utc DESC LIMIT 1000", new[] { unit.Trim() });
        while (cursor.MoveToNext())
            results.Add(ReadSummary(cursor));
        return results;
    }

    public void MarkSuperseded(string documentId) => UpdateStatus(documentId,"posting_status","superseded");
    public void MarkPosted(string documentId) => UpdateStatus(documentId, "posting_status", "posted");
    public void MarkDeliveryQueued(string documentId) => UpdateStatus(documentId, "delivery_status", "pending");
    public void MarkDeliverySent(string documentId) => UpdateStatus(documentId, "delivery_status", "sent");

    public IReadOnlyList<string> ListStatusHistory(string documentId)
    {
        var changes = new List<string>();
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT domain, state, changed_utc FROM document_status_history
              WHERE document_id = ? ORDER BY id",
            new[] { documentId });
        while (cursor.MoveToNext())
            changes.Add(cursor.GetString(2) + " · " + cursor.GetString(0) +
                " → " + cursor.GetString(1));
        return changes;
    }

    public bool SaveDocument(string documentId, string kind, string number,
        string unit, string documentDate, string fileName, string json)
    {
        if (string.IsNullOrWhiteSpace(documentId) || documentId.Length > 240 ||
            string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(unit) ||
            string.IsNullOrWhiteSpace(json) || json.Length > 1_000_000)
            throw new ArgumentException("Invalid archive document.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        using (var db = WritableDatabase)
        {
            db.BeginTransaction();
            try
            {
                using (var previous = db.RawQuery(
                    "SELECT content_sha256 FROM documents WHERE document_id = ?",
                    new[] { documentId }))
                {
                    if (previous.MoveToFirst())
                    {
                        if (!string.Equals(previous.GetString(0), hash, StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                "Document ID collision: different content; no overwrite allowed.");
                        db.SetTransactionSuccessful();
                        return false;
                    }
                }
                var values = new ContentValues();
                var now = DateTimeOffset.UtcNow.ToString("O");
                values.Put("document_id", documentId);
                values.Put("kind", kind);
                values.Put("number", number);
                values.Put("unit", unit);
                values.Put("document_date", documentDate);
                values.Put("received_utc", now);
                values.Put("saved_utc", now);
                values.Put("status", "saved");
                values.Put("content_sha256", hash);
                values.Put("file_name", Path.GetFileName(fileName));
                values.Put("sealed_json", Seal(json));
                db.InsertOrThrow("documents", null, values);
                var eventData = new ContentValues();
                eventData.Put("document_id", documentId);
                eventData.Put("domain", "archive");
                eventData.Put("state", "saved");
                eventData.Put("changed_utc", now);
                db.InsertOrThrow("document_status_history", null, eventData);
                db.SetTransactionSuccessful();
            }
            finally { db.EndTransaction(); }
        }
        // Do not authorize Gmail cleanup without a successful decrypt/read-back.
        var saved = FindDocument(documentId) ??
            throw new InvalidOperationException("Cannot read back saved archive document.");
        if (!string.Equals(saved.ContentSha256, hash, StringComparison.Ordinal))
            throw new InvalidOperationException("Archive content verification failed.");
        return true;
    }

    public ArchivedDocument? FindDocument(string documentId)
    {
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT document_id, kind, number, unit, document_date,
                     received_utc, saved_utc, status, content_sha256, file_name, sealed_json,
                     posting_status, delivery_status, sync_status
              FROM documents WHERE document_id = ?", new[] { documentId });
        return cursor.MoveToFirst() ? Read(cursor) : null;
    }

    public int CountDocuments(string unit)
    {
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            "SELECT COUNT(*) FROM documents WHERE unit = ? COLLATE NOCASE AND deleted_utc IS NULL", new[] { unit });
        return cursor.MoveToFirst() ? cursor.GetInt(0) : 0;
    }

    // Keep the processed-mail index: old Gmail deliveries must not resurrect
    // intentionally discarded documents after clearing the account's content.
    internal void ClearAllForAccountReset() => LocalAccountReset.ClearTables(WritableDatabase,
        "document_status_history","documents","sync_journal");

    public IReadOnlyList<ArchivedDocumentSummary> ListSummaries(string unit)
    {
        var results = new List<ArchivedDocumentSummary>();
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT document_id, kind, number, unit, document_date, saved_utc, status,
                     posting_status, delivery_status, sync_status, deleted_utc
              FROM documents WHERE unit = ? COLLATE NOCASE AND deleted_utc IS NULL
              ORDER BY saved_utc DESC, document_id DESC", new[] { unit });
        while (cursor.MoveToNext()) results.Add(ReadSummary(cursor));
        return results;
    }

    public IReadOnlyList<ArchivedDocument> ListDocuments(string unit)
    {
        var results = new List<ArchivedDocument>();
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT document_id, kind, number, unit, document_date,
                     received_utc, saved_utc, status, content_sha256, file_name, sealed_json,
                     posting_status, delivery_status, sync_status
              FROM documents WHERE unit = ? COLLATE NOCASE AND deleted_utc IS NULL
              ORDER BY saved_utc DESC LIMIT 1000", new[] { unit });
        while (cursor.MoveToNext()) results.Add(Read(cursor));
        return results;
    }

    private static ArchivedDocumentSummary ReadSummary(Android.Database.ICursor cursor) =>
        new(cursor.GetString(0), cursor.GetString(1), cursor.GetString(2),
            cursor.GetString(3), cursor.GetString(4), cursor.GetString(5),
            cursor.GetString(6), cursor.GetString(7), cursor.GetString(8),
            cursor.GetString(9), cursor.IsNull(10) ? null : cursor.GetString(10));

    private static ArchivedDocument Read(Android.Database.ICursor cursor)
    {
        var json = Unseal(cursor.GetString(10));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        if (!string.Equals(hash, cursor.GetString(8), StringComparison.Ordinal))
            throw new InvalidOperationException("Archived document integrity check failed.");
        return new ArchivedDocument(cursor.GetString(0), cursor.GetString(1),
            cursor.GetString(2), cursor.GetString(3), cursor.GetString(4),
            cursor.GetString(5), cursor.GetString(6), cursor.GetString(7),
            cursor.GetString(8), cursor.GetString(9), json,
            cursor.GetString(11), cursor.GetString(12), cursor.GetString(13));
    }

    // A durable marker means every relevant part of the mail was saved, or a key
    // package was installed and independently verified; NOT that it was posted.
    public void MarkMailReady(uint uidValidity, uint uid, string sender)
        => SetMailStatus(uidValidity, uid, sender, "ready");

    public void MarkMailTrashed(uint uidValidity, uint uid, string sender)
        => SetMailStatus(uidValidity, uid, sender, "trashed");

    public bool IsMailReady(uint uidValidity, uint uid, string sender)
    {
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT status FROM mail_processing
              WHERE uid_validity = ? AND uid = ? AND authorized_sender = ?",
            new[] { uidValidity.ToString(), uid.ToString(), sender.ToLowerInvariant() });
        return cursor.MoveToFirst() && cursor.GetString(0) is "ready" or "trashed";
    }

    /// <summary>
    /// Read the durable processed-mail index once per IMAP refresh. Statuses
    /// are scoped to UIDVALIDITY and exact sender; an incomplete/missing mail
    /// has no marker and must still be fetched.
    /// </summary>
    public IReadOnlyDictionary<uint, string> ListMailProcessingStates(uint uidValidity, string sender)
    {
        var result = new Dictionary<uint, string>();
        if (uidValidity == 0 || string.IsNullOrWhiteSpace(sender)) return result;
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT uid, status FROM mail_processing
              WHERE uid_validity = ? AND authorized_sender = ?",
            new[] { uidValidity.ToString(), sender.Trim().ToLowerInvariant() });
        while (cursor.MoveToNext())
        {
            if (uint.TryParse(cursor.GetString(0), out uint uid) && uid != 0)
                result[uid] = cursor.GetString(1);
        }
        return result;
    }

    private void SetMailStatus(uint validity, uint uid, string sender, string state)
    {
        if (validity == 0 || uid == 0 || string.IsNullOrWhiteSpace(sender))
            throw new ArgumentException("Missing mail identity.");
        using var db = WritableDatabase;
        var values = new ContentValues();
        values.Put("uid_validity", validity.ToString());
        values.Put("uid", uid.ToString());
        values.Put("authorized_sender", sender.ToLowerInvariant());
        values.Put("status", state);
        values.Put("updated_utc", DateTimeOffset.UtcNow.ToString("O"));
        db.InsertWithOnConflict("mail_processing", null, values,
            Android.Database.Sqlite.Conflict.Replace);
    }

    private static string Seal(string text)
    {
        using var store = OpenKeyStore();
        if (!store.ContainsAlias(KeyAlias))
        {
            using var generator = KeyGenerator.GetInstance("AES", "AndroidKeyStore")!;
            using var spec = new KeyGenParameterSpec.Builder(
                KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes("GCM").SetEncryptionPaddings("NoPadding").Build();
            generator.Init(spec);
            generator.GenerateKey();
        }
        using var key = store.GetKey(KeyAlias, null)!;
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key);
        var iv = cipher.GetIV() ?? throw new InvalidOperationException("Missing archive IV.");
        var bytes = cipher.DoFinal(Encoding.UTF8.GetBytes(text))!;
        return Convert.ToBase64String(iv) + ":" + Convert.ToBase64String(bytes);
    }

    private static string Unseal(string stored)
    {
        var parts = stored.Split(':', 2);
        if (parts.Length != 2) throw new InvalidOperationException("Invalid sealed document.");
        using var store = OpenKeyStore();
        using var key = store.GetKey(KeyAlias, null) ??
            throw new InvalidOperationException("Document archive Keystore key missing.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var spec = new GCMParameterSpec(128, Convert.FromBase64String(parts[0]));
        cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key, spec);
        return Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(parts[1]))!);
    }

    private static KeyStore OpenKeyStore()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load((KeyStore.ILoadStoreParameter?)null);
        return store;
    }
}
