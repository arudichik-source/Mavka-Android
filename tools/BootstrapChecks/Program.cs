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
Console.WriteLine($"PASS {checks} PC → Android V2 bootstrap, shared password, expiry, tamper and role checks (synthetic)");
