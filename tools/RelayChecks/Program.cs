using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mavka.Mobile;

int passed = 0;
void Check(bool valid, string name) { if (!valid) throw new Exception(name); passed++; }
void Reject(Action action, string name) { try { action(); } catch { passed++; return; } throw new Exception("Accepted: " + name); }
JsonElement Parse(string text) { using var d = JsonDocument.Parse(text); return d.RootElement.Clone(); }
const string sender = "synthetic-sender-0001", recipient = "synthetic-recipient-0001";
var nonce = RelayProtocol.B64(new byte[16]);
var canonical = Encoding.UTF8.GetString(RelayProtocol.Canonical("GET", "/v1/messages?limit=10&afterTime=1", sender, "1780000000", nonce, ""));
Check(canonical == "MAVKA_RELAY_REQUEST_V1\n" + RelayProtocol.Origin + "\nGET\n/v1/messages?limit=10&afterTime=1\n" +
    sender + "\n1780000000\nAAAAAAAAAAAAAAAAAAAAAA\ne3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "canonical query and empty body");
Reject(() => RelayProtocol.UnB64(nonce + "=", 16), "padded URL");
Reject(() => RelayProtocol.Canonical("POST", "/v1/messages\nother", sender, "1780000000", nonce, "{}"), "path injection");
using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var der = signing.SignData(Encoding.UTF8.GetBytes("der"), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
var raw = RelayProtocol.RawSignature(der);
Check(signing.VerifyData(Encoding.UTF8.GetBytes("der"), raw, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), "Android DER to P1363");
Reject(() => RelayProtocol.RawSignature(der.Concat(new byte[] { 0 }).ToArray()), "trailing DER");
var key = RandomNumberGenerator.GetBytes(32); const string keyId = "0123456789abcdef";
var content = "{\"format\":\"MAVKA_DOCUMENT_V1\",\"text\":\"" + string.Concat(Enumerable.Repeat("Україна + / = ", 8000)) + "\"}";
var encrypted = RelayContentCipher.Encrypt(content, key, keyId);
Check(RelayContentCipher.Decrypt(encrypted, id => id == keyId ? key.ToArray() : null) == content, "large inner AES exact bytes");
Reject(() => RelayContentCipher.Decrypt(encrypted, _ => new byte[32]), "wrong AES key");
var tamper = JsonNode.Parse(encrypted)!; tamper["keyId"] = "fedcba9876543210";
Reject(() => RelayContentCipher.Decrypt(tamper.ToJsonString(), _ => key.ToArray()), "AAD keyId tamper");
var chunks = RelayProtocol.Split(" \n" + encrypted + "\n ", "client-0001");
var parts = chunks.Select(x => RelayProtocol.Part(x, "client-0001")!).ToArray();
Check(parts.Length > 2 && parts.All(p => p.count == parts.Length && p.size == Encoding.UTF8.GetByteCount(" \n" + encrypted + "\n ")), "content header exact UTF8 size");
Check(RelayProtocol.Assemble(parts.Reverse().Concat(parts.Take(1)).ToArray()) == " \n" + encrypted + "\n ", "out of order and exact duplicate");
Check(RelayProtocol.Assemble(parts.Skip(1).ToArray()) is null, "missing part never imports");
Reject(() => RelayProtocol.Part(chunks[0], "other-client"), "wrong client");
Reject(() => RelayProtocol.Assemble(parts.Append(parts[0] with { clientId = "other-client" }).ToArray()), "mixed client");
var bad = Convert.FromBase64String(parts[0].data); bad[0] ^= 1;
Reject(() => RelayProtocol.Assemble(parts.Append(parts[0] with { data = Convert.ToBase64String(bad) }).ToArray()), "conflicting duplicate");
Reject(() => RelayProtocol.Assemble(parts.Select(p => p with { sha256 = new string('0', 64) }).ToArray()), "whole hash mismatch");
Reject(() => RelayProtocol.Assemble(parts.Select(p => p with { size = p.size + 1 }).ToArray()), "size mismatch");
var upper = JsonNode.Parse(chunks[0])!; upper["transferId"] = new string('A', 32);
Reject(() => RelayProtocol.Part(upper.ToJsonString()), "uppercase transfer");
var base64 = JsonNode.Parse(chunks[0])!; base64["data"] = parts[0].data + "\n";
Reject(() => RelayProtocol.Part(base64.ToJsonString()), "noncanonical Base64");
var fields = JsonNode.Parse(chunks[0])!; fields["totalBytes"] = parts[0].size;
Reject(() => RelayProtocol.Part(fields.ToJsonString()), "old totalBytes field");
Reject(() => RelayProtocol.Split(content, "client-0001"), "plaintext multipart");
foreach (var chunk in chunks)
{
    var outer = RelayContentCipher.Encrypt(chunk, key, keyId);
    Check(Encoding.UTF8.GetByteCount(chunk) < 32768 && RelayProtocol.Envelope(Parse(outer)).GetProperty("format").GetString() == "MAVKA_ENCRYPTED_V1", "chunk fits outer Relay");
}
var payload = RelayProtocol.Envelope(Parse(RelayContentCipher.Encrypt("{\"format\":\"TEST\",\"text\":\"Україна + / =\"}", key, keyId)));
var body = RelayProtocol.Serialize(new { messageId = "synthetic-message-0001", recipientDeviceId = recipient,
    expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), payload });
var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
var signature = RelayProtocol.B64(signing.SignData(RelayProtocol.Canonical("POST", "/v1/messages", sender, time, nonce, body),
    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
var trustedKey = RelayProtocol.Jwk(signing.ExportSubjectPublicKeyInfo());
var delivery = JsonSerializer.SerializeToElement(new { messageId = "synthetic-message-0001", senderDeviceId = sender,
    expiresAt = Parse(body).GetProperty("expiresAt").GetInt64(), payloadSha256 = RelayProtocol.Fingerprint(sender, Parse(body)),
    packet = new { format = "MAVKA_RELAY_STORED_V1", senderDeviceId = sender, time, nonce, signature, requestBody = body } }, RelayProtocol.Json);
RelayVerified Verify(JsonElement d, string r = recipient) => RelayProtocol.VerifyFrom(d, r, DateTimeOffset.UtcNow, sender, trustedKey);
Check(Verify(delivery).Id == "synthetic-message-0001", "trusted delivery signature");
Reject(() => Verify(delivery, "other-recipient-0001"), "recipient tamper");
Reject(() => RelayProtocol.Verify(delivery, recipient, DateTimeOffset.UtcNow), "synthetic signer cannot impersonate pinned root");
var changed = JsonNode.Parse(delivery.GetRawText())!; changed["packet"]!["requestBody"] = body + " ";
Reject(() => Verify(Parse(changed.ToJsonString())), "exact body signature tamper");
changed = JsonNode.Parse(delivery.GetRawText())!; changed["payloadSha256"] = new string('0', 64);
Reject(() => Verify(Parse(changed.ToJsonString())), "fingerprint tamper");
Reject(() => RelayProtocol.VerifyFrom(delivery, recipient, DateTimeOffset.UtcNow.AddHours(2), sender, trustedKey), "expired delivery");
if (args.Length == 2 && args[0] == "export")
    File.WriteAllText(args[1], RelayProtocol.Serialize(new { trustedKey, sender, recipient, delivery }));
if (args.Length == 2 && args[0] == "verify-node")
{
    using var fixture = JsonDocument.Parse(File.ReadAllText(args[1])); var root = fixture.RootElement;
    var nodeKey = JsonSerializer.Deserialize<RelayJwk>(root.GetProperty("trustedKey"), RelayProtocol.Json)!;
    Check(RelayProtocol.VerifyFrom(root.GetProperty("delivery"), root.GetProperty("recipient").GetString()!,
        DateTimeOffset.UtcNow, root.GetProperty("sender").GetString()!, nodeKey).Id == "synthetic-message-0001", "Node signed delivery accepted by C#");
}
CryptographicOperations.ZeroMemory(key);
Console.WriteLine("Relay protocol, AES, content parts: " + passed + " checks passed (synthetic only).");
