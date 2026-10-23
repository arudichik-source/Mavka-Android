using Android.App;
using Android.Widget;

namespace Mavka.Mobile;
public partial class MainActivity
{
    private async Task RefreshRelayAsync(TextView status)
    {
        if(_busy || _resetRequested)return;using var operation=BeginExchange();var cancellation=operation.Token;_busy=true;ReportExchangeStatus(status,"Оновлення сервера…");
        try
        {
            if(!new RelayIdentity(this).Enabled)throw new InvalidOperationException("Спочатку завершіть QR-підключення до головної Мавки.");
            status.Text=await Task.Run(()=>new RelaySync(this).SynchronizeNow(cancellation),cancellation);
            var posting=await Task.Run(()=>IncomingBatchPosting.Apply(this,cancellation:cancellation));
            _incomingPostingIssue=posting.Issues.Count>0?posting.Summary:null;
            cancellation.ThrowIfCancellationRequested();
            try{await Task.Run(()=>SendPendingFilePartsAsync(maximum:1,cancellation:cancellation),cancellation);}
            catch(Exception ex)when(ex is not OperationCanceledException){status.Text+=" Вихідні частини файлів залишено в черзі.";}
            if(posting.OpeningUnits+posting.Invoices>0 || posting.Issues.Count>0)status.Text+="\n"+posting.Summary;
        }
        catch(Exception ex){status.Text=ExplainOperationError(ex);}
        finally{ReportExchangeStatus(status,status.Text??"Обмін завершено.",true);_busy=false;}
    }

    private void ShowGrantedAssets()
    {
        var access=new MobileAccessStore(this).Load()!;
        if(access.isMvo || !access.canViewAssets)return;
        DrawIncomingBatchAction();
        if(_showRootMirror && DrawRootAssets(null))return;
        var top=Card();
        Text(top,access.allUnits?"Майно військової частини":"Майно доступних підрозділів",19,true);
        Button(top,"Обрати підрозділ",()=>ChooseMobileUnit(ShowAssets));
        var search=ProfileInput(top,"Пошук за назвою або NSN",_assetQuery);
        Button(top,"Скинути пошук",()=>search.Text="");
        var status=Text(top,"Завантаження залишків доступних підрозділів…",14);
        var list=new LinearLayout(this){Orientation=Orientation.Vertical};top.AddView(list);
        IReadOnlyList<UnitAssetsSnapshot> snapshots=[];var missing=new List<string>();var page=0;
        var generation=++_mirrorReadGeneration;
        void Draw()
        {
            list.RemoveAllViews();var current=new MobileAccessStore(this).Load();
            var allowed=snapshots.Where(s=>current?.units.Any(u=>u.unit.Equals(s.Unit,StringComparison.OrdinalIgnoreCase))==true && current.canViewAssets).ToArray();
            var all=allowed.SelectMany(s=>s.Items.Select(a=>(Snapshot:s,Asset:a))).ToArray();
            var found=InventorySearchSummary.Filter(all,search.Text,r=>r.Asset.Name,r=>r.Asset.Nsn)
                .OrderByDescending(r=>r.Snapshot.AsOf,StringComparer.Ordinal).ThenBy(r=>r.Asset.Name,StringComparer.CurrentCulture).ThenBy(r=>r.Snapshot.Unit,StringComparer.CurrentCulture).ToArray();
            page=Math.Min(page,Math.Max(0,(found.Length-1)/50));
            Text(list,$"Знайдено позицій: {found.Length} із {all.Length} · сторінка {page+1}",14);
            if(found.Length==0)Text(list,all.Length>0?"За цим запитом майна не знайдено.":"Отриманих залишків поки немає.",14);
            // Retain unit/date/price identity; search never adds snapshot rows
            // from different dates and never includes the separate local ledger.
            foreach(var date in found.Skip(page*50).Take(50).GroupBy(r=>r.Snapshot.AsOf))
            {
                Text(list,"Станом на "+date.Key,17,true);
                foreach(var row in date)
                    AddAssetRow(list,row.Asset.Name,row.Snapshot.Unit+(row.Asset.Category is {} category?" · кат. "+category:"")+" · "+(string.IsNullOrWhiteSpace(row.Asset.Nsn)?"":"NSN: "+row.Asset.Nsn+" · ")+"ціна "+(row.Asset.UnitPrice?.ToString("0.####")??"невідома"),row.Asset.Quantity.ToString("0.###")+" "+row.Asset.Measure,()=>
                    {
                        var matches=all.Where(e=>e.Snapshot.AsOf==row.Snapshot.AsOf && e.Asset.Name==row.Asset.Name && e.Asset.Measure==row.Asset.Measure && e.Asset.UnitPrice==row.Asset.UnitPrice && e.Asset.Nsn==row.Asset.Nsn && e.Asset.ItemId==row.Asset.ItemId && e.Asset.Category==row.Asset.Category).ToArray();
                        ShowUnitDistribution(row.Asset.Name,row.Asset.Measure,matches.Select(e=>(e.Snapshot.Unit,e.Asset.Quantity,e.Snapshot.AsOf,(Action?)null)).ToArray());
                    },doubleTap:true);
            }
            if(page>0)Button(list,"← Попередні 50",()=>{page--;Draw();});
            if((page+1)*50<found.Length)Button(list,"Наступні 50 →",()=>{page++;Draw();});
        }
        search.TextChanged+=(_,_)=>{_assetQuery=search.Text??"";page=0;Draw();};
        async void Load()
        {
            try
            {
                snapshots=await Task.Run(()=>
                {
                    using var archive=new DocumentArchiveStore(this);var received=new List<UnitAssetsSnapshot>();
                    foreach(var grant in access.units)
                    {
                        UnitAssetsSnapshot? latest=null;
                        foreach(var summary in archive.ListSummaries(grant.unit).Where(s=>s.Kind=="Знімок залишків").OrderByDescending(s=>s.DocumentDate,StringComparer.Ordinal).ThenByDescending(s=>s.SavedUtc,StringComparer.Ordinal))
                            if(MavkaExchangeParser.TryAssets(archive.FindDocument(summary.DocumentId)?.Json,out latest) && latest is not null)break;
                        if(latest is null)missing.Add(grant.unit);else received.Add(latest);
                    }
                    return received;
                });
                if(generation!=_mirrorReadGeneration || _screen!="assets")return;
                status.Text=$"Отримано підрозділів: {snapshots.Count} із {access.units.Length}"+(missing.Count>0?". Без даних: "+missing.Count:"");Draw();
            }
            catch(Exception ex){if(generation==_mirrorReadGeneration)status.Text="Не вдалося завантажити залишки: "+ExplainOperationError(ex);}
        }
        _refreshCurrentAssets=Load;Load();
    }
}
