using System.Text.Json;
using System.Security.Cryptography;
using Mavka.Mobile;
var checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
void Reject(Action action,string message){try{action();}catch(Exception){checks++;return;}throw new Exception(message);}
using var fixture=JsonDocument.Parse(File.ReadAllText(args[0]));var f=fixture.RootElement;
var now=DateTimeOffset.FromUnixTimeSeconds(f.GetProperty("now").GetInt64());var client=f.GetProperty("clientId").GetString()!;var device=f.GetProperty("deviceId").GetString()!;
var qr=f.GetProperty("qr").GetString()!;var envelope=f.GetProperty("envelope").GetString()!;var password=f.GetProperty("password").GetString()!;
var invitation=RegistrationV2Protocol.Parse(qr,now);var request=JsonSerializer.Deserialize<RegistrationRequest>(f.GetProperty("request").GetString()!)!;var keys=BootstrapProtocol.Open(envelope,invitation,request,password,now);
Check(keys.generation==1 && keys.access.canConfirmInvoices && !keys.access.autoPostInvoices,"MVO contract");Check(!qr.Contains(password) && !JsonSerializer.Serialize(keys).Contains(password) && !JsonSerializer.Serialize(keys).Contains("documentPassword"),"No password in QR/key plaintext");Check(keys.currentKey!=keys.futureKey && Convert.FromBase64String(keys.currentKey).Length==32,"Key pair");
Reject(()=>BootstrapProtocol.Open(envelope,invitation,request with{clientId=Guid.NewGuid().ToString("N")},password,now),"Wrong client accepted");Reject(()=>BootstrapProtocol.Open(envelope,invitation,request with{deviceId=Guid.NewGuid().ToString()},password,now),"Wrong phone accepted");Reject(()=>RegistrationV2Protocol.Parse(qr,now.AddMinutes(11)),"Expired QR accepted");Reject(()=>BootstrapProtocol.Open(envelope,invitation,request with{requestId=Guid.NewGuid().ToString()},password,now),"Different request accepted");Reject(()=>BootstrapProtocol.Open(envelope,invitation,request,password,now.AddMinutes(11)),"Expired package accepted");Reject(()=>BootstrapProtocol.Open(envelope,invitation,request,"INCORRECT-SHARED-PASSWORD",now),"Wrong shared password accepted");
var key=RegistrationV2Protocol.Derive(invitation,"request");try{Check(RegistrationV2Protocol.Open(f.GetProperty("requestWire").GetString()!,key,password)==f.GetProperty("request").GetString(),"PC enrollment decoded by Android");}finally{CryptographicOperations.ZeroMemory(key);}
var tampered=System.Text.Json.Nodes.JsonNode.Parse(envelope)!;var bytes=Convert.FromBase64String(tampered["ciphertext"]!.GetValue<string>());bytes[0]^=1;tampered["ciphertext"]=Convert.ToBase64String(bytes);Reject(()=>BootstrapProtocol.Open(tampered.ToJsonString(),invitation,request,password,now),"Tamper accepted");
var observer=keys.access with{isMvo=false,canConfirmInvoices=false,autoPostInvoices=true,units=[new(1,"One"),new(2,"Two")]};observer.Validate(client);Check(observer.Allows(1) && observer.Allows(2) && !observer.Allows(3),"Observer units");Reject(()=>(observer with{isMvo=true}).Validate(client),"MVO multi-unit accepted");Reject(()=>(observer with{canConfirmInvoices=true}).Validate(client),"Observer confirmation accepted");
using var ack=JsonDocument.Parse(BootstrapProtocol.Confirmation(keys,now));Check(ack.RootElement.GetProperty("sessionId").GetString()==invitation.sessionId && ack.RootElement.GetProperty("messageId").GetString()==keys.messageId,"Bound confirmation");
var admin=observer with{allUnits=true};admin.Validate(client);Check(admin.allUnits && admin.Allows(1) && !admin.Allows(3),"All scope still constrained to explicit delivered grants");
Reject(()=>(keys.access with{allUnits=true}).Validate(client),"MVO global scope accepted");
var op=new MobileChangeOperation("asset","asset:1:1:1",1,false,JsonSerializer.SerializeToElement(new{locationId=1,itemId=1,name="Synthetic",quantity=7m,price=1.123456789m,unit="од.",category=1}));
var part=new MobileChangePart(MobileChangePart.Format,client,new string('a',64),Guid.NewGuid().ToString("N"),1,0,true,"2026-10-20",admin,0,1,MobileChangePart.Hash([op]),[op]);part.Validate(client);checks++;
var roundtrip=JsonSerializer.Deserialize<MobileChangePart>(JsonSerializer.Serialize(part))!;roundtrip.Validate(client);Check(MobileChangePart.Hash(roundtrip.operations)==part.manifestHash,"Manifest survives wire roundtrip exactly");
Reject(()=>(part with{operations=[op with{locationId=3,key="asset:3:1:1"}]}).Validate(client),"Unpermitted unit accepted");
Reject(()=>(part with{operations=[op with{payload=JsonSerializer.SerializeToElement(new{locationId=2})}]}).Validate(client),"Mismatched payload scope accepted");
Reject(()=>(part with{operations=[op,op]}).Validate(client),"Duplicate record key accepted");
Reject(()=>(part with{sequence=3}).Validate(client),"Invalid sequence accepted");
Reject(()=>(part with{count=0}).Validate(client),"Invalid part count accepted");
Reject(()=>part.Validate("other-client"),"Wrong recipient accepted");
var pcChanges=JsonSerializer.Deserialize<MobileChangePart[]>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0])!,"synthetic-pc-changes-fixture.json")))!;
foreach(var pcPart in pcChanges)pcPart.Validate(pcPart.recipientClientId);
Check(pcChanges.Length>0 && MobileChangePart.Hash(pcChanges.OrderBy(p=>p.index).SelectMany(p=>p.operations))==pcChanges[0].manifestHash,"Actual PC delta fixture manifest and scope validated on Android codec");


