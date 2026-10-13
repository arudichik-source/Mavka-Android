using Android.Content;
using Android.OS;
using Android.Provider;

namespace Mavka.Mobile;

internal sealed record MavkaExportFile(
    Android.Net.Uri Uri, string Name, string MimeType,
    long Size, DateTimeOffset ModifiedAt);

/// <summary>
/// Lists and manages user-visible copies using Android MediaStore. It never
/// touches the Keystore, internal document archive or SQLite accounting base.
/// No general external-storage permission or filesystem paths are used.
/// </summary>
internal sealed class MavkaExportFileStore
{
    private readonly Context _context;

    public MavkaExportFileStore(Context context) => _context = context;

    public bool IsSupported => Build.VERSION.SdkInt >= BuildVersionCodes.Q;

    public IReadOnlyList<MavkaExportFile> ListFiles()
    {
        if (!IsSupported)
            return Array.Empty<MavkaExportFile>();

        var collection = MediaStore.Downloads.ExternalContentUri;
        string[] projection =
        [
            Android.Provider.BaseColumns.Id,
            MediaStore.IMediaColumns.DisplayName,
            MediaStore.IMediaColumns.RelativePath,
            MediaStore.IMediaColumns.Size,
            MediaStore.IMediaColumns.DateModified
        ];

        var files = new List<MavkaExportFile>();
        // Ask MediaStore for our dedicated folder, not the entire user
        // Downloads collection; still revalidate each returned row below.
        using var cursor = _context.ContentResolver?.Query(
            collection, projection,
            MediaStore.IMediaColumns.RelativePath + " = ?",
            new[] { MavkaExportPolicy.RelativePath },
            MediaStore.IMediaColumns.DateModified + " DESC");
        if (cursor is null) return files;

        int idIndex = cursor.GetColumnIndexOrThrow(Android.Provider.BaseColumns.Id);
        int nameIndex = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.DisplayName);
        int folderIndex = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.RelativePath);
        int sizeIndex = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.Size);
        int dateIndex = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.DateModified);

        while (cursor.MoveToNext())
        {
            string name = cursor.IsNull(nameIndex) ? "" : cursor.GetString(nameIndex);
            string folder = cursor.IsNull(folderIndex) ? "" : cursor.GetString(folderIndex);
            if (!MavkaExportPolicy.Matches(folder, name))
                continue;
            long id = cursor.GetLong(idIndex);
            var uri = ContentUris.WithAppendedId(collection, id);
            long size = cursor.IsNull(sizeIndex) ? 0 : cursor.GetLong(sizeIndex);
            long modified = cursor.IsNull(dateIndex) ? 0 : cursor.GetLong(dateIndex);
            string mime = name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "application/pdf"
                : name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                    ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                    : name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                        ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                        : "application/msword";
            files.Add(new MavkaExportFile(uri, name, mime, size,
                DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, modified))));
        }
        return files;
    }

    /// <summary>
    /// Publishes only a completed, password-encrypted PDF to the user-visible
    /// folder. MediaStore IS_PENDING keeps a partially written copy hidden.
    /// No plaintext PDF is ever written to the public storage location.
    /// </summary>
    public Android.Net.Uri SaveProtectedPdf(byte[] encryptedPdf, string fileName)
    {
        if (!IsSupported)
            throw new NotSupportedException("Android 10+ MediaStore export required.");
        if (!MavkaExportPolicy.Matches(MavkaExportPolicy.RelativePath, fileName))
            throw new ArgumentException("Invalid export filename.");
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only protected PDF is supported for export.");
        if (encryptedPdf.Length is < 200 or > 20_000_000 ||
            System.Text.Encoding.ASCII.GetString(encryptedPdf, 0, 5) != "%PDF-" ||
            encryptedPdf.AsSpan().IndexOf("/Encrypt"u8) < 0)
            throw new InvalidOperationException("Export must be a password-encrypted PDF.");

        var resolver = _context.ContentResolver ??
            throw new InvalidOperationException("Android ContentResolver unavailable.");
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "application/pdf");
        values.Put(MediaStore.IMediaColumns.RelativePath, MavkaExportPolicy.RelativePath);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);

        Android.Net.Uri? created = null;
        try
        {
            created = resolver.Insert(MediaStore.Downloads.ExternalContentUri, values) ??
                throw new InvalidOperationException("Cannot create export in Downloads.");
            using (var stream = resolver.OpenOutputStream(created) ??
                   throw new InvalidOperationException("Cannot write protected PDF."))
            {
                stream.Write(encryptedPdf, 0, encryptedPdf.Length);
                stream.Flush();
            }
            var ready = new ContentValues();
            ready.Put(MediaStore.IMediaColumns.IsPending, 0);
            if (resolver.Update(created, ready, null, null) != 1)
                throw new InvalidOperationException("Cannot finalize protected export.");
            return created;
        }
        catch
        {
            if (created is not null)
            {
                try { resolver.Delete(created, null, null); }
                catch { /* Keep the original failure; never expose partial data. */ }
            }
            throw;
        }
    }

    /// <summary>
    /// Only an already Agile-encrypted Office compound document is eligible.
    /// MediaStore pending semantics hide partially written output.
    /// </summary>
    public Android.Net.Uri SaveProtectedXlsx(byte[] encryptedXlsx, string fileName)
    {
        if (!IsSupported)
            throw new NotSupportedException("Android 10+ MediaStore export required.");
        if (!MavkaExportPolicy.Matches(MavkaExportPolicy.RelativePath, fileName) ||
            !fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid encrypted Excel export path.");

        ReadOnlySpan<byte> compoundHeader =
            [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        if (encryptedXlsx.Length is < 512 or > 20_000_000 ||
            !encryptedXlsx.AsSpan(0, 8).SequenceEqual(compoundHeader))
            throw new InvalidOperationException(
                "Expected Office-encrypted OLE container, not plaintext OOXML ZIP.");

        var resolver = _context.ContentResolver ??
            throw new InvalidOperationException("Android ContentResolver unavailable.");
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        values.Put(MediaStore.IMediaColumns.RelativePath, MavkaExportPolicy.RelativePath);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);

        Android.Net.Uri? created = null;
        try
        {
            created = resolver.Insert(MediaStore.Downloads.ExternalContentUri, values) ??
                throw new InvalidOperationException("Cannot create protected Excel export.");
            using (var stream = resolver.OpenOutputStream(created) ??
                   throw new InvalidOperationException("Cannot write protected Excel export."))
            {
                stream.Write(encryptedXlsx, 0, encryptedXlsx.Length);
                stream.Flush();
            }
            var ready = new ContentValues();
            ready.Put(MediaStore.IMediaColumns.IsPending, 0);
            if (resolver.Update(created, ready, null, null) != 1)
                throw new InvalidOperationException("Cannot finalize encrypted workbook.");
            return created;
        }
        catch
        {
            if (created is not null)
            {
                try { resolver.Delete(created, null, null); }
                catch { /* Partial encrypted output stays pending/inaccessible. */ }
            }
            throw;
        }
    }

    private void EnsureExportedCopy(MavkaExportFile file)
    {
        // Every external URI action, not just deletion, must be scoped to an
        // approved user-exported copy. Do not allow arbitrary content:// URIs.
        var matched = ListFiles().Any(item => item.Uri.ToString() == file.Uri.ToString() &&
            string.Equals(item.Name, file.Name, StringComparison.Ordinal) &&
            string.Equals(item.MimeType, file.MimeType, StringComparison.Ordinal));
        if (!matched)
            throw new InvalidOperationException(
                "Файл не належить до папки МаVКа файли або вже недоступний.");
    }

    public int DeleteFile(MavkaExportFile file)
    {
        EnsureExportedCopy(file);
        return _context.ContentResolver?.Delete(file.Uri, null, null) ?? 0;
    }

    public void OpenFile(MavkaExportFile file)
    {
        EnsureExportedCopy(file);
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(file.Uri, file.MimeType);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        _context.StartActivity(Intent.CreateChooser(intent, "Відкрити файл"));
    }

    public void ShareFile(MavkaExportFile file)
    {
        EnsureExportedCopy(file);
        var intent = new Intent(Intent.ActionSend);
        intent.SetType(file.MimeType);
        intent.PutExtra(Intent.ExtraStream, file.Uri);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        _context.StartActivity(Intent.CreateChooser(intent, "Поділитися файлом"));
    }
}
