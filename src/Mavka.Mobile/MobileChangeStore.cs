using Android.Content;
using Android.Database.Sqlite;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Mavka.Mobile;
internal sealed record MobileMirrorRecord(string Key,string Kind,int LocationId,string Json);
internal sealed class MobileChangeStore(Context context,string databaseName="mavka_root_mirror_v1.db"):SQLiteOpenHelper(context,databaseName,null,1)
{
    private string Client=>new UserProfileStore(context).Load()?.ClientId??throw new InvalidOperationException("Немає профілю.");
    public override void OnCreate(SQLiteDatabase db)
    {
        db.ExecSQL("CREATE TABLE parts(client TEXT,source TEXT,batch TEXT,part INTEGER,hash TEXT,sealed TEXT,PRIMARY KEY(client,source,batch,part))");
        db.ExecSQL("CREATE TABLE heads(client TEXT,source TEXT,sequence INTEGER,revision INTEGER,asof TEXT,PRIMARY KEY(client,source))");
        db.ExecSQL("CREATE TABLE records(client TEXT,source TEXT,key TEXT,kind TEXT,location INTEGER,sealed TEXT,PRIMARY KEY(client,source,key))");
        db.ExecSQL("CREATE TABLE receipts(client TEXT,id TEXT,sealed TEXT,done INTEGER DEFAULT 0,PRIMARY KEY(client,id))");
    }
    public override void OnUpgrade(SQLiteDatabase db,int oldVersion,int newVersion)=>throw new InvalidOperationException("Непідтримувана версія дзеркала обліку.");
    private static string Seal(string value)
    {
        var bytes=Encoding.UTF8.GetBytes(value);try{return Convert.ToBase64String(TransferKeystoreCipher.Seal(bytes));}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    private static string Open(string value)
    {
        var bytes=TransferKeystoreCipher.Open(Convert.FromBase64String(value));try{return Encoding.UTF8.GetString(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static long Head(SQLiteDatabase db,string client,string source)
    {
        using var row=db.RawQuery("SELECT sequence FROM heads WHERE client=? AND source=?",new[]{client,source});return row.MoveToFirst()?row.GetLong(0):0;
    }
    public bool TryAccept(string authenticatedJson)
    {
        authenticatedJson=MobileChangeCompression.Open(authenticatedJson,Client);
        using var json=JsonDocument.Parse(authenticatedJson);
        if(!json.RootElement.TryGetProperty("format",out var format) || format.GetString()!=MobileChangePart.Format)return false;
        if(Encoding.UTF8.GetByteCount(authenticatedJson)>150000)throw new InvalidDataException("Завеликий пакет змін.");
        var part=json.RootElement.Deserialize<MobileChangePart>()??throw new InvalidDataException("Порожній пакет змін.");var client=Client;part.Validate(client);
        var access=new MobileAccessStore(context).Load();
        // A previously accepted newer policy may already have revoked a scope.
        // Retain/ACK obsolete authenticated parts, but never apply them over it.
        using(var sourceDb=ReadableDatabase)using(var active=sourceDb.RawQuery("SELECT source FROM heads WHERE client=? LIMIT 1",new[]{client}))
            if(active.MoveToFirst() && active.GetString(0)!=part.sourceId)throw new InvalidDataException("Цей телефон уже прив'язаний до іншої бази обліку. Потрібне узгоджене перепідключення.");
        var obsolete=access is not null && access.revision>part.access.revision;
        var duplicate=false;
        using(var db=WritableDatabase)
        {
            db.BeginTransaction();try
            {
                using(var row=db.RawQuery("SELECT hash FROM parts WHERE client=? AND source=? AND batch=? AND part=?",new[]{client,part.sourceId,part.batchId,part.index.ToString()}))
                {
                    if(row.MoveToFirst())
                    {
                        if(row.GetString(0)!=Hash(authenticatedJson))throw new CryptographicException("Конфлікт повторної частини змін.");
                        duplicate=true;
                    }
                }
                if(!duplicate)
                {
                using(var quota=db.RawQuery("SELECT COALESCE(SUM(LENGTH(sealed)),0),COUNT(*) FROM parts WHERE client=?",new[]{client}))
                    if(quota.MoveToFirst() && (quota.GetLong(0)>80000000 || quota.GetLong(1)>100000))throw new IOException("Черга змін переповнена.");
                var values=new ContentValues();values.Put("client",client);values.Put("source",part.sourceId);values.Put("batch",part.batchId);values.Put("part",part.index);values.Put("hash",Hash(authenticatedJson));values.Put("sealed",obsolete?"":Seal(authenticatedJson));db.InsertOrThrow("parts",null,values);
                using(var verify=db.RawQuery("SELECT hash,sealed FROM parts WHERE client=? AND source=? AND batch=? AND part=?",new[]{client,part.sourceId,part.batchId,part.index.ToString()}))
                    if(!verify.MoveToFirst() || verify.GetString(0)!=Hash(authenticatedJson) || !obsolete && Open(verify.GetString(1))!=authenticatedJson)throw new IOException("Зміни не збережено.");
                }
                db.SetTransactionSuccessful();
            }
            finally{db.EndTransaction();}
        }
        if(!obsolete)ApplyReady(part.sourceId);
        DrainReceipts();return true; // authenticated readback/staging is durable even while other parts are missing
    }
    private void ApplyReady(string source)
    {
        var client=Client;
        while(true)
        {
            using var db=WritableDatabase;var current=Head(db,client,source);
            var candidates=new List<MobileChangePart>();
            using(var rows=db.RawQuery("SELECT sealed FROM parts WHERE client=? AND source=? AND part=0 AND sealed<>''",new[]{client,source}))
                while(rows.MoveToNext())candidates.Add(JsonSerializer.Deserialize<MobileChangePart>(Open(rows.GetString(0)))!);
            long revision;using(var headRow=db.RawQuery("SELECT revision FROM heads WHERE client=? AND source=?",new[]{client,source}))revision=headRow.MoveToFirst()?headRow.GetLong(0):0;
            var latestPolicy=new MobileAccessStore(context).Load()?.revision??0;
            var first=candidates.Where(p=>p.access.revision>=latestPolicy && (p.baseSequence==current || p.full && p.access.revision>revision)).OrderByDescending(p=>p.access.revision).ThenBy(p=>p.sequence).FirstOrDefault();
            if(first is null)return;
            var parts=new List<MobileChangePart>();
            using(var rows=db.RawQuery("SELECT part,sealed FROM parts WHERE client=? AND source=? AND batch=? ORDER BY part",new[]{client,source,first.batchId}))
                while(rows.MoveToNext())
                {
                    if(rows.GetString(1).Length==0)return;
                    var part=JsonSerializer.Deserialize<MobileChangePart>(Open(rows.GetString(1)))!;part.Validate(client);parts.Add(part);
                }
            if(parts.Count<first.count)return;
            var metadata=JsonSerializer.Serialize(first with{index=0,operations=[]});
            if(parts.Count!=first.count || parts.Where((p,i)=>p.index!=i || JsonSerializer.Serialize(p with{index=0,operations=[]})!=metadata).Any())throw new CryptographicException("Неузгоджені частини змін.");
            var operations=parts.SelectMany(p=>p.operations).ToArray();
            if(MobileChangePart.Hash(operations)!=first.manifestHash || operations.Select(p=>p.key).Distinct(StringComparer.Ordinal).Count()!=operations.Length)throw new CryptographicException("Невірна повнота пакета змін.");
            var oldAccess=new MobileAccessStore(context).Load();
            if(oldAccess is not null && oldAccess.revision>first.access.revision)return;
            // Persist policy first: a crash can only hide/restrict old rows, never
            // expose another unit. Applying the staged batch is retried next time.
            using(var policy=JsonDocument.Parse(JsonSerializer.Serialize(first.access)))new MobileAccessStore(context).Accept(policy.RootElement);
            db.BeginTransaction();try
            {
                var reset=(first.full || first.initial) && first.access.revision>revision;
                if(!reset && Head(db,client,source)!=first.baseSequence)throw new IOException("Послідовність змін оновилась.");
                if(reset)db.Delete("records","client=? AND source=?",new[]{client,source});
                foreach(var op in operations)
                {
                    bool existed;using(var row=db.RawQuery("SELECT 1 FROM records WHERE client=? AND source=? AND key=?",new[]{client,source,op.key}))existed=row.MoveToFirst();
                    if(op.remove){db.Delete("records","client=? AND source=? AND key=?",new[]{client,source,op.key});continue;}
                    var values=new ContentValues();values.Put("client",client);values.Put("source",source);values.Put("key",op.key);values.Put("kind",op.kind);values.Put("location",op.locationId);values.Put("sealed",Seal(op.payload.GetRawText()));db.InsertWithOnConflict("records",null,values,Conflict.Replace);
                    if(!first.initial && !reset && op.kind=="document")QueueReceipt(db,first,op);
                }
                var head=new ContentValues();head.Put("client",client);head.Put("source",source);head.Put("sequence",first.sequence);head.Put("revision",first.access.revision);head.Put("asof",first.asOf);db.InsertWithOnConflict("heads",null,head,Conflict.Replace);
                var clear=new ContentValues();clear.Put("sealed","");db.Update("parts",clear,"client=? AND source=? AND batch=?",new[]{client,source,first.batchId});
                db.SetTransactionSuccessful();
            }
            finally{db.EndTransaction();}
        }
    }
    private void QueueReceipt(SQLiteDatabase db,MobileChangePart batch,MobileChangeOperation op)
    {
        var p=op.payload;
        if(p.TryGetProperty("chunkCount",out var chunkCount) && chunkCount.GetInt32()>1)return; // whole large documents remain read-only; never post a partial receipt
        if(!p.GetProperty("type").GetString()!.Contains("накладна",StringComparison.OrdinalIgnoreCase) || p.GetProperty("status").GetString()=="Скасовано")return;
        var lines=p.GetProperty("movements").EnumerateArray().Where(r=>r.GetProperty("quantity").GetDecimal()>0).ToArray();
        if(lines.Length is <1 or >200 || lines.Any(r=>r.GetProperty("quantity").GetDecimal()>1000000000m || r.GetProperty("price").ValueKind!=JsonValueKind.Number))return;
        var legacyId=p.GetProperty("id").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
        using(var archive=new DocumentArchiveStore(context))
        {
            var previous=archive.FindDocument(legacyId);
            if(previous is not null && previous.Unit==p.GetProperty("location").GetString())return;
        }
        var family="root:"+batch.sourceId+":"+op.locationId+":"+p.GetProperty("id").GetInt32();
        using(var ledger=new PropertyLedgerStore(context))if(ledger.HasReceiptFamily(family))return;
        var id=family+":"+Hash(p.GetRawText())[..16];
        var json=JsonSerializer.Serialize(new{format="MAVKA_DOCUMENT_V1",type="invoice",documentId=id,documentNumber=p.GetProperty("number").GetString(),documentDate=p.GetProperty("date").GetString(),recipient=p.GetProperty("location").GetString(),recipientClientId=Client,positions=lines.Select(r=>new{name=r.GetProperty("name").GetString(),unit=r.GetProperty("unit").GetString(),quantity=r.GetProperty("quantity").GetDecimal(),price=r.GetProperty("price").GetDecimal()})});
        if(!MavkaExchangeParser.TryInvoice(json,out _))return;
        var values=new ContentValues();values.Put("client",Client);values.Put("id",id);values.Put("sealed",Seal(json));db.InsertWithOnConflict("receipts",null,values,Conflict.Ignore);
    }
    public void DrainReceipts()
    {
        var pending=new List<(string Id,string Json)>();
        using(var db=ReadableDatabase)using(var rows=db.RawQuery("SELECT id,sealed FROM receipts WHERE client=? AND done=0 LIMIT 200",new[]{Client}))while(rows.MoveToNext())pending.Add((rows.GetString(0),Open(rows.GetString(1))));
        foreach(var receipt in pending)
        {
            var access=new MobileAccessStore(context);if(!access.TryDocument(receipt.Json,out var doc) || doc is null)continue;
            using var archive=new DocumentArchiveStore(context);archive.SaveDocument(doc.DocumentId,doc.Kind,doc.Number,doc.Unit,doc.DocumentDate,"mavka-changes.json",doc.Json);
            if(archive.FindDocument(doc.DocumentId) is null)throw new IOException("Накладну не збережено.");
            var family=ReceiptFamily(doc.DocumentId);
            if(family is not null)
                foreach(var old in archive.ListSummaries(doc.Unit).Where(s=>s.DocumentId!=doc.DocumentId && ReceiptFamily(s.DocumentId)==family && s.PostingStatus!="posted"))archive.MarkSuperseded(old.DocumentId);
            if(MavkaExchangeParser.TryInvoice(receipt.Json,out var incoming) && incoming is not null && !IsCurrentInvoice(incoming))archive.MarkSuperseded(doc.DocumentId);
            else access.AutoPost(receipt.Json,IsCurrentInvoice);
            using var db=WritableDatabase;var values=new ContentValues();values.Put("done",1);values.Put("sealed","");db.Update("receipts",values,"client=? AND id=?",new[]{Client,receipt.Id});
        }
    }
    internal static string? ReceiptFamily(string id)
    {
        var fields=id.Split(':');return fields.Length>=4 && fields[0]=="root"?string.Join(":",fields.Take(4)):null;
    }
    public bool IsCurrentInvoice(IncomingInvoice invoice)
    {
        var family=ReceiptFamily(invoice.DocumentId);if(family is null)return true;
        var fields=family.Split(':');if(!int.TryParse(fields[2],out var location) || new MobileAccessStore(context).Load()?.Allows(location)!=true)return false;
        using var db=ReadableDatabase;using var row=db.RawQuery("SELECT sealed FROM records WHERE client=? AND source=? AND key=?",new[]{Client,fields[1],"document:"+location+":"+fields[3]});if(!row.MoveToFirst())return false;
        using var doc=JsonDocument.Parse(Open(row.GetString(0)));var p=doc.RootElement;
        if(p.TryGetProperty("chunkCount",out var chunkCount) && chunkCount.GetInt32()>1)return false;
        if(p.GetProperty("status").GetString()=="Скасовано" || p.GetProperty("number").GetString()!=invoice.Number || p.GetProperty("date").GetString()!=invoice.Date || p.GetProperty("location").GetString()!=invoice.Recipient)return false;
        var expected=p.GetProperty("movements").EnumerateArray().Where(r=>r.GetProperty("quantity").GetDecimal()>0).Select(r=>(r.GetProperty("name").GetString(),r.GetProperty("unit").GetString(),r.GetProperty("quantity").GetDecimal(),r.GetProperty("price").ValueKind==JsonValueKind.Number?(decimal?)r.GetProperty("price").GetDecimal():null));
        return expected.SequenceEqual(invoice.Positions.Select(r=>((string?)r.Name,(string?)r.Unit,r.Quantity,r.UnitPrice)));
    }
    public void Recover()
    {
        using(var pendingDb=ReadableDatabase)using(var sources=pendingDb.RawQuery("SELECT DISTINCT source FROM parts WHERE client=? AND sealed<>''",new[]{Client}))
        {var ids=new List<string>();while(sources.MoveToNext())ids.Add(sources.GetString(0));foreach(var id in ids)ApplyReady(id);}
        DrainReceipts();
    }
    public bool HasRecords(string kind,int? location=null)
    {
        var access=new MobileAccessStore(context).Load();if(access is null)return false;
        using var db=ReadableDatabase;using var rows=db.RawQuery("SELECT location FROM records WHERE client=? AND kind=?",new[]{Client,kind});
        while(rows.MoveToNext())if((location is null || rows.GetInt(0)==location) && access.Allows(rows.GetInt(0),kind=="asset"))return true;
        return false;
    }
    public IReadOnlyList<MobileMirrorRecord> List(string kind,int? location=null)
    {
        var access=new MobileAccessStore(context).Load();if(access is null || kind=="asset" && !access.canViewAssets || kind=="document" && !access.canViewDocuments)return [];
        var result=new List<MobileMirrorRecord>();using var db=ReadableDatabase;
        // The most recently applied source is the active root mirror; never sum
        // the same accounting base imported through two source paths.
        using var source=db.RawQuery("SELECT source FROM heads WHERE client=? ORDER BY rowid DESC LIMIT 1",new[]{Client});if(!source.MoveToFirst())return [];
        using var rows=db.RawQuery("SELECT key,location,sealed FROM records WHERE client=? AND source=? AND kind=?",new[]{Client,source.GetString(0),kind});
        while(rows.MoveToNext())if((location is null || rows.GetInt(1)==location) && access.Allows(rows.GetInt(1),kind=="asset"))result.Add(new(rows.GetString(0),kind,rows.GetInt(1),Open(rows.GetString(2))));
        if(kind!="document")return result;
        var complete=new List<MobileMirrorRecord>();
        foreach(var group in result.GroupBy(r=>r.Key.Split(":chunk:",StringSplitOptions.None)[0]))
        {
            var chunks=group.Select(r=>(Record:r,Node:System.Text.Json.Nodes.JsonNode.Parse(r.Json)!.AsObject())).OrderBy(r=>r.Node["chunkIndex"]?.GetValue<int>()??0).ToArray();
            var count=chunks[0].Node["chunkCount"]?.GetValue<int>()??1;
            if(count!=chunks.Length || chunks.Where((r,i)=>(r.Node["chunkIndex"]?.GetValue<int>()??0)!=i).Any())throw new CryptographicException("Не всі частини документа збережені.");
            var merged=chunks[0].Node.DeepClone().AsObject();var movements=new System.Text.Json.Nodes.JsonArray();
            foreach(var chunk in chunks)foreach(var row in chunk.Node["movements"]!.AsArray())movements.Add(row!.DeepClone());
            merged["movements"]=movements;complete.Add(new(group.Key,"document",chunks[0].Record.LocationId,merged.ToJsonString()));
        }
        return complete;
    }
}
