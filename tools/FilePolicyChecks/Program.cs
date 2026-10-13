using Mavka.Mobile;

void Check(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}
Check(MavkaExportPolicy.FolderTitle == "МаVКа файли", "Exact approved title");
Check(MavkaExportPolicy.Matches("Download/МаVКа файли/", "Накладна-1.pdf"),
    "Exact PDF folder");
Check(MavkaExportPolicy.Matches("Download/МаVКа файли/", "Акт.XLSX"),
    "Exact XLSX folder");
Check(!MavkaExportPolicy.Matches("Download/", "Накладна-1.pdf"),
    "Cannot touch unrelated downloads");
Check(!MavkaExportPolicy.Matches("Download/МаVКа файли/Сторонні/",
    "Накладна.pdf"), "No nested folder traversal");
Check(!MavkaExportPolicy.Matches("Download/МаVКа файли/", "../../data.pdf"),
    "No path separators");
Check(!MavkaExportPolicy.Matches("Download/МаVКа файли/", "data.sqlite"),
    "Never delete accounting database");
Check(!MavkaExportPolicy.Matches("Download/МаVКа файли/", "keys.json"),
    "Never delete keystore data");
Check(!MavkaExportPolicy.Matches(null, "copy.pdf"),
    "Unknown folder not eligible");
Check(!MavkaExportPolicy.Matches("Download/МаVКа файли/", null),
    "Unknown filename not eligible");
Check(MailAttachmentDisplayPolicy.Detect("preview.PDF") ==
    MailAttachmentDisplayPolicy.PreviewType.Pdf, "Recognize PDF metadata");
Check(MailAttachmentDisplayPolicy.Detect("schedule.XLSX") ==
    MailAttachmentDisplayPolicy.PreviewType.Excel, "Recognize XLSX metadata");
Check(MailAttachmentDisplayPolicy.Detect("script.exe") ==
    MailAttachmentDisplayPolicy.PreviewType.None, "Reject executable attachment");
Check(MailAttachmentDisplayPolicy.Detect("../doc.pdf") ==
    MailAttachmentDisplayPolicy.PreviewType.None, "Reject traversal in attachments");
Check(MailAttachmentDisplayPolicy.Detect("folder\\\\sample.pdf") ==
    MailAttachmentDisplayPolicy.PreviewType.None, "Reject separator");
Check(MailAttachmentDisplayPolicy.Description("demo.xlsx", 1536)
    .Contains("1.5 КБ"), "Display binary size metadata");
Check(MailAttachmentDisplayPolicy.Description("demo.pdf", 50)
    .Contains("не відкрито й не збережено"), "Disclose no import");
Check(PrivateVaultListingPolicy.MaxFiles == 100, "Vault quota contract");
var allIds = Enumerable.Range(1, 100)
    .Select(n => n.ToString("x64")).ToArray();
var allPaths = allIds.Select(id => "/app/secure_received_files_v1/" + id + ".bin");
var candidates = PrivateVaultListingPolicy.CandidateIds(allPaths);
Check(candidates.Count == 100, "All 100 permitted received files are visible");
Check(candidates.Distinct().Count() == 100, "No duplicate rows in file manager");
Check(allIds.All(id => candidates.Contains(id)), "Entries 61-100 are not hidden");
var withInvalid = new[] {
    "/app/secure_received_files_v1/unexpected.bin",
    "/app/secure_received_files_v1/../unsafe.bin",
    "/app/secure_received_files_v1/" + new string('Z', 64) + ".bin",
    "/app/secure_received_files_v1/" + allIds[0] + ".txt"
}.Concat(allPaths).ToArray();
Check(PrivateVaultListingPolicy.CandidateIds(withInvalid).Count == 100,
    "Reject invalid entries before taking 100");
Check(PrivateVaultListingPolicy.CandidateIds(allPaths.Concat(allPaths)).Count == 100,
    "Duplicate entries cannot crowd out valid files");
Check(PrivateVaultListingPolicy.CandidateIds(allPaths.Concat(
    new[] { "/app/secure_received_files_v1/" + (101).ToString("x64") + ".bin" })).Count == 100,
    "List honors bounded vault capacity");
Check(!PrivateVaultListingPolicy.IsId("../" + new string('a',60)),
    "No relative identifier traversal");