var compressionOp=op with{payload=JsonSerializer.SerializeToElement(new{locationId=1,name=new string('a',5000),quantity=7m})};
var compressionPart=part with{operations=[compressionOp],manifestHash=MobileChangePart.Hash([compressionOp])};
var changeJson=JsonSerializer.Serialize(compressionPart);var packed=MobileChangeCompression.Pack(changeJson);
using(var packedMetadata=JsonDocument.Parse(packed))Check(packedMetadata.RootElement.GetProperty("format").GetString()==MobileChangeCompression.Format,"Compression fixture really compressed on every platform");
Check(MobileChangeCompression.Open(packed,client)==changeJson,"Brotli restores exact logical bytes");
Reject(()=>MobileChangeCompression.Open(packed,"other-client"),"Compressed wrong recipient accepted");
var corrupt=System.Text.Json.Nodes.JsonNode.Parse(packed)!.AsObject();corrupt["sha256"]=new string('0',64);Reject(()=>MobileChangeCompression.Open(corrupt.ToJsonString(),client),"Compressed tamper accepted");
corrupt=System.Text.Json.Nodes.JsonNode.Parse(packed)!.AsObject();corrupt["size"]=1;Reject(()=>MobileChangeCompression.Open(corrupt.ToJsonString(),client),"Compressed size overflow accepted");
var pcCompressed=File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0])!,"synthetic-pc-changes-compressed-fixture.json"));
using var compressedMetadata=JsonDocument.Parse(pcCompressed);var pcClient=compressedMetadata.RootElement.GetProperty("recipientClientId").GetString()!;
var pcLogical=JsonSerializer.Deserialize<MobileChangePart>(MobileChangeCompression.Open(pcCompressed,pcClient))!;pcLogical.Validate(pcClient);Check(pcLogical.operations.Length>0 && MobileChangePart.Hash(pcLogical.operations)==pcLogical.manifestHash,"Actual PC Brotli fixture accepted by Android codec");
var longName="Synthetic "+new string('я',400);
string Snapshot(string name)=>JsonSerializer.Serialize(new{format="MAVKA_UNIT_ASSETS_V1",unit="One",asOf="2026-10-20",items=new[]{new{name,unit="од.",quantity=3m,price=1.123456789m}}});
Check(MavkaExchangeParser.TryAssets(Snapshot(longName),out var exactSnapshot) && exactSnapshot!.Items.Single().Name==longName && exactSnapshot.Items.Single().UnitPrice==1.123456789m,"Full long catalogue name survives stock parsing without shortening or price changes");
Check(!MavkaExchangeParser.TryAssets(Snapshot(new string('a',1025)),out _),"Catalogue name bound remains enforced");
var longInvoice=JsonSerializer.Serialize(new{format="MAVKA_DOCUMENT_V1",type="invoice",documentId="synthetic-long-name",documentNumber="1",documentDate="2026-10-20",recipient="One",positions=new[]{new{name=longName,unit="од.",quantity=3m,price=1.123456789m}}});
Check(MavkaExchangeParser.TryInvoice(longInvoice,out var exactInvoice) && exactInvoice!.Positions.Single().Name==longName,"Full long catalogue name survives invoice parsing");
Check(!ForegroundPollingPolicy.CanRun(false,true,false),"Foreground polling defaults/opt-out cannot run");
Check(!ForegroundPollingPolicy.CanRun(true,false,false),"Paused activity never polls");
Check(!ForegroundPollingPolicy.CanRun(true,true,true),"Existing operation prevents overlapping polls");
Check(ForegroundPollingPolicy.CanRun(true,true,false),"Only enabled resumed idle activity can poll");
Check(ForegroundPollingPolicy.Minutes(99)==2 && ForegroundPollingPolicy.Delay(2,0)==TimeSpan.FromMinutes(2) && ForegroundPollingPolicy.Delay(2,4)==TimeSpan.FromMinutes(10),"Polling intervals validated and error retries bounded");
var allMail=Enumerable.Range(1,300).Select(i=>(uint)i).ToArray();
var keyMail=new HashSet<uint>{299,300};var changesMail=allMail.Take(298).ToHashSet();
var batchMail=GmailProcessingSelectionPolicy.PayloadBatch(allMail.Reverse(),keyMail,changesMail,new HashSet<uint>());
Check(batchMail.Count==3 && batchMail.Take(2).SequenceEqual(new uint[]{299,300}),"Bounded Gmail batch prioritizes key provisioning before changes");
var remainingMail=allMail.ToHashSet();var completedMail=new HashSet<uint>();
while(remainingMail.Count>0)
{
    var portion=GmailProcessingSelectionPolicy.PayloadBatch(remainingMail,keyMail,changesMail,new HashSet<uint>());
    Check(portion.Count is >0 and <=3,"Gmail cycle keeps a bounded decrypt budget");
    foreach(var uid in portion){if(!completedMail.Add(uid))throw new Exception("Gmail duplicate cycle");remainingMail.Remove(uid);}
}
Check(completedMail.Count==300,"Every queued multipart mail remains eligible across successive completed cycles");
Console.WriteLine($"PASS {checks} PC → Android V2 bootstrap, shared password, expiry, tamper, roles and foreground polling checks (synthetic)");

