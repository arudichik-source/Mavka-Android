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
Console.WriteLine("Android export scope policy regression: 10/10 PASS");
