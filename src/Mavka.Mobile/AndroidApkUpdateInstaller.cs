using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using System.Security.Cryptography;

namespace Mavka.Mobile;

/// <summary>
/// Only the Android OS package installer can actually install the APK.
/// Before asking for user consent, reject mismatched package identity,
/// downgrades and mismatched signing certificates. Never uninstall or
/// clear Mavka storage as part of an update.
/// </summary>
internal static class AndroidApkUpdateInstaller
{
    public sealed record Candidate(long CurrentVersion, long NewVersion,
        string ApkSha256, long ApkBytes);

    public static Candidate Verify(Activity activity, string path)
    {
        if (!File.Exists(path))
            throw new IOException("Спочатку завантаж APK.");
        var pm = activity.PackageManager ??
            throw new InvalidOperationException("PackageManager недоступний.");
        var flags = Build.VERSION.SdkInt >= BuildVersionCodes.P
            ? PackageInfoFlags.SigningCertificates : PackageInfoFlags.Signatures;
        using var installed = pm.GetPackageInfo(activity.PackageName!, flags) ??
            throw new InvalidOperationException("Неможливо перевірити встановлену Мавку.");
        using var next = pm.GetPackageArchiveInfo(path, flags) ??
            throw new InvalidOperationException("Android не розпізнав завантажений APK.");
        var existingCert = ReadCertificate(installed);
        var replacementCert = ReadCertificate(next);
        long previous = Build.VERSION.SdkInt >= BuildVersionCodes.P
            ? installed.LongVersionCode : installed.VersionCode;
        long version = Build.VERSION.SdkInt >= BuildVersionCodes.P
            ? next.LongVersionCode : next.VersionCode;
        ApkUpdatePolicy.CheckApkMetadata(next.PackageName, previous, version,
            existingCert, replacementCert);
        using var input = File.OpenRead(path);
        if (input.Length > ApkUpdatePolicy.MaxApkBytes)
            throw new IOException("APK завеликий.");
        return new Candidate(previous, version,
            Convert.ToHexString(SHA256.HashData(input)), input.Length);
    }

    public static bool HasInstallerPermission(Activity activity) =>
        Build.VERSION.SdkInt < BuildVersionCodes.O ||
        activity.PackageManager?.CanRequestPackageInstalls() == true;

    public static void RequestInstallerPermission(Activity activity)
    {
        var settings = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
            Android.Net.Uri.Parse("package:" + activity.PackageName));
        activity.StartActivity(settings);
    }

    public static void LaunchSystemInstaller(Activity activity, string path)
    {
        // Revalidate the EXACT staged bytes on every attempt, including after
        // returning from Android's unknown-app-sources Settings screen.
        _ = Verify(activity, path);
        if (!HasInstallerPermission(activity))
            throw new InvalidOperationException(
                "Спочатку надай Мавці дозвіл установлювати оновлення.");
        var install = new Intent(Intent.ActionInstallPackage);
        install.SetDataAndType(UpdateApkContentProvider.ShareUri,
            ApkUpdatePolicy.ApkMime);
        install.AddFlags(ActivityFlags.GrantReadUriPermission);
        // User must explicitly confirm installation in the OS. Android
        // rejects incompatible signing keys again at the system level.
        activity.StartActivity(install);
    }

    private static byte[] ReadCertificate(PackageInfo package)
    {
        Android.Content.PM.Signature[]? signatures =
            Build.VERSION.SdkInt >= BuildVersionCodes.P
                ? package.SigningInfo?.GetApkContentsSigners()
                : package.Signatures?.ToArray();
        if (signatures is null || signatures.Length != 1)
            throw new InvalidOperationException(
                "APK має невідомий або багатокомпонентний підпис.");
        return SHA256.HashData(signatures[0].ToByteArray());
    }
}
