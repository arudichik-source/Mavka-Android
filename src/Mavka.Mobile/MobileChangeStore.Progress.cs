using System.Text.Json;
namespace Mavka.Mobile;
internal sealed partial class MobileChangeStore
{
    internal string SynchronizationProgress()
    {
        using var db=ReadableDatabase;var assets=0;var documents=0;
        using(var rows=db.RawQuery("SELECT kind,COUNT(*) FROM records WHERE client=? GROUP BY kind",new[]{Client}))
            while(rows.MoveToNext()){if(rows.GetString(0)=="asset")assets=rows.GetInt(1);else if(rows.GetString(0)=="document")documents=rows.GetInt(1);}
        string? pending=null;
        using(var request=db.RawQuery("SELECT id FROM state_requests WHERE client=? AND status='pending' ORDER BY rowid DESC LIMIT 1",new[]{Client}))if(request.MoveToFirst())pending=request.GetString(0);
        var details=new List<string>();
        using(var batches=db.RawQuery("SELECT source,batch,COUNT(*) FROM parts WHERE client=? AND sealed<>'' GROUP BY source,batch ORDER BY MAX(sequence) DESC",new[]{Client}))
            while(batches.MoveToNext() && details.Count<3)
            {
                using var first=db.RawQuery("SELECT sealed FROM parts WHERE client=? AND source=? AND batch=? AND sealed<>'' ORDER BY part LIMIT 1",new[]{Client,batches.GetString(0),batches.GetString(1)});
                if(!first.MoveToFirst())continue;var part=JsonSerializer.Deserialize<MobileChangePart>(Open(first.GetString(0)))!;
                if(part.sequence<=Head(db,Client,part.sourceId) || pending is not null && part.requestId!=pending)continue;
                details.Add($"отримано {batches.GetInt(2)} із {part.count} частин");
            }
        return $"В обліку головної Мавки: залишків {assets}, документів {documents}."+
            (details.Count>0?"\nОновлення бази: "+string.Join("; ",details)+". Облік доповниться після отримання й перевірки всіх частин.":pending is not null?"\nОчікується повний пакет за запитом до ПК.":"");
    }
}
