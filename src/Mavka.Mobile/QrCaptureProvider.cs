using Android.Content;
using Android.Database;
using Android.OS;

namespace Mavka.Mobile;
// Camera receives temporary URI permission to this one private JPEG only.
[ContentProvider(new[]{Authority},Exported=false,GrantUriPermissions=true)]
internal sealed class QrCaptureProvider:ContentProvider
{
#if MAVKA_BOOTSTRAP_QA
    public const string Authority="ua.mavka.mobile.bootstrapqa.qrcapture";
#elif MAVKA_RELAY_QA
    public const string Authority="ua.mavka.mobile.relayqa.qrcapture";
#else
    public const string Authority="ua.mavka.mobile.qrcapture";
#endif
    public static Android.Net.Uri ShareUri=>Android.Net.Uri.Parse("content://"+Authority+"/qr.jpg")!;
    public static string PathFor(Context context)=>System.IO.Path.Combine(context.CacheDir!.AbsolutePath,"mavka-qr-capture.jpg");
    public override bool OnCreate()=>true;
    private static void Check(Android.Net.Uri uri){if(uri.Authority!=Authority || uri.Path!="/qr.jpg" || !string.IsNullOrEmpty(uri.Query))throw new System.Security.SecurityException("Invalid QR photo URI.");}
    public override string GetType(Android.Net.Uri uri){Check(uri);return "image/jpeg";}
    public override ParcelFileDescriptor? OpenFile(Android.Net.Uri uri,string mode)
    {
        Check(uri);var flags=mode switch{"r"=>ParcelFileMode.ReadOnly,"w" or "wt"=>ParcelFileMode.WriteOnly|ParcelFileMode.Create|ParcelFileMode.Truncate,"rw"=>ParcelFileMode.ReadWrite|ParcelFileMode.Create,_=>throw new System.Security.SecurityException("Invalid mode.")};
        return ParcelFileDescriptor.Open(new Java.IO.File(PathFor(Context!)),flags);
    }
    public override ICursor? Query(Android.Net.Uri uri,string[]? projection,string? selection,string[]? selectionArgs,string? sortOrder){Check(uri);return null;}
    public override Android.Net.Uri? Insert(Android.Net.Uri uri,ContentValues? values)=>throw new NotSupportedException();
    public override int Delete(Android.Net.Uri uri,string? selection,string[]? selectionArgs)=>throw new NotSupportedException();
    public override int Update(Android.Net.Uri uri,ContentValues? values,string? selection,string[]? selectionArgs)=>throw new NotSupportedException();
}
