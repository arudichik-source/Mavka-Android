using Android.Content;
using Android.Database.Sqlite;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

internal sealed record StoredNotice(
    MavkaNotice Notice, bool IsRead, DateTimeOffset? RemindUtc, bool Reminded);

/// <summary>
/// Durable, unit-scoped, Keystore-encrypted inbox. All content stays encrypted
/// in SQLite; a duplicate ID with different bytes is a hard integrity error.
/// Reminder state is persisted so Gmail re-fetch never schedules duplicates.
/// </summary>
internal sealed class MavkaNoticeStore(Context context)
    : SQLiteOpenHelper(context, "mavka_secure_notices_v1.db", null, 1)
{
    private const string Alias = "mavka_notices_aes_v1";

    public override void OnCreate(SQLiteDatabase db)
    {
        db.ExecSQL(@"CREATE TABLE notices(
            notice_id TEXT PRIMARY KEY NOT NULL,
            unit TEXT NOT NULL,
            content_sha TEXT NOT NULL,
            sealed_content TEXT NOT NULL,
            received_utc TEXT NOT NULL,
            is_read INTEGER NOT NULL DEFAULT 0,
            reminder_ms INTEGER NULL,
            reminded INTEGER NOT NULL DEFAULT 0)");
        db.ExecSQL("CREATE INDEX idx_notice_unit_received ON notices(unit,received_utc DESC)");
        db.ExecSQL("CREATE INDEX idx_notice_reminder ON notices(reminder_ms)");
    }

    public override void OnUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) =>
        throw new InvalidOperationException("Unsupported secure notice database migration.");

    public bool Add(MavkaNotice incoming, string originalAuthenticatedJson)
    {
        if (!MavkaNoticeProtocol.TryRead(originalAuthenticatedJson, incoming.Unit,
            DateTimeOffset.UtcNow, out var verified) || verified != incoming)
            throw new InvalidOperationException("Authenticated notice no longer matches its parsed content.");
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(originalAuthenticatedJson)));
        using var db = WritableDatabase;
        db.BeginTransaction();
        try
        {
            using (var existing = db.RawQuery(
                "SELECT content_sha FROM notices WHERE notice_id = ?",
                new[] { incoming.NoticeId }))
            {
                if (existing.MoveToFirst())
                {
                    if (existing.GetString(0) != hash)
                        throw new CryptographicException(
                            "Повторне повідомлення має інший зміст. Імпорт відхилено.");
                    db.SetTransactionSuccessful();
                    return false;
                }
            }
            var values = new ContentValues();
            values.Put("notice_id", incoming.NoticeId);
            values.Put("unit", incoming.Unit);
            values.Put("content_sha", hash);
            values.Put("sealed_content", Seal(originalAuthenticatedJson));
            values.Put("received_utc", DateTimeOffset.UtcNow.ToString("O"));
            values.Put("is_read", 0);
            values.Put("reminded", 0);
            db.InsertOrThrow("notices", null, values);
            db.SetTransactionSuccessful();
            return true;
        }
        finally { db.EndTransaction(); }
    }

    public StoredNotice? Find(string id, string unit)
    {
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT sealed_content,is_read,reminder_ms,reminded,content_sha
              FROM notices WHERE notice_id = ? AND unit = ? COLLATE NOCASE",
            new[] { id, unit });
        if (!cursor.MoveToFirst()) return null;
        var json = Unseal(cursor.GetString(0));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(hash), Convert.FromHexString(cursor.GetString(4))))
            throw new CryptographicException("Захищене повідомлення пошкоджене.");
        if (!MavkaNoticeProtocol.TryRead(json, unit,
            DateTimeOffset.UtcNow, out var notice,
            enforceFreshness: false) || notice is null)
            throw new CryptographicException("Не вдалося перевірити збережене повідомлення.");
        DateTimeOffset? remindAt = cursor.IsNull(2) ? null :
            DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(2));
        return new StoredNotice(notice, cursor.GetInt(1) != 0,
            remindAt, cursor.GetInt(3) != 0);
    }

    public IReadOnlyList<StoredNotice> List(string unit)
    {
        var ids = new List<string>();
        using (var db = ReadableDatabase)
        using (var cursor = db.RawQuery(
            "SELECT notice_id FROM notices WHERE unit = ? COLLATE NOCASE ORDER BY received_utc DESC LIMIT 250",
            new[] { unit.Trim() }))
            while (cursor.MoveToNext()) ids.Add(cursor.GetString(0));
        return ids.Select(id => Find(id, unit)!).ToList();
    }

    public int UnreadCount(string unit)
    {
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            "SELECT COUNT(*) FROM notices WHERE unit = ? COLLATE NOCASE AND is_read = 0",
            new[] { unit.Trim() });
        return cursor.MoveToFirst() ? cursor.GetInt(0) : 0;
    }

    public void MarkRead(string id, string unit)
    {
        using var db = WritableDatabase;
        var cv = new ContentValues();
        cv.Put("is_read", 1);
        if (db.Update("notices", cv, "notice_id = ? AND unit = ? COLLATE NOCASE",
                new[] { id, unit }) != 1)
            throw new InvalidOperationException("Повідомлення не знайдено.");
    }

    public void SetReminder(string id, string unit, DateTimeOffset? when)
    {
        if (when is not null && (when <= DateTimeOffset.UtcNow ||
            when > DateTimeOffset.UtcNow.AddYears(1)))
            throw new ArgumentException("Нагадування має бути у майбутньому, до одного року.");
        using var db = WritableDatabase;
        var cv = new ContentValues();
        if (when is null) cv.PutNull("reminder_ms");
        else cv.Put("reminder_ms", when.Value.ToUnixTimeMilliseconds());
        cv.Put("reminded", 0);
        if (db.Update("notices", cv, "notice_id = ? AND unit = ? COLLATE NOCASE",
                new[] { id, unit }) != 1)
            throw new InvalidOperationException("Повідомлення не знайдено.");
    }

    public bool ConsumeReminder(string id, DateTimeOffset now)
    {
        using var db = WritableDatabase;
        var cv = new ContentValues();
        cv.Put("reminded", 1);
        return db.Update("notices", cv,
            "notice_id = ? AND reminder_ms <= ? AND reminded = 0",
            new[] { id, now.ToUnixTimeMilliseconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture) }) == 1;
    }

    public IReadOnlyList<(string Id, DateTimeOffset At)> PendingReminders()
    {
        var list = new List<(string, DateTimeOffset)>();
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            "SELECT notice_id, reminder_ms FROM notices WHERE reminder_ms IS NOT NULL AND reminded = 0 LIMIT 250",
            null);
        while (cursor.MoveToNext())
            list.Add((cursor.GetString(0),
                DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(1))));
        return list;
    }

    private static string Seal(string text)
    {
        using var store = OpenStore();
        if (!store.ContainsAlias(Alias))
        {
            using var generator = KeyGenerator.GetInstance("AES", "AndroidKeyStore")!;
            using var spec = new KeyGenParameterSpec.Builder(
                Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes("GCM").SetEncryptionPaddings("NoPadding").Build();
            generator.Init(spec);
            generator.GenerateKey();
        }
        using var key = store.GetKey(Alias, null)!;
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, key);
        var iv = cipher.GetIV() ??
            throw new InvalidOperationException("Немає nonce повідомлення.");
        return Convert.ToBase64String(iv) + ":" +
            Convert.ToBase64String(cipher.DoFinal(Encoding.UTF8.GetBytes(text))!);
    }

    private static string Unseal(string contents)
    {
        var parts = contents.Split(':', 2);
        if (parts.Length != 2) throw new CryptographicException(
            "Захищене повідомлення пошкоджене.");
        using var store = OpenStore();
        using var key = store.GetKey(Alias, null) ??
            throw new CryptographicException("Ключ захищених повідомлень відсутній.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var spec = new GCMParameterSpec(128,
            Convert.FromBase64String(parts[0]));
        cipher.Init(CipherMode.DecryptMode, key, spec);
        return Encoding.UTF8.GetString(cipher.DoFinal(
            Convert.FromBase64String(parts[1]))!);
    }

    private static KeyStore OpenStore()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load((KeyStore.ILoadStoreParameter?)null);
        return store;
    }
}
