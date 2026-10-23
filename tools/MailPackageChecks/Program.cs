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
