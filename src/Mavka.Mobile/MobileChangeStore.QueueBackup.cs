using Android.Database.Sqlite;
using Android.Content;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Mavka.Mobile;
internal sealed partial class MobileChangeStore
{
    private sealed record QueueHead(string Source,long Sequence,string Hash);
    private sealed record QueueProof(string Client,DateTimeOffset At,string Sha256,int Parts,QueueHead[] Heads);
    internal string QueueNotice=>context.GetSharedPreferences("mavka_queue_maintenance_v1",FileCreationMode.Private)!.GetString("notice","")??"";
    private void SaveQueueNotice(string value){using var editor=context.GetSharedPreferences("mavka_queue_maintenance_v1",FileCreationMode.Private)!.Edit()!;if(!editor.PutString("notice",value)!.Commit())throw new IOException("Не збережено стан черги.");}
    private string QueueBackupFolder=>Path.Combine(context.FilesDir!.AbsolutePath,"queue_backups_v1");
    private QueueHead[] QueueHeads(SQLiteDatabase db,CancellationToken token)
    {
        var result=new List<QueueHead>();using var heads=db.RawQuery("SELECT source,sequence FROM heads WHERE client=? ORDER BY source",new[]{Client});
        while(heads.MoveToNext()){var source=heads.GetString(0);result.Add(new(source,heads.GetLong(1),MobileStateRequest.Hash(RecordHashes(db,source,token))));}return result.ToArray();
    }
    internal string CleanQueue(bool manual,CancellationToken token)
    {
        using var db=WritableDatabase;db.BeginTransaction();
        try
        {
            // Older databases did not index the authenticated sequence. Recover it from
            // their sealed payload; an unreadable row is retained for investigation.
            var migrated=new List<(string Batch,int Index,long Sequence)>();
            using(var old=db.RawQuery("SELECT batch,part,sealed FROM parts WHERE client=? AND sequence<0 AND sealed<>''",new[]{Client}))
                while(old.MoveToNext())
                {
                    token.ThrowIfCancellationRequested();try{var part=JsonSerializer.Deserialize<MobileChangePart>(Open(old.GetString(2)))!;part.Validate(Client);migrated.Add((old.GetString(0),old.GetInt(1),part.sequence));}catch(Exception error)when(error is System.Security.Cryptography.CryptographicException or JsonException or InvalidDataException){ }
                }
            foreach(var old in migrated){var values=new ContentValues();values.Put("sequence",old.Sequence);db.Update("parts",values,"client=? AND batch=? AND part=?",new[]{Client,old.Batch,old.Index.ToString()});}
            var headRows=new List<QueueHead>();using(var rows=db.RawQuery("SELECT source,sequence FROM heads WHERE client=? ORDER BY source",new[]{Client}))while(rows.MoveToNext())headRows.Add(new(rows.GetString(0),rows.GetLong(1),""));
            var heads=headRows.ToArray();var stale=heads.ToDictionary(h=>h.Source,h=>h.Sequence);
            bool Remove(string source,long sequence)=>manual || sequence>=0 && stale.TryGetValue(source,out var current) && sequence<=current;
            var count=0;using(var rows=db.RawQuery("SELECT source,sequence FROM parts WHERE client=? AND sealed<>''",new[]{Client}))while(rows.MoveToNext())if(Remove(rows.GetString(0),rows.GetLong(1)))count++;
            if(count==0){db.SetTransactionSuccessful();return "Застарілих частин черги немає.";}
            heads=QueueHeads(db,token);
            Directory.CreateDirectory(QueueBackupFolder);var name=(manual?"manual-":"auto-")+DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff")+"-"+Guid.NewGuid().ToString("N");var path=Path.Combine(QueueBackupFolder,name+".zip");
            using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))using(var stream=zip.CreateEntry("queue.json",CompressionLevel.Fastest).Open())using(var writer=new Utf8JsonWriter(stream))
                {
                    writer.WriteStartArray();using var rows=db.RawQuery("SELECT source,batch,part,hash,sealed,sequence FROM parts WHERE client=? AND sealed<>''",new[]{Client});
                    while(rows.MoveToNext())
                    {
                        token.ThrowIfCancellationRequested();if(!Remove(rows.GetString(0),rows.GetLong(5)))continue;
                        JsonSerializer.Serialize(writer,new{source=rows.GetString(0),batch=rows.GetString(1),part=rows.GetInt(2),hash=rows.GetString(3),sealedPayload=rows.GetString(4),sequence=rows.GetLong(5)});
                    }
                    writer.WriteEndArray();
                }
                output.Flush(true);
            }
            string Digest(){using var input=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(input));}
            var digest=Digest();var proof=new QueueProof(Client,DateTimeOffset.UtcNow,digest,count,heads);var proofPath=Path.ChangeExtension(path,".proof");
            using(var output=new FileStream(proofPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var bytes=System.Text.Encoding.UTF8.GetBytes(Seal(JsonSerializer.Serialize(proof)));output.Write(bytes);output.Flush(true);}
            var verify=JsonSerializer.Deserialize<QueueProof>(Open(File.ReadAllText(proofPath)))!;if(verify.Client!=Client || verify.Sha256!=Digest())throw new IOException("Не підтверджено резервну копію черги. Частини залишено.");
            token.ThrowIfCancellationRequested();
            if(manual){db.Delete("parts","client=? AND sealed<>''",new[]{Client});var cancelled=new ContentValues();cancelled.Put("status","cancelled");db.Update("state_requests",cancelled,"client=? AND status='pending'",new[]{Client});}
            else foreach(var head in heads)db.Delete("parts","client=? AND source=? AND sealed<>'' AND sequence>=0 AND sequence<=?",new[]{Client,head.Source,head.Sequence.ToString()});
            db.SetTransactionSuccessful();return $"Очищено частин: {count}. Резервну копію збережено. Майно й документи в обліку не змінено.";
        }
        finally{db.EndTransaction();try{PruneAutomaticQueueBackups();}catch(Exception error)when(error is IOException or CryptographicException or JsonException){/* Keep extra copies if retention cannot finish. */}}
    }
    private void PruneAutomaticQueueBackups()
    {
        if(!Directory.Exists(QueueBackupFolder))return;
        var root=Path.GetFullPath(QueueBackupFolder);
        foreach(var file in Directory.GetFiles(root,"auto-*.proof").OrderByDescending(File.GetLastWriteTimeUtc).Skip(3))
        {
            var proof=JsonSerializer.Deserialize<QueueProof>(Open(File.ReadAllText(file)))!;
            if(proof.Client!=Client)continue;
            foreach(var path in new[]{Path.ChangeExtension(file,".zip"),file})
            {
                var full=Path.GetFullPath(path);
                if(Path.GetDirectoryName(full)!=root || !Path.GetFileName(full).StartsWith("auto-",StringComparison.Ordinal))throw new IOException("Невірний шлях копії черги.");
                if(File.Exists(full) && (File.GetAttributes(full)&FileAttributes.ReparsePoint)==0)File.Delete(full);
            }
        }
    }
    internal string CheckLatestQueueBackup(CancellationToken token)
    {
        if(!Directory.Exists(QueueBackupFolder))return "Резервних копій черги ще немає.";
        var file=Directory.GetFiles(QueueBackupFolder,"*.proof").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();if(file is null)return "Резервних копій черги ще немає.";
        var proof=JsonSerializer.Deserialize<QueueProof>(Open(File.ReadAllText(file)))!;if(proof.Client!=Client)throw new IOException("Копія іншого профілю.");
        using(var input=File.OpenRead(Path.ChangeExtension(file,".zip")))if(Convert.ToHexString(SHA256.HashData(input))!=proof.Sha256)throw new IOException("Резервна копія черги пошкоджена.");
        using var db=ReadableDatabase;var current=QueueHeads(db,token);var unchanged=JsonSerializer.Serialize(current)==JsonSerializer.Serialize(proof.Heads);
        return $"Копія від {proof.At.ToLocalTime():dd.MM.yyyy HH:mm}: {proof.Parts} частин, цілісність підтверджено. "+(unchanged?"Застосовані дані телефона збігаються зі станом на час очищення.":"Облік телефона змінився після копії.")+" Для перевірки актуальності щодо ПК надішліть запит оновлення бази.";
    }
}
