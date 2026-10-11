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
    string SavedUtc, string Status);

internal sealed record ArchivedDocument(
    string DocumentId, string Kind, string Number, string Unit, string DocumentDate,
    string ReceivedUtc, string SavedUtc, string Status, string ContentSha256,
    string FileName, string Json);

/// <summary>
/// Independent on-device archive. A Gmail message may be moved only after every
/// relevant attachment is committed and can be read back from this store.
/// Ledger posting is deliberately separate and never happens during download.
/// </summary>
internal sealed class DocumentArchiveStore : SQLiteOpenHelper
{
    private const string KeyAlias = "mavka_document_archive_aes_v1";

    public DocumentArchiveStore(Context context)
        : base(context, "mavka_document_archive_v1.db", null, 1) { }

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
            sealed_json TEXT NOT NULL)");
        db.ExecSQL("CREATE INDEX idx_document_unit_date ON documents(unit, document_date)");
        db.ExecSQL(@"CREATE TABLE mail_processing(
            uid_validity TEXT NOT NULL,
            uid TEXT NOT NULL,
            authorized_sender TEXT NOT NULL,
            status TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            PRIMARY KEY(uid_validity, uid, authorized_sender))");
    }

    public override void OnUpgrade(SQLiteDatabase db, int oldVersion, int newVersion)
        => throw new InvalidOperationException("Unsupported document archive migration.");

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
                     received_utc, saved_utc, status, content_sha256, file_name, sealed_json
              FROM documents WHERE document_id = ?", new[] { documentId });
        return cursor.MoveToFirst() ? Read(cursor) : null;
    }

    public int CountDocuments(string unit)
    {
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            "SELECT COUNT(*) FROM documents WHERE unit = ? COLLATE NOCASE", new[] { unit });
        return cursor.MoveToFirst() ? cursor.GetInt(0) : 0;
    }

    public IReadOnlyList<ArchivedDocumentSummary> ListSummaries(string unit)
    {
        var results = new List<ArchivedDocumentSummary>();
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT document_id, kind, number, unit, document_date, saved_utc, status
              FROM documents WHERE unit = ? COLLATE NOCASE
              ORDER BY saved_utc DESC LIMIT 500", new[] { unit });
        while (cursor.MoveToNext())
            results.Add(new ArchivedDocumentSummary(cursor.GetString(0), cursor.GetString(1),
                cursor.GetString(2), cursor.GetString(3), cursor.GetString(4),
                cursor.GetString(5), cursor.GetString(6)));
        return results;
    }

    public IReadOnlyList<ArchivedDocument> ListDocuments(string unit)
    {
        var results = new List<ArchivedDocument>();
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT document_id, kind, number, unit, document_date,
                     received_utc, saved_utc, status, content_sha256, file_name, sealed_json
              FROM documents WHERE unit = ? COLLATE NOCASE
              ORDER BY saved_utc DESC LIMIT 1000", new[] { unit });
        while (cursor.MoveToNext()) results.Add(Read(cursor));
        return results;
    }

    private static ArchivedDocument Read(Android.Database.ICursor cursor)
    {
        var json = Unseal(cursor.GetString(10));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        if (!string.Equals(hash, cursor.GetString(8), StringComparison.Ordinal))
            throw new InvalidOperationException("Archived document integrity check failed.");
        return new ArchivedDocument(cursor.GetString(0), cursor.GetString(1),
            cursor.GetString(2), cursor.GetString(3), cursor.GetString(4),
            cursor.GetString(5), cursor.GetString(6), cursor.GetString(7),
            cursor.GetString(8), cursor.GetString(9), json);
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
