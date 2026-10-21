#if MAVKA_BOOTSTRAP_QA
using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using Android.Database.Sqlite;
using System.Text.Json;
using System.Security.Cryptography;

namespace Mavka.Mobile;
// Isolated QA installer only; refuses real user profiles or a connected Gmail.
// No network calls, server identity, live documents or keys are involved.
[Activity(Name="ua.mavka.mobile.StorageQaActivity",Exported=true)]
public sealed class StorageQaActivity:Activity
{
    protected override async void OnCreate(Bundle? state)
    {
        base.OnCreate(state);var text=new TextView(this){TextSize=16};SetContentView(text);
        text.Text="Перевірка синтетичного локального обліку…";
        var resetOnly=Intent?.GetBooleanExtra("reset_only",false)==true;
        text.Text=await Task.Run(()=>Intent?.GetBooleanExtra("large_ui",false)==true?PrepareLargeUi():resetOnly?RunResetChecks():RunChecks());
    }
    private string PrepareLargeUi()
    {
        try
        {
            var profile=new UserProfileStore(this).Load();
            if(profile?.FullName!="SYNTHETIC-QA-USER" || new GmailCredentialsStore(this).HasCredentials || new RelayIdentity(this).Enabled || new ExchangeKeyStore(this).HasKeyPair)throw new InvalidOperationException("Large UI QA requires synthetic offline emulator only");
            using var mirror=new MobileChangeStore(this);using var db=mirror.WritableDatabase;
            using var head=db.RawQuery("SELECT source FROM heads WHERE client=? ORDER BY rowid DESC LIMIT 1",new[]{profile.ClientId});
            if(!head.MoveToFirst())throw new InvalidOperationException("Synthetic mirror required");var source=head.GetString(0);
            var payload=JsonSerializer.Serialize(new{locationId=1,location="QA One",id=100000,number="PERF-SYNTHETIC",date="2026-10-21",type="Акт",status="Проведено",chunkIndex=0,chunkCount=1,movements=new[]{new{itemId=1,name="Synthetic",unit="од.",quantity=1,price=1.123456789m,category=1,batch="QA",code="QA",serials=""}}});
            var sealedPayload=Convert.ToBase64String(TransferKeystoreCipher.Seal(System.Text.Encoding.UTF8.GetBytes(payload)));
            db.BeginTransaction();try
            {
                for(var i=0;i<33000;i++)
                {
                    var values=new ContentValues();values.Put("client",profile.ClientId);values.Put("source",source);values.Put("key","document:1:"+(100000+i));values.Put("kind","document");values.Put("location",1);values.Put("sealed",sealedPayload);db.InsertWithOnConflict("records",null,values,Conflict.Replace);
                }
                db.SetTransactionSuccessful();
            }
            finally{db.EndTransaction();}
            return "NATIVE LARGE UI QA READY 33000 encrypted document rows. NO NETWORK.";
        }
        catch(Exception ex){return "NATIVE LARGE UI QA FAILED: "+ex;}
    }
    private string RunResetChecks()
    {
        try
        {
            var profile=new UserProfileStore(this).Load();var gmail=new GmailCredentialsStore(this);var keys=new ExchangeKeyStore(this);var identity=new RelayIdentity(this);
            if(profile is null || profile.FullName!="SYNTHETIC-QA-USER" || gmail.HasCredentials || identity.Enabled || keys.HasKeyPair)throw new InvalidOperationException("Reset QA requires synthetic offline emulator only");
            var checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
            var access=new MobileAccessStore(this).Load()!;var policyJson=JsonSerializer.Serialize(access);var device=identity.DeviceId;
            var current=RandomNumberGenerator.GetBytes(32);var future=RandomNumberGenerator.GetBytes(32);
            var bootstrap=new BootstrapKeys(BootstrapProtocol.Keys,Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),profile.ClientId,device,RelayProtocol.MainId,DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),1,Convert.ToBase64String(current),Convert.ToBase64String(future),access);
            var ack=BootstrapProtocol.Confirmation(bootstrap,DateTimeOffset.UtcNow);
            keys.InstallBootstrap(bootstrap,"synthetic-hash",ack,"synthetic-main@example.invalid");
            gmail.Save("synthetic-user@example.invalid","synthetic-app-password");gmail.SaveMainMavkaEmail("synthetic-main@example.invalid");
            var documentPassword=new DocumentPasswordStore(this).Load();var proof=TransferKeystoreCipher.Seal("synthetic account proof"u8.ToArray());
            var json=JsonSerializer.Serialize(new{format="MAVKA_DOCUMENT_V1",type="invoice",recipientClientId=profile.ClientId,recipient=access.units[0].unit,documentId="QA-RESET-TRASH",documentNumber="QA-RESET-TRASH",documentDate="2026-10-21",positions=new[]{new{name="Synthetic reset",unit="од.",quantity=2,price=3}}});
            using(var archive=new DocumentArchiveStore(this))
            {
                ArchiveDocumentClassifier.TryClassify(json,access.units[0].unit,out var doc);
                archive.SaveDocument(doc!.DocumentId,doc.Kind,doc.Number,doc.Unit,doc.DocumentDate,"synthetic.json",json);archive.MoveToTrash(doc.DocumentId,doc.Unit);
                archive.MarkMailReady(77,88,"synthetic-scope");
            }
            var privateFolder=Path.Combine(FilesDir!.AbsolutePath,"secure_received_files_v1");Directory.CreateDirectory(privateFolder);File.WriteAllText(Path.Combine(privateFolder,"synthetic.bin"),"synthetic encrypted placeholder");
            LocalAccountReset.Clear(this);
            Check(!LocalAccountReset.IsPending(this),"reset completion marker persisted");
            using(var ledger=new PropertyLedgerStore(this))using(var db=ledger.ReadableDatabase)using(var count=db.RawQuery("SELECT (SELECT COUNT(*) FROM receipts)+(SELECT COUNT(*) FROM receipt_items)+(SELECT COUNT(*) FROM ledger_events)",null))Check(count.MoveToFirst() && count.GetInt(0)==0,"all unit ledger records removed");
            using(var archive=new DocumentArchiveStore(this))
            {
                using(var db=archive.ReadableDatabase)using(var count=db.RawQuery("SELECT (SELECT COUNT(*) FROM documents)+(SELECT COUNT(*) FROM document_status_history)",null))Check(count.MoveToFirst() && count.GetInt(0)==0,"whole archive, posting history and trash removed");
                Check(archive.IsMailReady(77,88,"synthetic-scope"),"processed-mail index retained against resurrection");
            }
            using(var mirror=new MobileChangeStore(this)){mirror.Recover();Check(!mirror.HasRecords("asset") && !mirror.HasRecords("document"),"mirror and staged recovery empty");}
            Check(IncomingBatchPosting.Apply(this).OpeningUnits==0,"cleared snapshots cannot be re-credited on startup");
            Check(!Directory.Exists(privateFolder),"local received attachment files removed");
            Check(new UserProfileStore(this).Load()==profile,"profile/client identity unchanged");
            Check(JsonSerializer.Serialize(new MobileAccessStore(this).Load())==policyJson,"exact unit and role access retained");
            Check(gmail.GetEmail()=="synthetic-user@example.invalid" && gmail.GetPassword()=="synthetic-app-password" && gmail.GetMainMavkaEmail()=="synthetic-main@example.invalid","Gmail account and encrypted password retained");
            Check(new DocumentPasswordStore(this).Load()==documentPassword,"manual common password retained");
            Check(keys.HasKeyPair && keys.Generation==1 && keys.GetKey(keys.CurrentKeyId!)!.SequenceEqual(current) && keys.GetKey(keys.FutureKeyId!)!.SequenceEqual(future),"both exchange keys and generation retained");
            Check(identity.DeviceId==device && !identity.Enabled && !RelayBackground.IsEnabled(this),"Relay identity and background-off choice retained");
            Check(TransferKeystoreCipher.Open(proof).SequenceEqual("synthetic account proof"u8.ToArray()),"existing Keystore ciphertext still decrypts");
            using(var editor=GetSharedPreferences("mavka_local_account_reset_v1",FileCreationMode.Private)!.Edit()!)if(!editor.PutBoolean("pending",true)!.Commit())throw new Exception("QA marker");
            try{IncomingBatchPosting.Apply(this);throw new Exception("pending reset must block posting");}catch(InvalidOperationException){checks++;}
            LocalAccountReset.ResumePending(this);Check(!LocalAccountReset.IsPending(this),"interrupted clear resumes safely");
            LocalAccountReset.Clear(this);Check(JsonSerializer.Serialize(new MobileAccessStore(this).Load())==policyJson,"whole reset retry retains account access");
            CryptographicOperations.ZeroMemory(current);CryptographicOperations.ZeroMemory(future);
            return "NATIVE ACCOUNT RESET QA PASS "+checks+" · all scopes/documents/trash cleared; account, passwords, current/future AES, Keystore and grants retained; resume/replay safe. NO NETWORK.";
        }
        catch(Exception ex){return "NATIVE ACCOUNT RESET QA FAILED: "+ex;}
    }
    private string RunChecks()
    {
        try
        {
            var profile=new UserProfileStore(this).Load();
            if(profile is null || profile.FullName!="SYNTHETIC-QA-USER" || new GmailCredentialsStore(this).HasCredentials || new RelayIdentity(this).Enabled || new ExchangeKeyStore(this).HasKeyPair)throw new InvalidOperationException("QA requires isolated synthetic emulator profile and no connected Gmail.");
            var checks=0;void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
            void Reject(Action action,string label){try{action();}catch(Exception){checks++;return;}throw new Exception(label);}
            var suffix=Guid.NewGuid().ToString("N");var noticeDb="qa_notices_"+suffix+".db";var mirrorDb="mavka_root_mirror_v1.db";
            var prefs=GetSharedPreferences("mavka_exchange_keys_v1",FileCreationMode.Private)!;var old=prefs.GetString("mobile_access",null);var keepPreview=false;
            try
            {
                using(var legacy=new LegacyNotices(this,noticeDb))using(var db=legacy.WritableDatabase)db.ExecSQL("INSERT INTO notices VALUES('old','QA','hash','sealed','2026-10-20',0,NULL,0)");
                var raw=MavkaNoticeProtocol.CreateJson("QA","Synthetic","Only synthetic data",DateTimeOffset.UtcNow);
                MavkaNoticeProtocol.TryRead(raw,"QA",DateTimeOffset.UtcNow,out var notice);
                using(var notices=new MavkaNoticeStore(this,noticeDb))
                {
                    using(var db=notices.ReadableDatabase)using(var row=db.RawQuery("SELECT deleted FROM notices WHERE notice_id='old'",null))Check(row.MoveToFirst() && row.GetInt(0)==0,"v1 migration preserves old row");
                    Check(notices.Add(notice!,raw),"notice saved");notices.SetReminder(notice!.NoticeId,"QA",DateTimeOffset.UtcNow.AddHours(1));
                    notices.Delete(notice.NoticeId,"QA");MavkaNoticeAlerts.Remove(this,notice.NoticeId);
                    Check(notices.Find(notice.NoticeId,"QA") is null && notices.UnreadCount("QA")==1 && notices.PendingReminderCount("QA")==0,"deleted hidden; old unread preserved; reminder cancelled");
                    Check(!notices.Add(notice,raw) && notices.ContainsSaved(notice.NoticeId,"QA",raw),"deleted duplicate remains ACKable without resurrection");
                    Reject(()=>notices.ContainsSaved(notice.NoticeId,"QA",raw.Replace("Only synthetic data","Changed synthetic data")),"conflicting duplicate accepted");
                    using(var db=notices.ReadableDatabase)using(var row=db.RawQuery("SELECT sealed_content,deleted FROM notices WHERE notice_id=?",new[]{notice.NoticeId}))Check(row.MoveToFirst() && row.GetString(0)=="" && row.GetInt(1)==1,"deleted plaintext/ciphertext body removed");
                    var timedRaw=MavkaNoticeProtocol.CreateJson("QA","Timed synthetic","Synthetic reminder",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1));
                    MavkaNoticeProtocol.TryRead(timedRaw,"QA",DateTimeOffset.UtcNow,out var timed);
                    Check(MavkaNoticeAlerts.Receive(this,notices,timed!,timedRaw) && notices.Find(timed!.NoticeId,"QA")!.RemindUtc?.ToUnixTimeMilliseconds()==timed.SuggestedReminderUtc?.ToUnixTimeMilliseconds(),"PC notice automatically persists reminder with no phone confirmation");
                    var snooze=DateTimeOffset.UtcNow.AddHours(2);notices.SetReminder(timed!.NoticeId,"QA",snooze);
                    Check(!MavkaNoticeAlerts.Receive(this,notices,timed,timedRaw) && notices.Find(timed.NoticeId,"QA")!.RemindUtc?.ToUnixTimeMilliseconds()==snooze.ToUnixTimeMilliseconds(),"cross-channel duplicate preserves user's changed reminder");
                    notices.SetReminder(timed.NoticeId,"QA",null);MavkaNoticeAlerts.Receive(this,notices,timed,timedRaw);
                    Check(notices.Find(timed.NoticeId,"QA")!.RemindUtc is null,"duplicate cannot restore a cancelled reminder");
                    notices.Delete(timed.NoticeId,"QA");MavkaNoticeAlerts.Remove(this,timed.NoticeId);
                    Check(!MavkaNoticeAlerts.Receive(this,notices,timed,timedRaw) && notices.Find(timed.NoticeId,"QA") is null,"deleted automatic reminder stays deleted on replay");
                    var lateRaw=MavkaNoticeProtocol.CreateJson("QA","Delayed synthetic","Synthetic overdue reminder",DateTimeOffset.UtcNow.AddHours(-2),DateTimeOffset.UtcNow.AddHours(-1));
                    Check(MavkaNoticeProtocol.TryRead(lateRaw,"QA",DateTimeOffset.UtcNow,out var late),"offline delayed notice remains importable after reminder time");
                    MavkaNoticeAlerts.Receive(this,notices,late!,lateRaw);
                    Check(notices.Find(late!.NoticeId,"QA")!.Reminded && !notices.ConsumeReminder(late.NoticeId,DateTimeOffset.UtcNow),"overdue reminder consumed exactly once");
                    notices.Delete(late.NoticeId,"QA");MavkaNoticeAlerts.Remove(this,late.NoticeId);
                }
                using(var reopened=new MavkaNoticeStore(this,noticeDb))Check(reopened.Find(notice!.NoticeId,"QA") is null && reopened.ContainsSaved(notice.NoticeId,"QA",raw),"deleted tombstone survives restart");
                var client=profile.ClientId;var source=new string('a',64);var access=new MobileAccessPolicy(MobileAccessPolicy.Format,client,1,false,[new(1,"QA One"),new(2,"QA Two")],true,true,false,true,true);
                using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(access)))new MobileAccessStore(this).Accept(doc.RootElement);
                MobileChangeOperation Asset(int loc,int qty)=>new("asset",$"asset:{loc}:1:1",loc,false,JsonSerializer.SerializeToElement(new{locationId=loc,location=loc==1?"QA One":"QA Two",itemId=1,name="Synthetic",unit="од.",quantity=qty,price=1.123456789m,category=1}));
                MobileChangeOperation Invoice(int id,decimal qty=3m)=>new("document",$"document:1:{id}",1,false,JsonSerializer.SerializeToElement(new{locationId=1,location="QA One",id,number="SYNTHETIC-"+id,date="2026-10-20",type="Накладна",status="Проведено",movements=new[]{new{itemId=1,name="Synthetic",unit="од.",quantity=qty,price=1.123456789m,category=1,batch="QA",code="QA",serials=""}}}));
                string Wire(long seq,MobileChangeOperation[] ops,MobileAccessPolicy? policy=null,bool full=false)=>JsonSerializer.Serialize(new MobileChangePart(MobileChangePart.Format,client,source,Guid.NewGuid().ToString("N"),seq,seq-1,seq==1,"2026-10-20",policy??access,0,1,MobileChangePart.Hash(ops),ops,full));
                var initial=new[]{Asset(1,7),Asset(2,9),Invoice(10)};var batch=Guid.NewGuid().ToString("N");
                var first=new MobileChangePart(MobileChangePart.Format,client,source,batch,1,0,true,"2026-10-20",access,0,2,MobileChangePart.Hash(initial),[initial[0]]);
                var second=first with{index=1,operations=[initial[1],initial[2]]};
                using(var early=new MobileChangeStore(this,"qa_early_"+suffix+".db")){early.TryAccept(Wire(2,[Asset(1,8)]));Check(early.List("asset").Count==0,"a delta received before initial full mirror cannot skip the baseline");}
                using(var mirror=new MobileChangeStore(this,mirrorDb)){mirror.TryAccept(JsonSerializer.Serialize(second));Check(mirror.List("asset").Count==0,"partial batch not visible");}
                using(var mirror=new MobileChangeStore(this,mirrorDb))
                {
                    mirror.TryAccept(MobileChangeCompression.Pack(JsonSerializer.Serialize(first)));Check(mirror.List("asset").Count==2,"out of order parts survive restart and apply atomically");
                    using(var archive=new DocumentArchiveStore(this))Check(archive.FindDocument("root:"+source+":1:10") is null,"initial historical invoice not replayed");
                    mirror.TryAccept(JsonSerializer.Serialize(first));Check(mirror.List("asset").Count==2,"duplicate no replay");
                    Reject(()=>mirror.TryAccept(JsonSerializer.Serialize(first with{operations=[Asset(1,99)]})),"conflicting part accepted");
                    var third=Wire(3,[Asset(1,9)]);mirror.TryAccept(third);
                    Check(JsonDocument.Parse(mirror.List("asset",1).Single().Json).RootElement.GetProperty("quantity").GetInt32()==7,"future batch waits for predecessor");
                    mirror.TryAccept(Wire(2,[Asset(1,8)]));Check(JsonDocument.Parse(mirror.List("asset",1).Single().Json).RootElement.GetProperty("quantity").GetInt32()==9,"ordered catchup applies pending batch");
                    mirror.TryAccept(Wire(4,[new("asset","asset:2:1:1",2,true,JsonSerializer.SerializeToElement(new{}))]));Check(mirror.List("asset").Count==1,"explicit deletion applied");
                    mirror.TryAccept(JsonSerializer.Serialize(first));Check(mirror.List("asset").Count==1,"old duplicate cannot rollback");
                    mirror.TryAccept(Wire(5,[Invoice(10,99m)]));
                    using(var archive=new DocumentArchiveStore(this))Check(!archive.ListSummaries("QA One").Any(d=>d.DocumentId.StartsWith("root:"+source+":1:10:",StringComparison.Ordinal)),"editing historical initial invoice never creates another receipt");
                    access=access with{revision=2,units=[new(1,"QA One")],allUnits=false};using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(access)))new MobileAccessStore(this).Accept(doc.RootElement);
                    mirror.TryAccept(Wire(6,[Asset(1,10)],full:true));Check(mirror.List("asset").Single().LocationId==1,"new grant revision rebases complete authorized state");
                    Reject(()=>mirror.TryAccept(Wire(7,[Asset(2,10)])),"forbidden unit accepted");
                    mirror.TryAccept(Wire(7,[Invoice(11)]));
                    using(var archive=new DocumentArchiveStore(this))Check(archive.ListSummaries("QA One").Any(d=>d.DocumentId.StartsWith("root:"+source+":1:11:",StringComparison.Ordinal) && d.PostingStatus=="posted"),"observer new receipt auto-posted");
                    access=access with{revision=3,isMvo=true,canConfirmInvoices=true,autoPostInvoices=false};
                    mirror.TryAccept(Wire(8,[Asset(1,10)],full:true));mirror.TryAccept(Wire(9,[Invoice(12)]));
                    using(var archive=new DocumentArchiveStore(this))Check(archive.ListSummaries("QA One").Any(d=>d.DocumentId.StartsWith("root:"+source+":1:12:",StringComparison.Ordinal) && d.PostingStatus!="posted"),"MVO new receipt awaits manual confirmation");
                    var header=System.Text.Json.Nodes.JsonNode.Parse(Invoice(20).payload.GetRawText())!.AsObject();header["chunkCount"]=2;header["chunkIndex"]=0;
                    var tail=header.DeepClone().AsObject();tail["chunkIndex"]=1;
                    mirror.TryAccept(Wire(10,[new("document","document:1:20",1,false,JsonSerializer.SerializeToElement(header)),new("document","document:1:20:chunk:1",1,false,JsonSerializer.SerializeToElement(tail))]));
                    var whole=mirror.List("document",1).Single(d=>d.Key=="document:1:20");using(var assembled=JsonDocument.Parse(whole.Json))Check(assembled.RootElement.GetProperty("movements").GetArrayLength()==2,"large document chunks compose as one complete read-only document");
                    using(var ledger=new PropertyLedgerStore(this))Check(!ledger.HasReceiptFamily("root:"+source+":1:20"),"large chunk never becomes a partial receipt");
                    using(var ledger=new PropertyLedgerStore(this))Check(!ledger.HasReceiptFamily("root:"+source+":1:12"),"MVO ledger not changed by download");
                    mirror.TryAccept(Wire(11,[Invoice(12,5)]));
                    using(var archive=new DocumentArchiveStore(this))
                    {
                        var versions=archive.ListSummaries("QA One").Where(d=>d.DocumentId.StartsWith("root:"+source+":1:12:",StringComparison.Ordinal)).ToArray();
                        Check(versions.Length==2 && versions.Count(d=>d.PostingStatus=="superseded")==1,"pending correction retains old version and supersedes it");
                        var oldVersionDoc=archive.FindDocument(versions.Single(d=>d.PostingStatus=="superseded").DocumentId)!;MavkaExchangeParser.TryInvoice(oldVersionDoc.Json,out var obsoleteInvoice);Check(!mirror.IsCurrentInvoice(obsoleteInvoice!),"old pending version cannot be posted");
                        var latest=archive.FindDocument(versions.Single(d=>d.PostingStatus!="superseded").DocumentId)!;MavkaExchangeParser.TryInvoice(latest.Json,out var currentInvoice);Check(mirror.IsCurrentInvoice(currentInvoice!),"corrected pending version matches current root");
                        using var ledger=new PropertyLedgerStore(this);ledger.AddConfirmedInvoice(currentInvoice!);ledger.MarkMailSent(currentInvoice!.DocumentId);
                        Check(ledger.HasReceiptFamily("root:"+source+":1:12"),"receipt family prevents another version being credited twice");
                        mirror.TryAccept(Wire(12,[new("document","document:1:12",1,true,JsonSerializer.SerializeToElement(new{}))]));Check(!mirror.IsCurrentInvoice(currentInvoice),"deleted document cannot be newly posted");
                    }
                    access=access with{revision=4,isMvo=false,canConfirmInvoices=false,autoPostInvoices=true,allUnits=true,units=[new(1,"QA One"),new(2,"QA Two")]};
                    mirror.TryAccept(Wire(13,[Asset(1,10),Asset(2,5),new("document","document:1:20",1,false,JsonSerializer.SerializeToElement(header)),new("document","document:1:20:chunk:1",1,false,JsonSerializer.SerializeToElement(tail))],full:true));
                }
                using(var ledger=new PropertyLedgerStore(this))
                {
                    var unit="QA Opening "+suffix;var opening=new UnitAssetsSnapshot(unit,"2026-10-20",[new("Synthetic","од.",7,1.123456789m,null)]);
                    Check(!ledger.HasOpeningBalances(unit) && ledger.AddOpeningBalances(opening) && ledger.HasOpeningBalances(unit),"opening status from actual durable receipt");
                    Check(!ledger.AddOpeningBalances(opening) && ledger.ListHoldingsAsOf(unit,new(2026,10,20)).Single().Quantity==7,"opening retry never doubles stock");
                }
                using(var archive=new DocumentArchiveStore(this))using(var ledger=new PropertyLedgerStore(this))
                {
                    var grants=Enumerable.Range(1,65).Select(i=>new MobileUnitGrant(100+i,"QA Batch "+i)).ToArray();
                    var batchPolicy=access with{revision=5,units=grants};
                    void Policy(MobileAccessPolicy policy){using var doc=JsonDocument.Parse(JsonSerializer.Serialize(policy));new MobileAccessStore(this).Accept(doc.RootElement);}
                    void Save(string json)
                    {
                        using var parsed=JsonDocument.Parse(json);var root=parsed.RootElement;
                        var unit=root.TryGetProperty("recipient",out var recipient)?recipient.GetString()!:root.GetProperty("unit").GetString()!;
                        if(!ArchiveDocumentClassifier.TryClassify(json,unit,out var doc) || doc is null)throw new Exception("Synthetic document parse failed");
                        archive.SaveDocument(doc.DocumentId,doc.Kind,doc.Number,doc.Unit,doc.DocumentDate,"synthetic.json",json);
                    }
                    string Snapshot(string unit,int qty,string date="2026-10-20",string? nsn=null)=>JsonSerializer.Serialize(new{format="MAVKA_UNIT_ASSETS_V1",recipientClientId=client,unit,asOf=date,items=qty==0?Array.Empty<object>():new object[]{new{name="Synthetic batch",unit="од.",quantity=qty,price=1.123456789m,nsn}}});
                    string Receipt(string unit,string id,int qty,string date="2026-10-21")=>JsonSerializer.Serialize(new{format="MAVKA_DOCUMENT_V1",type="invoice",recipientClientId=client,recipient=unit,documentId=id,documentNumber=id,documentDate=date,positions=new[]{new{name="Synthetic batch",unit="од.",quantity=qty,price=1.123456789m}}});
                    Policy(batchPolicy);
                    foreach(var grant in grants)Save(Snapshot(grant.unit,grant.locationId==165?0:7));
                    Save(Snapshot(grants[0].unit,3,"2026-10-19")); // delivered later but older accounting date
                    Save(Snapshot("QA Forbidden",99));
                    Save(Receipt(grants[0].unit,"QA-BATCH-INVOICE",2));
                    var imported=IncomingBatchPosting.Apply(this);
                    Check(imported.OpeningUnits==65 && imported.Invoices==1 && imported.Issues.Count==0,"observer all 65 units and invoice posted in one pass");
                    Check(ledger.ListHoldingsAsOf(grants[0].unit,new(2026,10,21)).Single().Quantity==9,"latest dated snapshot chosen regardless of arrival; later receipt once");
                    Check(ledger.HasOpeningBalances(grants[^1].unit) && ledger.ListHoldingsAsOf(grants[^1].unit,new(2026,10,21)).Count==0,"empty authorized unit has durable zero baseline");
                    Check(!ledger.HasReceipts("QA Forbidden"),"batch never posts archived forbidden unit");
                    Check(ledger.ListPendingReceiptIds(100).Count==0 && archive.FindDocument("QA-BATCH-INVOICE")!.PostingStatus=="posted","automatic posting creates no outgoing MVO receipt");
                    var retry=IncomingBatchPosting.Apply(this,true);
                    Check(retry.OpeningUnits==0 && retry.Invoices==0 && retry.Issues.Count==0,"whole batch manual retry idempotent");
                    using(var reopened=new PropertyLedgerStore(this))Check(reopened.HasOpeningBalances(grants[24].unit),"batch baseline survives fresh store");
                    var mvo=batchPolicy with{revision=6,isMvo=true,allUnits=false,units=[grants[0]],canConfirmInvoices=true,autoPostInvoices=false};Policy(mvo);
                    var manualInvoice=Receipt(grants[0].unit,"QA-BATCH-MVO",4);Save(manualInvoice);
                    new MobileAccessStore(this).AutoPost(manualInvoice);
                    Check(IncomingBatchPosting.Apply(this).Invoices==0 && ledger.GetMailStatus("QA-BATCH-MVO") is null,"MVO never auto-confirms stored invoice");
                    Check(IncomingBatchPosting.Apply(this,true).Invoices==1 && ledger.ListHoldingsAsOf(grants[0].unit,new(2026,10,21)).Single().Quantity==13,"MVO one action confirms allowed backlog once");
                    Check(IncomingBatchPosting.Apply(this,true).Invoices==0,"MVO batch duplicate cannot double-credit");
                    Save(Receipt(grants[1].unit,"QA-BATCH-ROLE-CHANGE",5));
                    var observer=batchPolicy with{revision=7,units=[grants[1]],allUnits=false};Policy(observer);
                    Check(IncomingBatchPosting.Apply(this).Invoices==1 && ledger.GetMailStatus("QA-BATCH-ROLE-CHANGE")=="sent","grant or role change recovers already archived invoice");
                    Save(Receipt(grants[0].unit,"QA-BATCH-REVOKED",1));
                    Check(IncomingBatchPosting.Apply(this,true).Invoices==0 && !new MobileAccessStore(this).PostDocument(Receipt(grants[0].unit,"QA-BATCH-REVOKED",1),true),"current access checked after scope revoked");
                    var conflict=new MobileUnitGrant(900,"QA Conflict");var healthy=new MobileUnitGrant(901,"QA Healthy");var invalid=new MobileUnitGrant(902,"QA Invalid");
                    Policy(batchPolicy with{revision=8,allUnits=false,units=[conflict,healthy,invalid]});
                    MavkaExchangeParser.TryInvoice(Receipt(conflict.unit,"QA-EXISTING",1),out var existing);ledger.AddConfirmedInvoice(existing!);ledger.MarkMailSent(existing!.DocumentId);
                    Save(Snapshot(conflict.unit,7));Save(Snapshot(healthy.unit,8));Save(Snapshot(invalid.unit,2000000000));
                    var partial=IncomingBatchPosting.Apply(this,true);
                    Check(partial.OpeningUnits==1 && partial.Issues.Count==2,"conflict and invalid scope reported without blocking healthy unit");
                    Check(!ledger.HasOpeningBalances(conflict.unit) && ledger.ListHoldingsAsOf(conflict.unit,new(2026,10,21)).Single().Quantity==1,"snapshot never added over existing receipts");
                    Check(!ledger.HasOpeningBalances(invalid.unit) && !ledger.HasReceipts(invalid.unit),"bad row rolls back the whole unit baseline");
                    Save(Snapshot("QA One",10,nsn:"FIND-ONE"));Save(Snapshot("QA Two",5,nsn:"FIND-TWO"));
                    Policy(access with{revision=9});
                }
                keepPreview=true;
                return "NATIVE STORAGE QA PASS "+checks+" · SQLite migration, deleted reminder replay, staged batch restart/order, grants, MVO/observer, 65-unit batch, opening status. NO NETWORK.";
            }
            finally
            {
                if(!keepPreview){using var editor=prefs.Edit()!;if(old is null)editor.Remove("mobile_access");else editor.PutString("mobile_access",old);if(!editor.Commit())throw new IOException("QA policy restore failed.");}
            }
        }
        catch(Exception ex){return "NATIVE STORAGE QA FAILED: "+ex;}
    }
    private sealed class LegacyNotices(Context context,string name):SQLiteOpenHelper(context,name,null,1)
    {
        public override void OnCreate(SQLiteDatabase db)=>db.ExecSQL("CREATE TABLE notices(notice_id TEXT PRIMARY KEY,unit TEXT,content_sha TEXT,sealed_content TEXT,received_utc TEXT,is_read INTEGER,reminder_ms INTEGER,reminded INTEGER)");
        public override void OnUpgrade(SQLiteDatabase db,int oldVersion,int newVersion)=>throw new NotSupportedException();
    }
}
#endif
