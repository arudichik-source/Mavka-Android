using Android.Content;
using Android.Database.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;
internal sealed record RelayInboxRow(string Id, string Hash, long Expires, string Plain, bool Acked);
internal sealed record RelayOutboxRow(string Id, string Body, string Group, bool Submitted);
internal sealed class RelayStore(Context context, string owner) : SQLiteOpenHelper(context, "mavka_relay_v1.db", null, 1)
{
    private sealed record Sealed(string owner, string id, string hash, string text);
    public override void OnCreate(SQLiteDatabase db)
    {
        db.ExecSQL("CREATE TABLE inbox(owner TEXT NOT NULL,id TEXT NOT NULL,hash TEXT NOT NULL,expires INTEGER NOT NULL,sealed TEXT NOT NULL,acked INTEGER NOT NULL DEFAULT 0,imported INTEGER NOT NULL DEFAULT 0,created INTEGER NOT NULL,PRIMARY KEY(owner,id))");
        db.ExecSQL("CREATE TABLE outbox(owner TEXT NOT NULL,id TEXT NOT NULL,group_id TEXT NOT NULL,hash TEXT NOT NULL,sealed TEXT NOT NULL,submitted INTEGER NOT NULL DEFAULT 0,done INTEGER NOT NULL DEFAULT 0,last_attempt INTEGER NOT NULL DEFAULT 0,created INTEGER NOT NULL,PRIMARY KEY(owner,id))");
        db.ExecSQL("CREATE INDEX outbox_pending ON outbox(owner,done,submitted,last_attempt)");
    }
    public override void OnUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) =>
        throw new InvalidOperationException("Непідтримувана версія локальної Relay-бази.");
    private string Seal(string id, string hash, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(RelayProtocol.Serialize(new Sealed(owner, id, hash, text)));
        try { return Convert.ToBase64String(TransferKeystoreCipher.Seal(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private string Open(string id, string hash, string sealedText)
    {
        var plain = TransferKeystoreCipher.Open(Convert.FromBase64String(sealedText));
        try
        {
            var value = JsonSerializer.Deserialize<Sealed>(plain) ?? throw new CryptographicException("Пошкоджена локальна черга.");
            if (value.owner != owner || value.id != id || value.hash != hash)
                throw new CryptographicException("Прив'язка локального пакета не збігається.");
            return value.text;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static void Quota(SQLiteDatabase db)
    {
        using var c = db.RawQuery("SELECT COALESCE((SELECT SUM(LENGTH(sealed)) FROM inbox),0)+COALESCE((SELECT SUM(LENGTH(sealed)) FROM outbox WHERE done=0),0)", null);
        if (!c.MoveToFirst() || c.GetLong(0) > 32_000_000) throw new IOException("Локальна черга Relay заповнена (32 МБ).");
    }
    public void SaveIncoming(RelayVerified packet, string plain)
    {
        using var doc = JsonDocument.Parse(plain, new JsonDocumentOptions { MaxDepth = 14 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new CryptographicException("Потрібен JSON-пакет.");
        _ = RelayProtocol.Part(plain); // Reject malformed transport chunks before ACK.
        using (var db = WritableDatabase)
        {
            db.BeginTransaction();
            try
            {
                using var c = db.RawQuery("SELECT hash,sealed FROM inbox WHERE owner=? AND id=?", new[] { owner, packet.Id });
                if (c.MoveToFirst())
                {
                    if (c.GetString(0) != packet.Hash || Open(packet.Id, packet.Hash, c.GetString(1)!) != plain)
                        throw new CryptographicException("Конфлікт отриманого пакета.");
                }
                else
                {
                    Quota(db);
                    var v = new ContentValues(); v.Put("owner", owner); v.Put("id", packet.Id);
                    v.Put("hash", packet.Hash); v.Put("expires", packet.ExpiresAt);
                    v.Put("sealed", Seal(packet.Id, packet.Hash, plain)); v.Put("created", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    db.InsertOrThrow("inbox", null, v); Quota(db);
                }
                db.SetTransactionSuccessful();
            }
            finally { db.EndTransaction(); }
        }
        // A successful authenticated read-back is required before network ACK.
        using var verifyDb = ReadableDatabase;
        using var verified = verifyDb.RawQuery("SELECT hash,sealed FROM inbox WHERE owner=? AND id=?", new[] { owner, packet.Id });
        if (!verified.MoveToFirst() || verified.GetString(0) != packet.Hash ||
            Open(packet.Id, packet.Hash, verified.GetString(1)!) != plain)
            throw new IOException("Не вдалося повторно прочитати збережений пакет.");
    }
    public IReadOnlyList<RelayInboxRow> Inbox(bool pendingOnly = true)
    {
        using var db = ReadableDatabase;
        using var c = db.RawQuery("SELECT id,hash,expires,sealed,acked FROM inbox WHERE owner=?" +
            (pendingOnly ? " AND imported=0" : " AND acked=0") + " ORDER BY created,id LIMIT 1200", new[] { owner });
        var result = new List<RelayInboxRow>();
        while (c.MoveToNext())
        {
            var id = c.GetString(0)!; var hash = c.GetString(1)!;
            result.Add(new(id, hash, c.GetLong(2), Open(id, hash, c.GetString(3)!), c.GetInt(4) != 0));
        }
        return result;
    }
    public bool IsImported(string id)
    {
        using var db = ReadableDatabase;
        using var c = db.RawQuery("SELECT imported FROM inbox WHERE owner=? AND id=?", new[] { owner, id });
        return c.MoveToFirst() && c.GetInt(0) != 0;
    }
    public void Acked(string id) { using var db = WritableDatabase; var v = new ContentValues(); v.Put("acked", 1); db.Update("inbox", v, "owner=? AND id=?", new[] { owner, id }); }
    internal void DiscardDocumentsForAccountReset(string? keepBootstrapGroup)
    {
        using var db=WritableDatabase;db.BeginTransaction();
        try
        {
            // Retain authenticated transport evidence until its normal ACK cleanup,
            // but never import intentionally discarded local packages again.
            var imported=new ContentValues();imported.Put("imported",1);db.Update("inbox",imported,null,null);
            if(keepBootstrapGroup is null)db.Delete("outbox",null,null);
            else db.Delete("outbox","group_id<>?",new[]{keepBootstrapGroup});
            db.SetTransactionSuccessful();
        }
        finally{db.EndTransaction();}
    }
    public void Imported(IEnumerable<string> ids)
    {
        using var db = WritableDatabase; db.BeginTransaction();
        try
        {
            var v = new ContentValues(); v.Put("imported", 1);
            foreach (var id in ids) db.Update("inbox", v, "owner=? AND id=?", new[] { owner, id });
            db.SetTransactionSuccessful();
        }
        finally { db.EndTransaction(); }
    }
    public void Queue(string group, IReadOnlyList<(string Id, string Body)> packets)
    {
        using var db = WritableDatabase; db.BeginTransaction();
        try
        {
            using var previous = db.RawQuery("SELECT 1 FROM outbox WHERE owner=? AND group_id=? LIMIT 1", new[] { owner, group });
            if (!previous.MoveToFirst())
            {
                Quota(db);
                foreach (var packet in packets)
                {
                    var hash = RelayProtocol.Hash(packet.Body); var v = new ContentValues();
                    v.Put("owner", owner); v.Put("id", packet.Id); v.Put("group_id", group);
                    v.Put("hash", hash); v.Put("sealed", Seal(packet.Id, hash, packet.Body));
                    v.Put("created", DateTimeOffset.UtcNow.ToUnixTimeSeconds()); db.InsertOrThrow("outbox", null, v);
                }
                Quota(db);
            }
            db.SetTransactionSuccessful();
        }
        finally { db.EndTransaction(); }
    }
    public bool HasGroup(string group)
    {
        using var db = ReadableDatabase;
        using var c = db.RawQuery("SELECT 1 FROM outbox WHERE owner=? AND group_id=? LIMIT 1", new[] { owner, group });
        return c.MoveToFirst();
    }
    public bool Submitted(string group)
    {
        using var db = ReadableDatabase;
        using var c = db.RawQuery("SELECT COUNT(*),SUM(submitted) FROM outbox WHERE owner=? AND group_id=?", new[] { owner, group });
        return c.MoveToFirst() && c.GetInt(0) > 0 && c.GetInt(0) == c.GetInt(1);
    }
    public IReadOnlyList<RelayOutboxRow> Outbox()
    {
        using var db = ReadableDatabase;
        var before = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60).ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var c = db.RawQuery("SELECT id,group_id,hash,sealed,submitted FROM outbox WHERE owner=? AND done=0 AND last_attempt<? ORDER BY submitted,last_attempt,created LIMIT 20", new[] { owner, before });
        var result = new List<RelayOutboxRow>();
        while (c.MoveToNext())
        {
            var id = c.GetString(0)!;
            result.Add(new(id, Open(id, c.GetString(2)!, c.GetString(3)!), c.GetString(1)!, c.GetInt(4) != 0));
        }
        return result;
    }
    public void Sent(string id, bool delivered)
    {
        using var db = WritableDatabase; var v = new ContentValues();
        v.Put("submitted", 1); v.Put("done", delivered ? 1 : 0);
        v.Put("last_attempt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        // Keep a compact idempotency tombstone after recipient delivery.
        if (delivered) v.Put("sealed", "");
        db.Update("outbox", v, "owner=? AND id=?", new[] { owner, id });
    }
    public (int Incoming, int Waiting, int Server) Counts()
    {
        using var db = ReadableDatabase;
        int Count(string sql) { using var c = db.RawQuery(sql, new[] { owner }); return c.MoveToFirst() ? c.GetInt(0) : 0; }
        return (Count("SELECT COUNT(*) FROM inbox WHERE owner=? AND imported=0"),
            Count("SELECT COUNT(*) FROM outbox WHERE owner=? AND submitted=0 AND done=0"),
            Count("SELECT COUNT(*) FROM outbox WHERE owner=? AND submitted=1 AND done=0"));
    }
    public void Cleanup()
    {
        using var db = WritableDatabase;
        db.Delete("inbox", "owner=? AND imported=1 AND acked=1", new[] { owner });
    }
}
