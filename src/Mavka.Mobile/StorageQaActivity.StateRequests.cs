#if MAVKA_BOOTSTRAP_QA
using Android.Content;
using Android.Database.Sqlite;
using System.Text.Json;
namespace Mavka.Mobile;
public sealed partial class StorageQaActivity
{
    private string RunStateRequestChecks()
    {
        var count=0;void Check(bool ok,string text){if(!ok)throw new InvalidDataException(text);count++;}
        var proofPath=Path.Combine(FilesDir!.AbsolutePath,"state-request-qa.json");
        var accessPreferences=GetSharedPreferences("mavka_exchange_keys_v1",FileCreationMode.Private)!;var originalPolicy=accessPreferences.GetString("mobile_access",null);var policyChanged=false;
        try
        {
            var profile=new UserProfileStore(this).Load();
            if(new GmailCredentialsStore(this).HasCredentials || new RelayIdentity(this).Enabled || new ExchangeKeyStore(this).HasKeyPair || profile is not null && profile.FullName!="SYNTHETIC-QA-USER")throw new InvalidOperationException("Isolated offline synthetic QA only.");
            if(!(Android.OS.Build.Model?.Contains("sdk",StringComparison.OrdinalIgnoreCase)==true || Android.OS.Build.Fingerprint?.Contains("generic",StringComparison.OrdinalIgnoreCase)==true))throw new InvalidOperationException("Emulator only.");
            profile??=new UserProfileStore(this).Save("QA One","SYNTHETIC-QA-USER","","",false);
            var source=new string('a',64);var client=profile.ClientId;var policy=new MobileAccessPolicy(MobileAccessPolicy.Format,client,100,false,[new(1,"QA One"),new(2,"QA Two")],true,true,false,true,true);
            using(var parsed=JsonDocument.Parse(JsonSerializer.Serialize(policy)))new MobileAccessStore(this).Accept(parsed.RootElement);policyChanged=true;
            var database="qa_state_requests_"+Guid.NewGuid().ToString("N")+".db";
            MobileChangeOperation Asset(int location,int quantity)=>new("asset",$"asset:{location}:1:1",location,false,JsonSerializer.SerializeToElement(new{locationId=location,location=location==1?"QA One":"QA Two",itemId=1,name="Synthetic",unit="од.",price=2,quantity,category=1}));
            MobileChangeOperation Document(int id)=>new("document",$"document:1:{id}",1,false,JsonSerializer.SerializeToElement(new{locationId=1,location="QA One",id,number="SYNTHETIC-"+id,date="2026-10-23",type="Акт",status="Проведено",movements=new[]{new{itemId=1,name="Synthetic",unit="од.",quantity=2,price=2,category=1,batch="QA",code="QA",serials=""}}}));
            MobileChangePart Part(long sequence,MobileChangeOperation[] ops)=>new(MobileChangePart.Format,client,source,Guid.NewGuid().ToString("N"),sequence,sequence-1,sequence==1,"2026-10-23",policy,0,1,MobileChangePart.Hash(ops),ops,sequence==1);
            var legacyName="qa_state_legacy_"+Guid.NewGuid().ToString("N")+".db";
            using(var legacy=new LegacyRequestMirror(this,legacyName))using(var db=legacy.WritableDatabase)
            {
                var head=new ContentValues();head.Put("client",client);head.Put("source",source);head.Put("sequence",4);head.Put("revision",100);head.Put("asof","2026-10-23");db.InsertOrThrow("heads",null,head);
                var value=new ContentValues();value.Put("client",client);value.Put("source",source);value.Put("key","asset:1:1:1");value.Put("kind","asset");value.Put("location",1);value.Put("sealed",Convert.ToBase64String(TransferKeystoreCipher.Seal(System.Text.Encoding.UTF8.GetBytes(Asset(1,42).payload.GetRawText()))));db.InsertOrThrow("records",null,value);
            }
            using(var migrated=new MobileChangeStore(this,legacyName))
            {
                var report=migrated.CreateStateRequest(CancellationToken.None).Single();Check(report.sequence==4 && report.hashes.Count==1,"v1 to v3 migration retains applied records and sequence");
                using var db=migrated.ReadableDatabase;using var indexed=db.RawQuery("SELECT sequence FROM parts LIMIT 1",null);Check(!indexed.MoveToFirst(),"legacy queue gains sequence index without inventing parts");
            }
            using(var mirror=new MobileChangeStore(this,database))
            {
                var empty=mirror.CreateStateRequest(CancellationToken.None);Check(empty.Length==1 && empty[0].hashes.Count==0 && empty[0].sequence==0,"empty phone reports before local cipher has records");mirror.CancelStateRequest();
                var initial=Part(1,[Asset(1,7),Asset(2,11),Document(10)]);mirror.TryAccept(JsonSerializer.Serialize(initial));
                var request=mirror.CreateStateRequest(CancellationToken.None).Single();Check(request.sequence==1 && request.hashes.Count==3,"report captures all applied mirror records");
                Check(mirror.CreateStateRequest(CancellationToken.None).Single().requestId==request.requestId,"unchanged retry retains request identity");
                mirror.TryAccept(JsonSerializer.Serialize(Part(2,[Asset(1,8)])));
                Check(JsonDocument.Parse(mirror.List("asset",1).Single().Json).RootElement.GetProperty("quantity").GetInt32()==7,"normal packages stage while waiting for requested repair");
                var target=new[]{Asset(1,9),Asset(2,11),Document(10),Document(20)}.ToDictionary(o=>o.key,o=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(o.payload.GetRawText()))).ToLowerInvariant());
                var reply=Part(6,[Asset(1,9),Document(20)]) with{full=false,requestId=request.requestId,requestSequence=1,requestHash=request.reportHash,resultHash=MobileStateRequest.Hash(target)};
                mirror.TryAccept(JsonSerializer.Serialize(reply));
                Check(mirror.List("asset").Count==2 && mirror.List("document").Count==2,"repair complements existing records and retains unrelated scope");
                Check(mirror.CreateStateRequest(CancellationToken.None).Single().sequence==6,"requested response bridges missing sequences");mirror.CancelStateRequest();
                using(var db=mirror.ReadableDatabase)using(var row=db.RawQuery("SELECT COUNT(*) FROM parts WHERE client=? AND sequence=2 AND sealed<>''",new[]{client}))Check(row.MoveToFirst() && row.GetInt(0)==0,"covered stale queue removed after verified repair");
                Check(mirror.CheckLatestQueueBackup(CancellationToken.None).Contains("цілісність підтверджено"),"automatic queue backup is verifiable");
                mirror.TryAccept(JsonSerializer.Serialize(reply));Check(mirror.List("document").Count==2,"repair cannot duplicate records");
                var report=mirror.CreateStateRequest(CancellationToken.None).Single();var wrong=Part(8,[Asset(1,99)]) with{full=false,requestId=report.requestId,requestSequence=6,requestHash=report.reportHash,resultHash=new string('b',64)};
                var failed=false;try{mirror.TryAccept(JsonSerializer.Serialize(wrong));}catch(System.Security.Cryptography.CryptographicException){failed=true;}
                Check(failed && JsonDocument.Parse(mirror.List("asset",1).Single().Json).RootElement.GetProperty("quantity").GetInt32()==9,"incorrect resulting state rolls entire update back");
                var clean=mirror.CleanQueue(true,CancellationToken.None);Check(clean.Contains("Очищено частин") && mirror.List("document").Count==2,"manual queue clear backs up parts without touching applied documents");
                var backupState=mirror.CheckLatestQueueBackup(CancellationToken.None);Check(backupState.Contains("збігаються") && backupState.Contains("ще не підтверджено: 1"),"backup distinguishes unapplied parts from unchanged applied state");
                Check(new UserProfileStore(this).Load()==profile && new MobileAccessStore(this).Load()!.clientId==client,"profile and access unchanged");
            }
            using(var bulk=new MobileChangeStore(this,"qa_gmail_drain_"+Guid.NewGuid().ToString("N")+".db"))
            {
                var request=bulk.CreateStateRequest(CancellationToken.None).Single();
                var ops=Enumerable.Range(1000,34).Select(Document).ToArray();
                var target=ops.ToDictionary(o=>o.key,o=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(o.payload.GetRawText()))).ToLowerInvariant());
                var first=Part(2,ops) with{count=34,initial=false,full=false,requestId=request.requestId,requestSequence=0,requestHash=request.reportHash,resultHash=MobileStateRequest.Hash(target)};
                var session=new GmailRefreshSession();var mails=Enumerable.Range(1,34).Select(i=>(Validity:9u,Uid:(uint)i)).ToArray();
                while(true)
                {
                    var batch=mails.Where(m=>!session.Attempted.Contains(m)).Take(3).ToArray();if(!session.Register(batch))break;
                    foreach(var mail in batch)bulk.TryAccept(JsonSerializer.Serialize(first with{index=(int)mail.Uid-1,operations=[ops[(int)mail.Uid-1]]}));
                    if(session.Passes==1)Check(bulk.List("document").Count==0 && bulk.SynchronizationProgress().Contains("3 із 34"),"partial progress is visible; incomplete package cannot change live mirror");
                }
                Check(session.Passes==12 && bulk.List("document").Count==34,"one drain session applies34 parts only after complete verified package");
                Check(bulk.SynchronizationProgress().Contains("документів 34") && !bulk.SynchronizationProgress().Contains("із 34"),"final progress distinguishes applied records from staged parts");
            }
            var result="NATIVE STATE REQUEST QA PASS "+count+" · SQLite report, frozen predecessor, bridge, final hash, rollback, duplicate, queue backup/clear, retained records, Gmail34-part drain/progress. NO NETWORK.";
            File.WriteAllText(proofPath,JsonSerializer.Serialize(new{passed=count,allPassed=true,network=false,result}));return result;
        }
        catch(Exception error){File.WriteAllText(proofPath,JsonSerializer.Serialize(new{passed=count,allPassed=false,error=error.ToString()}));return "STATE REQUEST QA FAIL: "+error;}
        finally{if(policyChanged){using var editor=accessPreferences.Edit()!;if(originalPolicy is null)editor.Remove("mobile_access");else editor.PutString("mobile_access",originalPolicy);editor.Commit();}}
    }
    private sealed class LegacyRequestMirror(Context context,string name):SQLiteOpenHelper(context,name,null,1)
    {
        public override void OnCreate(SQLiteDatabase db)
        {
            db.ExecSQL("CREATE TABLE parts(client TEXT,source TEXT,batch TEXT,part INTEGER,hash TEXT,sealed TEXT,PRIMARY KEY(client,source,batch,part))");
            db.ExecSQL("CREATE TABLE heads(client TEXT,source TEXT,sequence INTEGER,revision INTEGER,asof TEXT,PRIMARY KEY(client,source))");
            db.ExecSQL("CREATE TABLE records(client TEXT,source TEXT,key TEXT,kind TEXT,location INTEGER,sealed TEXT,PRIMARY KEY(client,source,key))");
            db.ExecSQL("CREATE TABLE receipts(client TEXT,id TEXT,sealed TEXT,done INTEGER DEFAULT 0,PRIMARY KEY(client,id))");
        }
        public override void OnUpgrade(SQLiteDatabase db,int oldVersion,int newVersion)=>throw new NotSupportedException();
    }
}
#endif
