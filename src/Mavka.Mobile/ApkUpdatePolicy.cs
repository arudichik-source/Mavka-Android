using System.Security.Cryptography;

namespace Mavka.Mobile;

/// <summary>
/// Only Google Drive sharing URLs are accepted for user-initiated Android
/// updates. A download never grants software authenticity: Android signing
/// certificate and monotonically increasing versionCode are checked later.
/// </summary>
internal static class ApkUpdatePolicy
{
    public const long MaxApkBytes = 220L * 1024 * 1024;
    public const string ProductionPackage = "ua.mavka.mobile";
    public const string ApkMime = "application/vnd.android.package-archive";

    public static Uri NormalizeDriveLink(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000 ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url) ||
            !IsTrustedDriveUri(url)) throw new ArgumentException(
                "Використовуй HTTPS-посилання Google Drive на файл APK.");
        if (url.Host == "drive.google.com")
        {
            string? fileId = null;
            var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3 && segments[0] == "file" &&
                segments[1] == "d") fileId = segments[2];
            else if (segments.Length >= 2 && segments[0] == "open")
                fileId = FindQueryValue(url.Query, "id");
            else if (segments.Length >= 1 && segments[0] is "uc" or "download")
                fileId = FindQueryValue(url.Query, "id");
            if (!string.IsNullOrWhiteSpace(fileId))
            {
                if (fileId.Length is < 8 or > 200 || !fileId.All(ch =>
                    char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
                    throw new ArgumentException("Некоректний ідентифікатор файла Google Drive.");
                return new Uri("https://drive.google.com/uc?export=download&id=" + fileId);
            }
        }
        return url;
    }

    public static bool IsTrustedDriveUri(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort &&
        string.IsNullOrEmpty(url.UserInfo) &&
        (url.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
         url.Host.Equals("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase) ||
         url.Host.Equals("doc-0o-00-docs.googleusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         url.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase));

    public static void CheckApkMetadata(string? candidatePackage, long installedVersion,
        long candidateVersion, byte[] installedSignerDigest, byte[] candidateSignerDigest)
    {
        if (candidatePackage != ProductionPackage)
            throw new InvalidOperationException(
                "Цей APK належить іншій програмі або тестовій QA-версії.");
        if (candidateVersion <= installedVersion)
            throw new InvalidOperationException(
                "Оновлення має мати більший Android versionCode, ніж поточна програма.");
        if (installedSignerDigest.Length != 32 || candidateSignerDigest.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(installedSignerDigest, candidateSignerDigest))
            throw new InvalidOperationException(
                "Підпис APK відрізняється від встановленої Мавки. " +
                "Оновлення заборонене, щоб зберегти базу й Android Keystore.");
    }

    public static string? FindQueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var items = pair.Split('=', 2);
            if (items.Length == 2 && items[0] == key)
                return Uri.UnescapeDataString(items[1]);
        }
        return null;
    }
}
