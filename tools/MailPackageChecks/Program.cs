using MimeKit;
using System.Security.Cryptography;
using System.Text;

var checks=0;void Check(bool condition,string label){if(!condition)throw new InvalidDataException(label);checks++;}
using var original=new MimeMessage();
original.From.Add(new MailboxAddress("Головна Мавка","main@example.invalid"));
original.To.Add(new MailboxAddress("Користувач","phone@example.invalid"));
original.MessageId="synthetic-protected-parts@mavka.local";original.Subject="MAVKA_ENCRYPTED_V1";
var builder=new BodyBuilder{TextBody="Захищене повідомлення Мавки."};
var payloads=Enumerable.Range(0,20).Select(i=>Encoding.UTF8.GetBytes("{\"format\":\"SYNTHETIC\",\"index\":"+i+",\"data\":\""+Convert.ToBase64String(RandomNumberGenerator.GetBytes(6000+i))+"\"}")).ToArray();
for(var i=0;i<payloads.Length;i++)builder.Attachments.Add($"Зміни-{i}.json",payloads[i],new ContentType("application","json"));
original.Body=builder.ToMessageBody();using var bytes=new MemoryStream();await original.WriteToAsync(bytes);bytes.Position=0;
using var restored=await MimeMessage.LoadAsync(bytes);
Check(restored.Subject==original.Subject && restored.MessageId==original.MessageId,"Protocol subject/stable Message-ID changed");
Check(restored.From.Mailboxes.Single().Address=="main@example.invalid" && restored.To.Mailboxes.Single().Address=="phone@example.invalid","Routing changed");
Check(restored.TextBody==builder.TextBody,"Ukrainian text changed");
var attachments=restored.Attachments.Cast<MimePart>().ToArray();Check(attachments.Length==20,"Bundle attachment count changed");
for(var i=0;i<attachments.Length;i++){using var decoded=new MemoryStream();await (attachments[i].Content??throw new InvalidDataException("Missing MIME content")).DecodeToAsync(decoded);Check(decoded.ToArray().SequenceEqual(payloads[i]) && attachments[i].FileName==$"Зміни-{i}.json","Protected payload/file name changed");}
Console.WriteLine($"PASS {checks} MIME protected-bundle, routing, stable-ID and Ukrainian metadata checks. NO NETWORK.");

// Cached flat MIME retrieval is byte-identical to on-demand attachment reads.
Mavka.Mobile.GmailChangeMessageCache.Validate(restored,"main@example.invalid",original.Subject);
for(var i=0;i<attachments.Length;i++){
var descriptor=new MailKit.BodyPartBasic(new ContentType("application","json"),(i+2).ToString(System.Globalization.CultureInfo.InvariantCulture));
Check(ReferenceEquals(Mavka.Mobile.GmailChangeMessageCache.Part(restored,descriptor),attachments[i]),"Cached MIME part path preserves exact attachment order");
}
Check(Mavka.Mobile.GmailChangeMessageCache.Part(restored,new MailKit.BodyPartBasic(new ContentType("application","json"),"2.1")) is null,"Nested MIME falls back without guessing a part");
Check(Mavka.Mobile.GmailChangeMessageCache.Part(restored,new MailKit.BodyPartBasic(new ContentType("application","json"),"99")) is null,"Missing MIME part is never substituted");
try{Mavka.Mobile.GmailChangeMessageCache.Validate(restored,"other@example.invalid",original.Subject);throw new Exception("Wrong sender accepted");}catch(InvalidDataException){checks++;}
try{Mavka.Mobile.GmailChangeMessageCache.Validate(restored,"main@example.invalid","Changed subject");throw new Exception("Wrong protocol subject accepted");}catch(InvalidDataException){checks++;}
Console.WriteLine($"PASS {checks} MIME compatibility/cache path, exact sender/subject, missing/nested safety checks. NO NETWORK.");var structure=new MailKit.BodyPartMultipart(new ContentType("multipart","mixed"),"");
structure.BodyParts.Add(new MailKit.BodyPartText(new ContentType("text","plain"),"1"));
for(var i=0;i<20;i++)structure.BodyParts.Add(new MailKit.BodyPartBasic(new ContentType("application","json"),(i+2).ToString()) {Octets=10000,ContentDisposition=new ContentDisposition("attachment"){FileName=$"Зміни-{i}.json"}});
var summary=new MailKit.MessageSummary(0){Body=structure,Size=200000};
Check(Mavka.Mobile.GmailChangeMessageCache.Eligible(summary,true),"Known bounded flat change bundle is fetched once");
Check(!Mavka.Mobile.GmailChangeMessageCache.Eligible(summary,false),"Ordinary mail is never bulk fetched");
summary.Size=4000001;Check(!Mavka.Mobile.GmailChangeMessageCache.Eligible(summary,true),"Oversized mail retains bounded on-demand retrieval");
Console.WriteLine($"PASS {checks} protected MIME and bounded bulk-fetch eligibility checks");