// Repair responses must remain bound to the report actually requested on this phone.
var repairClient=Guid.NewGuid().ToString("N");var repairSource=new string('a',64);var repairId=Guid.NewGuid().ToString("N");var repairHash=MobileStateRequest.Hash(new());
var repairPolicy=new MobileAccessPolicy(MobileAccessPolicy.Format,repairClient,1,false,new[]{new MobileUnitGrant(1,"One")},true,true,false,true,false);
var repair=new MobileChangePart(MobileChangePart.Format,repairClient,repairSource,Guid.NewGuid().ToString("N"),6,5,false,"2026-10-23",repairPolicy,0,1,MobileChangePart.Hash(Array.Empty<MobileChangeOperation>()),Array.Empty<MobileChangeOperation>(),false,repairId,2,repairHash,repairHash);
repair.Validate(repairClient);
Check(MobileStateRequest.MatchesResponse(repair,repairId,repairSource,2,repairHash),"Requested incremental bridge skips missing sequences without resetting records");
Check(!MobileStateRequest.MatchesResponse(repair,repairId,repairSource,3,repairHash),"Changed applied state rejects stale report response");
Check(!MobileStateRequest.MatchesResponse(repair,Guid.NewGuid().ToString("N"),repairSource,2,repairHash),"Superseded request cannot apply");
Check(!MobileStateRequest.MatchesResponse(repair,repairId,new string('b',64),2,repairHash),"Another accounting source cannot apply");
Check(!MobileStateRequest.MatchesResponse(repair,repairId,repairSource,2,new string('c',64)),"Changed report hash cannot apply");
Check(!MobileStateRequest.MatchesResponse(repair with{full=true},repairId,repairSource,2,repairHash),"Request reply never clears existing mirror");
Check(!MobileStateRequest.MatchesResponse(repair,repairId,repairSource,6,repairHash),"Applied response cannot replay");
Console.WriteLine("PASS Android state-report bridge, stale/duplicate/source/hash and non-reset guards");
// The operator's 34-letter update drains in one session, including later UIDs
// when an early ordinary mail is unsupported. Failed mail remains for retry.
var refresh=new GmailRefreshSession();var mailbox=Enumerable.Range(1,34).Select(i=>(Validity:7u,Uid:(uint)i)).ToArray();
var stored=new HashSet<uint>();
while(true)
{
    var batch=mailbox.Where(m=>!refresh.Attempted.Contains(m)).Take(3).ToArray();
    if(!refresh.Register(batch))break;
    foreach(var mail in batch.Where(m=>m.Uid>2))stored.Add(mail.Uid);
}
Check(refresh.Passes==12 && refresh.Messages==34 && stored.Count==32,"One action drains34 messages; two blocked letters cannot starve later parts");
Check(!refresh.Register(new[]{(7u,34u)}),"No-progress/repeated UID stops instead of spinning");
Check(refresh.Register(new[]{(8u,1u)}),"UIDVALIDITY reset never skips reused UID");
var freshSession=new GmailRefreshSession();Check(freshSession.Register(new[]{(7u,1u)}),"Unprocessed mail is eligible again next user refresh");
var bounded=new GmailRefreshSession();for(uint i=1;i<=GmailRefreshSession.MaximumPasses;i++)Check(bounded.Register(new[]{(1u,i)}),"Bounded refresh accepts distinct pages");
Check(bounded.AtLimit && !bounded.Register(new[]{(1u,999u)}),"One action has a finite page budget");
Console.WriteLine("PASS automatic Gmail drain34 messages, blocked UID fairness, mailbox reset, resumable new pass and finite session limit");