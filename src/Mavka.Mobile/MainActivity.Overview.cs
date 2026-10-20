using Android.App;
using Android.Widget;

namespace Mavka.Mobile;
public partial class MainActivity
{
    private async Task RefreshRelayAsync(TextView status)
    {
        if(_busy)return;_busy=true;status.Text="Оновлення сервера…";
        try
        {
            if(!new RelayIdentity(this).Enabled)throw new InvalidOperationException("Спочатку завершіть QR-підключення до головної Мавки.");
            status.Text=await new RelaySync(this).SynchronizeNow();
        }
        catch(Exception ex){status.Text=ExplainOperationError(ex);}
        finally{_busy=false;}
    }

    private void ShowGrantedAssets()
    {
        var access=new MobileAccessStore(this).Load()!;
        if(access.isMvo || !access.canViewAssets)return;
        if(_showRootMirror && DrawRootAssets(null))return;
        var top=Card();
        Text(top,access.allUnits?"Майно військової частини":"Майно доступних підрозділів",19,true);
        Button(top,"Обрати підрозділ",()=>ChooseMobileUnit(ShowAssets));
        var snapshots=new List<UnitAssetsSnapshot>();
        var missing=new List<string>();
        foreach(var grant in access.units)
        {
            UnitAssetsSnapshot? latest=null;
            foreach(var summary in _archive.ListSummaries(grant.unit).Where(s=>s.Kind=="Знімок залишків").OrderByDescending(s=>s.DocumentDate,StringComparer.Ordinal).ThenByDescending(s=>s.SavedUtc,StringComparer.Ordinal))
            {
                if(MavkaExchangeParser.TryAssets(_archive.FindDocument(summary.DocumentId)?.Json,out var snapshot) && snapshot is not null){latest=snapshot;break;}
            }
            if(latest is null){missing.Add(grant.unit);continue;}
            snapshots.Add(latest);
        }
        Text(top,$"Отримано підрозділів: {snapshots.Count} із {access.units.Length}",14);
        if(missing.Count>0)Text(top,"Дані ще не отримані: "+string.Join(", ",missing),13);
        Text(top,"Знімки з різними датами показані окремо. Локальний облік телефона сюди не додається.",12);
        foreach(var date in snapshots.GroupBy(s=>s.AsOf).OrderByDescending(g=>g.Key,StringComparer.Ordinal))
        {
            var card=Card();Text(card,"Станом на "+date.Key,17,true);
            Text(card,"Підрозділи: "+string.Join(", ",date.Select(s=>s.Unit)),13);
            // Keep every authoritative card/category separate. Never infer identity
            // from a similar name or combine different prices/measurement units.
            foreach(var snapshot in date)
                foreach(var asset in snapshot.Items)
                    AddAssetRow(card,asset.Name,snapshot.Unit+" · ціна "+(asset.UnitPrice?.ToString("0.####")??"невідома"),asset.Quantity.ToString("0.###")+" "+asset.Measure);
        }
        if(snapshots.Count==0)Text(_body,"Оновіть пошту або сервер на головному екрані, щоб отримати доступні залишки.",14);
    }
}
