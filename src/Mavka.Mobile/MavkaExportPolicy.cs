namespace Mavka.Mobile;

/// <summary>
/// Namespace and format allow-list for exported copies only.
/// It must never be used to identify or delete internal accounting records.
/// </summary>
internal static class MavkaExportPolicy
{
    public const string FolderTitle = "МаVКа файли";
    // The physical Android directory is "Download"; file managers often
    // label it "Downloads" in their localized UI.
    public const string RelativePath = "Download/МаVКа файли/";

    public static bool Matches(string? relativePath, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            string.IsNullOrWhiteSpace(fileName))
            return false;
        var path = relativePath.Replace('\\', '/').Trim('/');
        if (!string.Equals(path, RelativePath.Trim('/'), StringComparison.Ordinal))
            return false;
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName is "." or "..")
            return false;
        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".mavkafile", StringComparison.OrdinalIgnoreCase);
    }
}
