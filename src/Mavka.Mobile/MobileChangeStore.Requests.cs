using Android.Content;
using Android.Database.Sqlite;
using System.Text.Json;

namespace Mavka.Mobile;
internal sealed partial class MobileChangeStore
{
    private static void CreateRequests(SQLiteDatabase db)=>db.ExecSQL("CREATE TABLE IF NOT EXISTS state_requests(client TEXT,id TEXT,source TEXT,sequence INTEGER,hash TEXT,sealed TEXT,status TEXT,PRIMARY KEY(client,id))");
    private Dictionary<string,string> RecordHashes(SQLiteDatabase db,string source,CancellationToken token,MobileAccessPolicy? policy=null)
    {
        var hashes=new Dictionary<string,string>(StringComparer.Ordinal);
        using var rows=db.RawQuery("SELECT key,sealed,location,kind FROM records WHERE client=? AND source=? ORDER BY key",new[]{Client,source});
        if(!rows.MoveToFirst())return hashes;using var reader=new TransferKeystoreCipher.ReadSession();
        do{token.ThrowIfCancellationRequested();if(policy is not null && !policy.Allows(rows.GetInt(2),rows.GetString(3)=="asset"))continue;hashes.Add(rows.GetString(0),Hash(reader.OpenText(rows.GetString(1))));if(hashes.Count>200000)throw new IOException("Завеликий звіт телефона.");}while(rows.MoveToNext());
        return hashes;
    }
    public MobileStateRequest[] CreateStateRequest(CancellationToken token)
    {
        LocalAccountReset.EnsureReady(context);using var db=WritableDatabase;db.BeginTransaction();
        try
        {
            string source="";long sequence=0,revision=0;
            using(var head=db.RawQuery("SELECT source,sequence,revision FROM heads WHERE client=?",new[]{Client}))
                if(head.MoveToFirst()){source=head.GetString(0);sequence=head.GetLong(1);revision=head.GetLong(2);if(head.MoveToNext())throw new IOException("На телефоні кілька джерел обліку.");}
            var hashes=RecordHashes(db,source,token);var hash=MobileStateRequest.Hash(hashes);
            using(var previous=db.RawQuery("SELECT sealed FROM state_requests WHERE client=? AND source=? AND sequence=? AND hash=? AND status='pending' ORDER BY rowid DESC LIMIT 1",new[]{Client,source,sequence.ToString(),hash}))
                if(previous.MoveToFirst())
                {
                    var saved=JsonSerializer.Deserialize<MobileStateRequest[]>(Open(previous.GetString(0)))!;
                    if(saved[0].createdAt>DateTimeOffset.UtcNow.AddDays(-29)){db.SetTransactionSuccessful();return saved;}
                }
            var id=Guid.NewGuid().ToString("N");var created=DateTimeOffset.UtcNow;var chunks=hashes.Chunk(500).Select(c=>c.ToDictionary(p=>p.Key,p=>p.Value)).ToArray();if(chunks.Length==0)chunks=[new()];
            var parts=chunks.Select((h,i)=>new MobileStateRequest(MobileStateRequest.Format,Client,id,created,source,sequence,revision,i,chunks.Length,hash,h)).ToArray();foreach(var part in parts)part.Validate(Client);
            var old=new ContentValues();old.Put("status","superseded");db.Update("state_requests",old,"client=? AND status='pending'",new[]{Client});
            var values=new ContentValues();values.Put("client",Client);values.Put("id",id);values.Put("source",source);values.Put("sequence",sequence);values.Put("hash",hash);values.Put("sealed",Seal(JsonSerializer.Serialize(parts)));values.Put("status","pending");db.InsertOrThrow("state_requests",null,values);
            db.SetTransactionSuccessful();return parts;
        }
        finally{db.EndTransaction();}
    }
    internal void CancelStateRequest(){using var db=WritableDatabase;var cancelled=new ContentValues();cancelled.Put("status","cancelled");db.Update("state_requests",cancelled,"client=? AND status='pending'",new[]{Client});}
    private bool RequestedResponse(SQLiteDatabase db,MobileChangePart part,long current)
    {
        if(part.requestId.Length==0 || part.requestSequence!=current || part.sequence<=current)return false;
        using var row=db.RawQuery("SELECT source,sequence,hash FROM state_requests WHERE client=? AND id=? AND status='pending'",new[]{Client,part.requestId});
        return row.MoveToFirst() && row.GetLong(1)==current && MobileStateRequest.MatchesResponse(part,part.requestId,row.GetString(0),current,row.GetString(2));
    }
}
