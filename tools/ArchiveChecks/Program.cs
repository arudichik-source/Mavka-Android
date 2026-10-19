using Mavka.Mobile;

void Check(bool success, string message)
{
    if (!success) throw new InvalidOperationException(message);
}
Check(PhoneNumberNormalizer.Normalize("067 123 45 67") == "+380671234567", "UA local");
Check(PhoneNumberNormalizer.Normalize("380671234567") == "+380671234567", "UA international");
Check(PhoneNumberNormalizer.Normalize("+48 500 100 200") == "+48500100200", "International");
Check(PhoneNumberNormalizer.Normalize(null) == "", "Legacy profile with no phone");
foreach (var bad in new[] { "a123456789", "++380671234567", "067123", "+0123456789" })
{
    bool rejected = false;
    try { PhoneNumberNormalizer.Normalize(bad); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "Invalid phone accepted: " + bad);
}
const string unit = "Тестова частина";
const string validInvoice = """
{"format":"MAVKA_DOCUMENT_V1","type":"invoice",
 "documentId":"LOCAL-001","documentNumber":"7","recipient":"Тестова частина",
 "date":"2026-10-11","positions":[
 {"name":"Канцелярський папір","unit":"пач.","quantity":2,"price":100.55,"nsn":"TEST"}]}
""";
Check(ArchiveDocumentClassifier.TryClassify(validInvoice, unit, out var invoice)
    && invoice is not null && invoice.DocumentId == "LOCAL-001"
    && invoice.Kind == "Накладна", "Invoice classification");
Check(!ArchiveDocumentClassifier.TryClassify(validInvoice, "Інша частина", out _),
    "Cross-unit archive must be rejected");
const string snapshot = """
{"format":"MAVKA_UNIT_ASSETS_V1","unit":"Тестова частина",
 "asOf":"2026-10-01","items":[{"name":"Папір","unit":"пач.","quantity":1}]}
""";
Check(ArchiveDocumentClassifier.TryClassify(snapshot, unit, out var first)
    && ArchiveDocumentClassifier.TryClassify(snapshot, unit, out var second)
    && first!.DocumentId == second!.DocumentId
    && first.DocumentId.StartsWith("SNAPSHOT:", StringComparison.Ordinal),
    "Stable snapshot deduplication ID");
const string movements = """
{"format":"MAVKA_UNIT_MOVEMENTS_V1","unit":"Тестова частина",
 "asOf":"2026-10-11","entries":[
 {"type":"writeoff","documentId":"W-1","documentNumber":"5",
 "date":"2026-10-10","name":"Папір","unit":"пач.","quantity":1}]}
""";
Check(ArchiveDocumentClassifier.TryClassify(movements, unit, out var journal)
    && journal is not null && journal.Kind == "Довідкова історія",
    "Read-only history classification");
Check(!ArchiveDocumentClassifier.TryClassify("{}", unit, out _), "Unknown attachment");
Check(!ArchiveDocumentClassifier.TryClassify(
    validInvoice.Replace("\"quantity\":2", "\"quantity\":-9"), unit, out _),
    "Invalid quantity cannot be archived");
// The real IMAP intake previously rejected 200-position encrypted documents
// before the invoice parser could see them. Exercise a >65 KB valid payload.
var expanded = System.Text.Json.JsonSerializer.Serialize(new
{
    format = "MAVKA_DOCUMENT_V1", type = "invoice",
    documentId = "DEMO-LARGE-200", documentNumber = "TEST-200",
    recipient = unit, date = "2026-10-12",
    positions = Enumerable.Range(1, 200).Select(i => new
    {
        name = "Канцелярська позиція " + i + new string('X', 165),
        unit = "шт.", quantity = 1, price = 1.25m,
        nsn = "TEST-" + i + new string('Z', 155)
    }).ToArray()
});
Check(expanded.Length > 65000 && expanded.Length < 196608,
    "Large invoice test fixture must exceed old intake cap.");
Check(MavkaExchangeParser.TryInvoice(expanded, out var large) &&
    large is not null && large.Positions.Count == 200,
    "Valid long 200-position invoice must parse.");
Check(ArchiveDocumentClassifier.TryClassify(expanded, unit, out var accepted) &&
    accepted is not null && accepted.DocumentId == "DEMO-LARGE-200",
    "Large invoice must remain eligible for durable archive.");
// The archive UI must never render more than 50 documents in one page.
var boundaryCases = new[]
{
    (Total: 0, Index: 0, Pages: 1, Start: 0, Count: 0),
    (Total: 1, Index: 0, Pages: 1, Start: 0, Count: 1),
    (Total: 50, Index: 0, Pages: 1, Start: 0, Count: 50),
    (Total: 51, Index: 0, Pages: 2, Start: 0, Count: 50),
    (Total: 51, Index: 1, Pages: 2, Start: 50, Count: 1),
    (Total: 100, Index: 1, Pages: 2, Start: 50, Count: 50),
    (Total: 101, Index: 2, Pages: 3, Start: 100, Count: 1),
    (Total: 151, Index: 2, Pages: 4, Start: 100, Count: 50),
    (Total: 151, Index: 3, Pages: 4, Start: 150, Count: 1)
};
foreach (var item in boundaryCases)
{
    var page = DocumentPageWindow.For(item.Total, item.Index);
    Check(page.PageCount == item.Pages && page.PageIndex == item.Index &&
        page.StartIndex == item.Start && page.ItemCount == item.Count &&
        page.ItemCount <= 50, $"Pagination boundary failed at {item.Total}/{item.Index}");
}
var clampedHigh = DocumentPageWindow.For(101, 100);
Check(clampedHigh.PageIndex == 2 && clampedHigh.ItemCount == 1,
    "Clamped final archive page");
