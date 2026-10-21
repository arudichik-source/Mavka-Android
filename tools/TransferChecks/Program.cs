using Mavka.Mobile;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
void Reject(Action action, string name) { try { action(); } catch (Exception) { checks++; return; } throw new Exception(name); }
var key = RandomNumberGenerator.GetBytes(32);
var localKey = RandomNumberGenerator.GetBytes(32);
byte[] Protect(byte[] plain) { var nonce = RandomNumberGenerator.GetBytes(12); var c = new byte[plain.Length]; var tag = new byte[16]; using var aes = new AesGcm(localKey, 16); aes.Encrypt(nonce, plain, c, tag); return nonce.Concat(c).Concat(tag).ToArray(); }
byte[] Unprotect(byte[] bytes) { var plain = new byte[bytes.Length - 28]; using var aes = new AesGcm(localKey, 16); aes.Decrypt(bytes[..12], bytes[12..^16], bytes[^16..], plain); return plain; }
byte[]? Resolve(string id) => id == Convert.ToHexString(SHA256.HashData(key))[..16].ToLowerInvariant() ? (byte[])key.Clone() : null;
var root = Path.Combine(Path.GetTempPath(), "mavka-transfer-" + Guid.NewGuid().ToString("N"));
try
{
    var data = new byte[800_123]; Encoding.ASCII.GetBytes("%PDF-1.7\nSYNTHETIC ONLY").CopyTo(data, 0);
    var h = MavkaFileParts.Describe("тест.pdf", data);
    var packets = Enumerable.Range(0, h.Count).Select(i => MavkaFileParts.Encrypt(h with { Index = i },
        data.AsSpan(i * MavkaFileParts.ChunkBytes, Math.Min(MavkaFileParts.ChunkBytes, data.Length - i * MavkaFileParts.ChunkBytes)), key)).ToArray();
    Check(h.Count == 4, "4 parts");
    var store = new PendingFileTransferStore(root, Protect, Unprotect);
    var mail = new PendingFileTransferStore.MailRef(42, 77);
    store.Accept(packets[3], Resolve, mail); store.Accept(packets[0], Resolve, mail);
    Check(store.Assemble(h.TransferId) is null, "Incomplete cannot assemble");
    Check(store.CleanupCandidates(h.TransferId).Count == 0, "Incomplete cannot trash mail");
    store = new PendingFileTransferStore(root, Protect, Unprotect);
    Check(store.List().Single().Received == 2, "Restart retains progress");
    Check(store.Accept(packets[0], Resolve, mail).Received == 2, "Duplicate is idempotent");
    store.Accept(packets[2], Resolve, mail); store.Accept(packets[1], Resolve, mail);
    using (var assembled = store.Assemble(h.TransferId))
        Check(assembled is not null && assembled.Content.SequenceEqual(data), "Exact reverse-order whole-file roundtrip");
    Check(store.CleanupCandidates(h.TransferId).Count == 0, "Assembly alone does not authorize Trash");
    store.MarkSaved(h.TransferId, new string('a', 64));
    Check(store.CleanupCandidates(h.TransferId).Count == 1, "Only durable vault completion authorizes Trash");
    store.AcknowledgeMail(h.TransferId, mail); Check(store.CleanupCandidates(h.TransferId).Count == 0, "Cleanup retry checkpoint");
    var bad = JsonNode.Parse(packets[0])!; bad["metadata"]!["index"] = 1;
    Reject(() => MavkaFileParts.Decrypt(bad.ToJsonString(), Resolve), "Tampered index accepted");
    bad = JsonNode.Parse(packets[0])!; bad["metadata"]!["fileName"] = "other.pdf";
    Reject(() => MavkaFileParts.Decrypt(bad.ToJsonString(), Resolve), "Tampered filename accepted");
    bad = JsonNode.Parse(packets[0])!; bad["tag"] = Convert.ToBase64String(new byte[16]);
    Reject(() => store.Accept(bad.ToJsonString(), Resolve, mail), "Forged tag accepted");
    Reject(() => MavkaFileParts.Decrypt(packets[0], _ => null), "Missing key accepted");
    Reject(() => store.Delete("../"), "Deletion escaped scope");
    using var other = new MemoryStream(); using(var bounded = new BoundedWriteStream(10))
    { bounded.Write(new byte[10]); Reject(() => bounded.WriteByte(0), "Decoder overrun accepted"); }
    Check(MavkaEncryptedFileEnvelope.MimeFor("report.docx").Contains("wordprocessingml"), "DOCX MIME");
    var docBytes = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 1 };
    using (var word = MavkaEncryptedFileEnvelope.Decrypt(MavkaEncryptedFileEnvelope.Encrypt("report.doc", docBytes, key), Resolve))
        Check(word.Content.SequenceEqual(docBytes), "Encrypted Word exact roundtrip");
    var docxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0, 1 };
    using (var word = MavkaEncryptedFileEnvelope.Decrypt(MavkaEncryptedFileEnvelope.Encrypt("report.docx", docxBytes, key), Resolve))
        Check(word.Content.SequenceEqual(docxBytes), "Encrypted DOCX roundtrip");
    var command = new DriveFileCommand(DriveFileCommand.Kind, "TEST_FILE_123", "https://drive.google.com/uc?export=download&id=TEST_FILE_123", 200, new string('A',64), h.TransferId);
    Check(DriveFileCommand.TryRead(JsonSerializer.Serialize(command, MavkaFileParts.Json), out _), "Authorized Drive command shape");
    Check(!DriveFileCommand.TryRead(JsonSerializer.Serialize(command with { DownloadUrl = "http://127.0.0.1/TEST_FILE_123" }, MavkaFileParts.Json), out _), "Reject arbitrary download origin");
    Check(!DriveFileCommand.TryRead(JsonSerializer.Serialize(command with { EncryptedSize = int.MaxValue }, MavkaFileParts.Json), out _), "Reject oversized Drive command");
    Check((await DriveFileCommand.BoundedReadAsync(new MemoryStream(new byte[10]),10,default)).Length == 10, "Exact bounded download");
    try { await DriveFileCommand.BoundedReadAsync(new MemoryStream(new byte[11]),10,default); throw new Exception("Overrun accepted"); }
    catch (IOException) { checks++; }
    try { await DriveFileCommand.BoundedReadAsync(new MemoryStream(new byte[9]),10,default); throw new Exception("Truncation accepted"); }
    catch (IOException) { checks++; }
    // A 2 MB input expands under two base64 layers; wire and decoded caps differ.
    int jsonBytes = 4 * ((2_000_000 + 2) / 3) + 1000;
    int wireBase64 = 4 * ((jsonBytes + 2) / 3); int wire = wireBase64 + 2 * ((wireBase64 + 75) / 76);
    Check(jsonBytes < MavkaEncryptedFileEnvelope.MaxSingleEnvelopeBytes && wire < MavkaEncryptedFileEnvelope.MaxSingleWireBytes,
        "Full 2 MB file fits separate MIME/decoded bounds");
    var bundle = Encoding.UTF8.GetBytes("{\"format\":\"MAVKA_FILE_BUNDLE_V1\",\"parts\":[" + string.Join(",", packets) + "]}");
    var downloadable = command with { EncryptedSize = bundle.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bundle)) };
    var receivedParts = await downloadable.DownloadAsync(default,new FakeDriveReply(bundle));
    Check(receivedParts.Length == h.Count,"Actual Drive download/parse route with synthetic HTTP reply");
    var resumed = await downloadable.DownloadAsync(default, new FakeRangeDriveReply(bundle));
    Check(resumed.Length == h.Count,
        "Interrupted Drive download must request remaining bytes using HTTP Range");
    using(var received = MavkaFileParts.Decrypt(receivedParts[0],Resolve))
        Check(received.Content.SequenceEqual(data.Take(MavkaFileParts.ChunkBytes)),"Downloaded Drive part authenticates");
    try { await (downloadable with { Sha256 = new string('0',64) }).DownloadAsync(default,new FakeDriveReply(bundle)); throw new Exception("Wrong bundle hash accepted"); }
    catch(CryptographicException) { checks++; }
    try { await downloadable.DownloadAsync(default,new FakeDriveReply(bundle,"text/html")); throw new Exception("Drive sign-in HTML accepted"); }
    catch(IOException) { checks++; }
    try { await downloadable.DownloadAsync(default,new FakeDriveReply(bundle,redirect:"http://127.0.0.1/")); throw new Exception("Drive redirect escaped allowlist"); }
    catch(IOException) { checks++; }
    Reject(() => store.Accept(packets[0],Resolve,mail,Guid.NewGuid().ToString("N")),"Drive transfer binding accepted wrong ID");
    var outRoot = Path.Combine(root,"outgoing");
    var outgoing = new OutgoingFileQueue(outRoot,Protect,Unprotect);
    var batch = outgoing.Enqueue(h,i => packets[i]);
    Check(outgoing.Part(batch) == packets[0], "Encrypted outbox bytes");
    batch = outgoing.Advance(batch);
    outgoing = new OutgoingFileQueue(outRoot,Protect,Unprotect);
    Check(outgoing.List().Single().NextIndex == 1 && outgoing.Part(outgoing.List().Single()) == packets[1], "SMTP checkpoint survives restart");
    Check(outgoing.Enqueue(h with { TransferId = Guid.NewGuid().ToString("N") },_ => throw new Exception("Should not re-encrypt")).Metadata.TransferId == h.TransferId,"Reselection resumes existing batch");
    Reject(() => outgoing.Delete("../"),"Outgoing deletion escaped scope");
    outgoing.Delete(h.TransferId);
    Directory.Delete(outRoot);
    store.Delete(h.TransferId); Check(store.List().Count == 0, "Manual deletion of pending transfer");
    Console.WriteLine($"Encrypted multipart, Word, durable resume/deletion and Drive command checks: {checks} PASS");
}
finally { if (Directory.Exists(root)) Directory.Delete(root,true); CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(localKey); }

sealed class FakeRangeDriveReply(byte[] bytes) : HttpMessageHandler
{
    private int _calls;
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        int split = bytes.Length / 2;
        if (++_calls == 1)
        {
            if (request.Headers.Range is not null)
                throw new Exception("First Drive request unexpectedly has Range");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes[..split])
            });
        }
        var requested = request.Headers.Range?.Ranges.Single().From;
        if (requested != split)
            throw new Exception("Resume must start after last received encrypted byte");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(bytes[split..])
        };
        response.Content.Headers.ContentRange =
            new System.Net.Http.Headers.ContentRangeHeaderValue(
                split, bytes.Length - 1, bytes.Length);
        return Task.FromResult(response);
    }
}

sealed class FakeDriveReply(byte[] bytes, string? mime = null, string? redirect = null) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        var response = new HttpResponseMessage(redirect is null ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.Found)
        { Content = new ByteArrayContent(bytes) };
        if(mime is not null) response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
        if(redirect is not null) response.Headers.Location = new Uri(redirect);
        return Task.FromResult(response);
    }
}
