using System.Text.Json;
using System.Security.Cryptography;
using Mavka.Mobile;

var checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
void Reject(Action action,string message){try{action();}catch(Exception){checks++;return;}throw new Exception(message);}
using var fixture=JsonDocument.Parse(File.ReadAllText(args[0]));var f=fixture.RootElement;
var now=DateTimeOffset.FromUnixTimeSeconds(f.GetProperty("now").GetInt64());
var client=f.GetProperty("clientId").GetString()!;var device=f.GetProperty("deviceId").GetString()!;
var qr=f.GetProperty("qr").GetString()!;var envelope=f.GetProperty("envelope").GetString()!;
var invitation=BootstrapProtocol.Invitation(qr,client,device,now);var keys=BootstrapProtocol.Open(envelope,invitation,now);
Check(keys.generation==1 && keys.access.canConfirmInvoices && !keys.access.autoPostInvoices,"MVO contract");
Check(keys.documentPassword=="SYNTHETIC-DOCUMENT-PASSWORD-ONLY","PC password reaches Android decoder");
Check(keys.currentKey!=keys.futureKey && Convert.FromBase64String(keys.currentKey).Length==32,"Key pair");
Reject(()=>BootstrapProtocol.Invitation(qr,Guid.NewGuid().ToString("N"),device,now),"Wrong client accepted");
Reject(()=>BootstrapProtocol.Invitation(qr,client,Guid.NewGuid().ToString(),now),"Wrong phone accepted");
Reject(()=>BootstrapProtocol.Invitation(qr,client,device,now.AddMinutes(11)),"Expired QR accepted");
Reject(()=>BootstrapProtocol.Open(envelope,invitation with{messageId=Guid.NewGuid().ToString()},now),"Different packet accepted");
Reject(()=>BootstrapProtocol.Open(envelope,invitation,now.AddMinutes(11)),"Expired package accepted");
using var parsed=JsonDocument.Parse(envelope);var cipher=parsed.RootElement.GetProperty("ciphertext").GetString()!;
var tampered=System.Text.Json.Nodes.JsonNode.Parse(envelope)!;var bytes=Convert.FromBase64String(cipher);bytes[0]^=1;tampered["ciphertext"]=Convert.ToBase64String(bytes);
Reject(()=>BootstrapProtocol.Open(tampered.ToJsonString(),invitation,now),"Tamper accepted");
var observer=keys.access with{isMvo=false,canConfirmInvoices=false,autoPostInvoices=true,units=[new(1,"One"),new(2,"Two")]};observer.Validate(client);
Check(observer.Allows(1) && observer.Allows(2) && !observer.Allows(3),"Multi-unit observer boundaries");
Reject(()=>(observer with{isMvo=true}).Validate(client),"MVO multi-unit accepted");
Reject(()=>(observer with{canConfirmInvoices=true}).Validate(client),"Observer confirmation accepted");
using var ack=JsonDocument.Parse(BootstrapProtocol.Confirmation(keys,now));
Check(ack.RootElement.GetProperty("messageId").GetString()==invitation.messageId,"Bound installation confirmation");
Console.WriteLine($"PASS {checks} PC → Android bootstrap, AES, expiry, tamper and role checks (synthetic)");