var clampedLow = DocumentPageWindow.For(100, -99);
Check(clampedLow.PageIndex == 0 && clampedLow.ItemCount == 50,
    "Clamped initial archive page");
var emptyAfterFilter = DocumentPageWindow.For(0, 17);
Check(emptyAfterFilter.PageIndex == 0 && emptyAfterFilter.PageCount == 1,
    "Empty search resets page bounds");
var huge = DocumentPageWindow.For(int.MaxValue, int.MaxValue);
Check(huge.ItemCount > 0 && huge.ItemCount <= 50 &&
    huge.StartIndex + huge.ItemCount == int.MaxValue,
    "Large document count remains bounded and overflow safe");
var invalidCountRejected = false;
try { DocumentPageWindow.For(-1, 0); }
catch (ArgumentOutOfRangeException) { invalidCountRejected = true; }
Check(invalidCountRejected, "Negative count rejected");
// Archive retention: posted documents are never destructively removed.
Check(ArchiveTrashPolicy.CanMoveToTrash("not_posted"),
    "Unposted document can move to recoverable trash");
Check(!ArchiveTrashPolicy.CanMoveToTrash("posted"),
    "Posted invoice cannot be removed by a document UI action");
var now = DateTimeOffset.UtcNow;
Check(ArchiveTrashPolicy.CanRestore(now.AddDays(-29).ToString("O"), now),
    "29-day archive trash can be restored");
Check(!ArchiveTrashPolicy.CanRestore(now.AddDays(-31).ToString("O"), now),
    "Expired archive trash cannot be restored");
Check(ArchiveTrashPolicy.CanPurge(now.AddDays(-31).ToString("O"), now),
    "Expired archive trash can be explicitly purged");
Check(!ArchiveTrashPolicy.CanPurge(now.AddDays(-29).ToString("O"), now),
    "Non-expired archive may never be purged");

// The QR alone does not grant access: the desktop must produce a valid HMAC
// under the secret in the 10-minute QR invitation.
var invitationJson = QrPairingProtocol.Generate("main@example.com", now);
var invitation = QrPairingProtocol.ParseInvitation(invitationJson, now);
const string clientId = "synthetic-client-123";
var qrAck = QrPairingProtocol.CreateAck(invitation, clientId);
Check(QrPairingProtocol.VerifyAck(qrAck, invitation, clientId, now),
    "Valid main-device QR confirmation");
Check(!QrPairingProtocol.VerifyAck(qrAck, invitation, "another-client", now),
    "QR reply bound to exact mobile client");
Check(!QrPairingProtocol.VerifyAck(qrAck, invitation, clientId, now.AddMinutes(11)),
    "QR confirmation expires after ten minutes");
var separate = QrPairingProtocol.ParseInvitation(
    QrPairingProtocol.Generate("main@example.com", now), now);
Check(!QrPairingProtocol.VerifyAck(qrAck, separate, clientId, now),
    "QR reply bound to particular pairing session");
Check(!QrPairingProtocol.VerifyAck(
        qrAck.Replace("MAVKA_QR_PAIR_ACK_V1","FORGED"), invitation, clientId, now),
    "Tampered QR response rejected");

// Portable AES-256-GCM backup tests without any Android Keystore.
const string syntheticBackup = "{\"unit\":\"Тестовий підрозділ\",\"items\":[1,2,3]}";
var encryptedBackup = PortableBackupEnvelope.Encrypt(
    syntheticBackup, "SYNTHETIC-BACKUP-PASSWORD-TEST");
Check(PortableBackupEnvelope.Decrypt(
    encryptedBackup, "SYNTHETIC-BACKUP-PASSWORD-TEST") == syntheticBackup,
    "Portable encrypted backup exact roundtrip");
bool rejectedWrongPassword = false;
try { PortableBackupEnvelope.Decrypt(encryptedBackup, "ANOTHER-SYNTHETIC-SECRET"); }
catch (System.Security.Cryptography.CryptographicException) { rejectedWrongPassword = true; }
Check(rejectedWrongPassword, "Wrong backup password authentication rejected");
Check(!System.Text.Encoding.UTF8.GetString(encryptedBackup).Contains(
    "Тестовий підрозділ", StringComparison.Ordinal),
    "Portable backup never stores unencrypted unit name");
Console.WriteLine("Archive UI pagination: 14/14 checks PASS");
Console.WriteLine("Archive and profile regression checks: PASS (includes 200 long rows)");
