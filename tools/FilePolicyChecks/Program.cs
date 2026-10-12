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
Console.WriteLine("File export, mail metadata and encrypted vault listing policy: 25/25 PASS");