Check(MavkaExportPolicy.Matches("Download/МаVКа файли/", "Акт.docx"),
    "Allow DOCX exported copies");
Check(MavkaExportPolicy.Matches("Download/МаVКа файли/", "Звіт.DOC"),
    "Allow legacy DOC exported copies");
Check(!MavkaExportPolicy.Matches("Download/", "Звіт.docx"),
    "Do not treat unrelated Word files as Mavka-managed");
Check(!MavkaExportPolicy.Matches("Download/МаVКа файли/", "../secret.doc"),
    "Reject Word path traversal");
Check(MailImagePreviewPolicy.IsImage("photo.PNG") &&
    MailImagePreviewPolicy.IsImage("photo.jpeg") &&
    MailImagePreviewPolicy.IsImage("photo.webp"),
    "Image suffix detection");
Check(!MailImagePreviewPolicy.IsImage("run.exe"),
    "Reject executable image preview");
Check(MailImagePreviewPolicy.HasValidSignature("photo.png",
    new byte[] {137,80,78,71,13,10,26,10,0,0,0,0}),
    "PNG signature");
Check(MailImagePreviewPolicy.HasValidSignature("photo.jpg",
    new byte[] {255,216,255,1,2,3,4,5,6,7,8,9}),
    "JPEG signature");
Check(MailImagePreviewPolicy.HasValidSignature("photo.webp",
    "RIFF0000WEBP"u8),
    "WebP signature");
Check(!MailImagePreviewPolicy.HasValidSignature("photo.png",
    new byte[] {0,1,2,3,4,5,6,7,8,9,10,11}),
    "Reject altered PNG");
var statuses = new Dictionary<uint,string>
{
    [10] = "ready",
    [11] = "trashed",
    [12] = "pending",
    [13] = "ready"
};
var recent = new uint[] { 15, 14, 13, 12, 11, 10 };
Check(GmailProcessingSelectionPolicy.PendingPayloads(recent, statuses, 20)
    .SequenceEqual(new uint[] { 15, 14, 12 }),
    "Do not refetch ready/trashed mail; unresolved remains eligible");
Check(GmailProcessingSelectionPolicy.ReadyForTrash(recent, statuses, 20)
    .SequenceEqual(new uint[] { 13, 10 }),
    "Only verified ready mails are candidates for a Trash retry");
Check(GmailProcessingSelectionPolicy.ReadyForTrash(recent, statuses, 1)
    .SequenceEqual(new uint[] { 13 }),
    "Bounded ready-mail cleanup batch");
Check(GmailProcessingSelectionPolicy.PendingPayloads(recent, statuses, 1)
    .SequenceEqual(new uint[] { 15 }),
    "Bounded unprocessed-mail batch");
Check(!GmailProcessingSelectionPolicy.IsReady(statuses, 11) &&
    GmailProcessingSelectionPolicy.IsTrashed(statuses, 11),
    "Trashed is never re-imported");
Check(!GmailProcessingSelectionPolicy.IsReady(statuses, 12) &&
    !GmailProcessingSelectionPolicy.IsTrashed(statuses, 12),
    "Pending mail is not lost");
Check(GmailProcessingSelectionPolicy.PendingPayloads(
    new uint[]{42}, new Dictionary<uint,string>(), 20).SequenceEqual(new uint[]{42}),
    "Unseen attachment must still be processed");
Check(IncomingAttachmentSecurityPolicy.RejectPlaintext(true, false, false),
    "Plaintext accounting JSON is rejected after key installation");
Check(!IncomingAttachmentSecurityPolicy.RejectPlaintext(true, true, false),
    "AES-GCM authenticated documents are accepted");
Check(!IncomingAttachmentSecurityPolicy.RejectPlaintext(true, false, true),
    "Password-sealed key enrollment is not disguised as a document");
Check(!IncomingAttachmentSecurityPolicy.RejectPlaintext(false, false, false),
    "Legacy unkeyed initial setup remains possible");
Check(MavkaExportPolicy.Matches("Download/МаVКа файли/", "Захищений.docx.mavkafile"),
    "Allow password-and-key-protected Office container");
Check(!MavkaExportPolicy.Matches("Download/", "Захищений.docx.mavkafile"),
    "Encrypted files must not be managed outside dedicated Mavka folder");
