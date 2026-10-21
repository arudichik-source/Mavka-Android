using Android.Content;
using Android.Database.Sqlite;

namespace Mavka.Mobile;

/// <summary>Explicit local content reset. A durable marker resumes interrupted clears
/// before imports/recovery; no profile, credential, access preference or Keystore alias is removed.</summary>
internal static class LocalAccountReset
{
    private const string Preferences="mavka_local_account_reset_v1";
    public static bool IsPending(Context context)=>context.GetSharedPreferences(Preferences,FileCreationMode.Private)!.GetBoolean("pending",false);
    public static void Clear(Context context)
    {
        Mark(context,true);
        ResumePending(context);
    }
    public static void EnsureReady(Context context)
    {
        if(IsPending(context))throw new InvalidOperationException("Очищення обліку не завершено. Повторіть очищення або перезапустіть Мавку.");
    }
    // Callers hold RelaySync.Gate; foreground Gmail is excluded by the Activity's
    // busy state. Never resume an import between the independently committed stores.
    public static void ResumePending(Context context)
    {
        if(!IsPending(context))return;
        using(var archive=new DocumentArchiveStore(context))archive.ClearAllForAccountReset();
        using(var mirror=new MobileChangeStore(context))mirror.ClearAllForAccountReset();
        using(var ledger=new PropertyLedgerStore(context))ledger.ClearAllForAccountReset();
        var confirmation=new BootstrapStore(context).Confirmation;
        using(var relay=new RelayStore(context,"account-reset"))
            relay.DiscardDocumentsForAccountReset(confirmation is null?null:RelayProtocol.Hash("bootstrap-installed:"+RelayProtocol.Hash(confirmation)));
        var root=Path.GetFullPath(context.FilesDir?.AbsolutePath??throw new IOException("Локальне сховище недоступне."));
        foreach(var name in new[]{"secure_received_files_v1","pending_file_parts_v1","outgoing_file_parts_v1","relay_received_file_parts_v1"})
        {
            var folder=Path.GetFullPath(Path.Combine(root,name));
            if(!folder.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new IOException("Невірний шлях локального сховища.");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
        new GmailInboxCache(context).Clear(strict:true);
        Mark(context,false);
    }
    private static void Mark(Context context,bool pending)
    {
        using var editor=context.GetSharedPreferences(Preferences,FileCreationMode.Private)!.Edit()!;
        if(!editor.PutBoolean("pending",pending)!.Commit())throw new IOException("Не вдалося зберегти стан очищення.");
    }
    internal static void ClearTables(SQLiteDatabase db,params string[] tables)
    {
        using(db)
        {
            db.BeginTransaction();try
            {
                foreach(var table in tables)db.Delete(table,null,null);
                foreach(var table in tables)
                {
                    using var row=db.RawQuery("SELECT COUNT(*) FROM "+table,null);
                    if(!row.MoveToFirst() || row.GetLong(0)!=0)throw new IOException("Очищення локальної бази не підтверджено.");
                }
                db.SetTransactionSuccessful();
            }
            finally{db.EndTransaction();}
        }
    }
}
