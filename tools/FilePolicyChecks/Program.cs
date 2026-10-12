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
Console.WriteLine("File export and mail metadata policy regression: 17/17 PASS");