Check(MavkaThemePolicy.Normalize(null) == MavkaThemePolicy.Olive,
    "Default theme remains olive");
Check(MavkaThemePolicy.Normalize("unknown-value") == MavkaThemePolicy.Olive,
    "Unknown persisted theme must fall back safely");
Check(MavkaThemePolicy.Normalize(MavkaThemePolicy.Blue) == MavkaThemePolicy.Blue,
    "Explicit blue theme is preserved");
Check(MavkaThemePolicy.Label(MavkaThemePolicy.Blue) == "Синя" &&
    MavkaThemePolicy.Label(MavkaThemePolicy.Olive) == "Оливкова",
    "Theme selector labels match persisted choices");
// Archive delete must never mutate postings, and restore is time-bound.
var now = DateTimeOffset.UtcNow;
Check(ArchiveTrashPolicy.CanMoveToTrash("not_posted"),
    "Unposted archive document may enter 30-day trash");
Check(!ArchiveTrashPolicy.CanMoveToTrash("posted"),
    "Posted inventory evidence must not be deletable");
Check(ArchiveTrashPolicy.CanRestore(now.AddDays(-2).ToString("O"), now),
    "Recover a recent archived document");
Check(!ArchiveTrashPolicy.CanRestore(now.AddDays(-31).ToString("O"), now) &&
    ArchiveTrashPolicy.CanPurge(now.AddDays(-31).ToString("O"), now),
    "Expired documents can be purged only after 30 days");

// QR invitation is only a 10-minute challenge, never authority by itself.
var qrText = QrPairingProtocol.Generate("main@example.org", now);
var invite = QrPairingProtocol.ParseInvitation(qrText, now);
Check(invite.MainEmail == "main@example.org" &&
    invite.ExpiresUtc == now.AddMinutes(10), "QR invitation domain and lifetime");
var ack = QrPairingProtocol.CreateAck(invite, "MAVKA-CLIENT-1");
Check(QrPairingProtocol.VerifyAck(ack, invite, "MAVKA-CLIENT-1", now),
    "Correct QR HMAC acknowledgment succeeds");
Check(!QrPairingProtocol.VerifyAck(ack, invite, "MAVKA-CLIENT-2", now),
    "Different device cannot reuse the HMAC");
Check(!QrPairingProtocol.VerifyAck(ack, invite, "MAVKA-CLIENT-1",
    now.AddMinutes(11)), "Expired QR acknowledgment is rejected");

// Password-protected backups are independent of phone-specific Keystore.
var sample = "{\\\"format\\\":\\\"MAVKA_BACKUP_DATA_V1\\\",\\\"unit\\\":\\\"Тестовий підрозділ\\\"}";
var backup = PortableBackupEnvelope.Encrypt(sample, "SYNTHETIC-PORTABLE-BACKUP-PASS");
Check(PortableBackupEnvelope.Decrypt(backup,
    "SYNTHETIC-PORTABLE-BACKUP-PASS") == sample,
    "Independent AES-256 portable backup roundtrip");
bool invalidBackupPassword = false;
try { PortableBackupEnvelope.Decrypt(backup, "WRONG-PORTABLE-BACKUP-PASS"); }
catch (System.Security.Cryptography.CryptographicException) { invalidBackupPassword = true; }
Check(invalidBackupPassword, "Wrong backup password must fail GCM authentication");
using (var backupDoc = System.Text.Json.JsonDocument.Parse(backup))
{
    var root = backupDoc.RootElement;
    string modified = System.Text.Encoding.UTF8.GetString(backup);
    string validTag = root.GetProperty("tag").GetString()!;
    string altered = modified.Replace(validTag,
        Convert.ToBase64String(new byte[16]), StringComparison.Ordinal);
    bool rejected = false;
    try { PortableBackupEnvelope.Decrypt(
        System.Text.Encoding.UTF8.GetBytes(altered),
        "SYNTHETIC-PORTABLE-BACKUP-PASS"); }
    catch (System.Security.Cryptography.CryptographicException) { rejected = true; }
    Check(rejected, "Corrupt backup GCM tag must reject without plaintext");
}
Console.WriteLine("File, QR, archive trash and passworded backup policies: 63/63 PASS");
