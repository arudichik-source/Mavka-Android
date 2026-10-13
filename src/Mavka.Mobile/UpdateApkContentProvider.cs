using Android.Content;
using Android.Database;
using Android.Database.Sqlite;
using Android.OS;
using Android.Provider;

namespace Mavka.Mobile;

/// <summary>
/// Grants the Android package installer read-only access to exactly one
/// vetted APK staged in this app's private cache. Not exported to general
/// applications; no directory traversal, write permission or arbitrary files.
/// </summary>
[ContentProvider(new[] { Authority }, Exported = false, GrantUriPermissions = true)]
internal sealed class UpdateApkContentProvider : ContentProvider
{
    public const string Authority = "ua.mavka.mobile.updateprovider";
    private const string AllowedPath = "/verified-update.apk";

    public static Android.Net.Uri ShareUri =>
        Android.Net.Uri.Parse("content://" + Authority + AllowedPath)!;

    public static string StagedPath(Context context) =>
        Path.Combine(context.CacheDir?.AbsolutePath ??
            throw new InvalidOperationException("Приватний кеш Android недоступний."),
            "mavka-verified-update.apk");

    public override bool OnCreate() => true;

    public override string GetType(Android.Net.Uri uri)
    {
        Check(uri);
        return ApkUpdatePolicy.ApkMime;
    }

    public override ParcelFileDescriptor? OpenFile(Android.Net.Uri uri, string mode)
    {
        Check(uri);
        if (mode != "r") throw new Java.IO.FileNotFoundException(
            "Update provider only permits read-only access.");
        var file = StagedPath(Context ??
            throw new InvalidOperationException("Контекст Android відсутній."));
        if (!File.Exists(file)) throw new Java.IO.FileNotFoundException(
            "Оновлення не завантажене.");
        return ParcelFileDescriptor.Open(new Java.IO.File(file), ParcelFileMode.ReadOnly);
    }

    public override ICursor? Query(Android.Net.Uri uri, string[]? projection,
        string? selection, string[]? selectionArgs, string? sortOrder)
    {
        Check(uri);
        string[] names = projection ?? new[]
        {
            OpenableColumns.DisplayName, OpenableColumns.Size
        };
        var cursor = new MatrixCursor(names);
        object?[] cells = names.Select(name => name switch
        {
            OpenableColumns.DisplayName => (object)"Mavka-Mobile-Update.apk",
            OpenableColumns.Size => File.Exists(StagedPath(Context!))
                ? new FileInfo(StagedPath(Context!)).Length : 0L,
            _ => null
        }).ToArray();
        cursor.AddRow(cells);
        return cursor;
    }

    public override Android.Net.Uri? Insert(Android.Net.Uri uri, ContentValues? values) =>
        throw new NotSupportedException("Installer has read-only permission.");

    public override int Delete(Android.Net.Uri uri, string? selection, string[]? selectionArgs) =>
        throw new NotSupportedException("Installer cannot delete APK.");

    public override int Update(Android.Net.Uri uri, ContentValues? values,
        string? selection, string[]? selectionArgs) =>
        throw new NotSupportedException("Installer cannot alter APK.");

    private static void Check(Android.Net.Uri uri)
    {
        if (uri.Scheme != "content" || uri.Authority != Authority ||
            uri.Path != AllowedPath || !string.IsNullOrEmpty(uri.Query))
            throw new Java.IO.FileNotFoundException("Invalid update URI.");
    }
}
