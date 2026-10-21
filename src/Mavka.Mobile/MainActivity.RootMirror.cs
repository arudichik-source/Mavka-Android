using Android.App;
using Android.Widget;
using System.Text.Json;

namespace Mavka.Mobile;
public partial class MainActivity
{
    private bool _showRootMirror=true;
    private long _mirrorReadGeneration;
    private async Task RecoverMirrorAsync()
    {
        if(_busy)return;
        _busy=true;
        try
        {
            if(new UserProfileStore(this).Load() is null && !LocalAccountReset.IsPending(this))return;
            await RelaySync.Gate.WaitAsync();
            IncomingBatchResult posting;
            try{posting=await Task.Run(()=>{var pending=LocalAccountReset.IsPending(this);LocalAccountReset.ResumePending(this);if(pending)_messages=[];if(new UserProfileStore(this).Load() is null)return new IncomingBatchResult(0,0,[]);using var store=new MobileChangeStore(this);store.Recover();return IncomingBatchPosting.Apply(this);});}
            finally{RelaySync.Gate.Release();}
            _incomingPostingIssue=posting.Issues.Count>0?posting.Summary:null;
            if(IsFinishing)return;
            if(_screen=="assets")ShowAssets();else if(_screen=="documents")ShowDocuments();else if(_screen=="home")ShowHome();
        }
        catch(Exception ex){System.Diagnostics.Debug.WriteLine("MIRROR_RECOVERY: "+ex.GetType().Name);}
        finally{_busy=false;}
    }
    private bool DrawRootAssets(int? location)
    {
        using(var store=new MobileChangeStore(this))if(!store.HasRecords("asset",location))return false;
        IReadOnlyList<MobileMirrorRecord> records=[];
        var generation=++_mirrorReadGeneration;
        var header=Card();Text(header,"Актуальний облік головної Мавки",18,true);
        Text(header,location is null?(new MobileAccessStore(this).Load()?.allUnits==true?"Майно військової частини":"Майно доступних підрозділів"):"Обраний підрозділ",15,true);
        Text(header,"Останні збережені зміни. Оновлення — на головному екрані.",12);
        Button(header,"Обрати підрозділ",()=>ChooseMobileUnit(ShowAssets));
        Button(header,"Журнал зарахувань на телефоні",()=>{_showRootMirror=false;ShowAssets();});
        var search=ProfileInput(header,"Пошук за назвою або NSN",_assetQuery);var list=new Android.Widget.LinearLayout(this){Orientation=Orientation.Vertical};header.AddView(list);var page=0;
        Button(header,"Скинути пошук",()=>search.Text="");
        void Draw()
        {
            list.RemoveAllViews();var query=(search.Text??"").Trim();
            var parsedRows=records.Select(r=>(Record:r,Payload:JsonDocument.Parse(r.Json))).ToArray();var currentPolicy=new MobileAccessStore(this).Load();
            var rows=InventorySearchSummary.Filter(parsedRows.Where(r=>currentPolicy?.Allows(r.Record.LocationId,true)==true),query,
                r=>r.Payload.RootElement.GetProperty("name").GetString()!,r=>r.Payload.RootElement.TryGetProperty("nsn",out var nsn)?nsn.GetString():null)
                .Where(r=>r.Payload.RootElement.GetProperty("quantity").GetDecimal()!=0).OrderBy(r=>r.Payload.RootElement.GetProperty("name").GetString(),StringComparer.CurrentCulture).ThenBy(r=>r.Record.LocationId).ToArray();
            var groups=rows.GroupBy(r=>location is null?
                r.Payload.RootElement.GetProperty("itemId")+"|"+r.Payload.RootElement.GetProperty("unit")+"|"+r.Payload.RootElement.GetProperty("price")+"|"+r.Payload.RootElement.GetProperty("category"):
                r.Record.Key).ToArray();
            page=Math.Min(page,Math.Max(0,(groups.Length-1)/50));Text(list,$"Позицій: {groups.Length} · сторінка {page+1}",13);
            if(groups.Length==0)Text(list,query.Length>0?"За цим запитом майна не знайдено.":"Ненульових залишків немає.",14);
            foreach(var group in groups.Skip(page*50).Take(50))
            {
                var entries=group.ToArray();var payload=entries[0].Payload.RootElement;var selected=entries.Select(e=>e.Record).ToArray();
                var quantity=entries.Sum(e=>e.Payload.RootElement.GetProperty("quantity").GetDecimal());
                var detail=(location is null?"Разом у підрозділах: "+entries.Select(e=>e.Record.LocationId).Distinct().Count():payload.GetProperty("location").GetString())+" · кат. "+payload.GetProperty("category")+" · ціна "+payload.GetProperty("price");
                AddAssetRow(list,payload.GetProperty("name").GetString()!,detail,quantity.ToString("0.############################")+" "+payload.GetProperty("unit"),()=>
                {
                    if(selected.Length==1){ShowRootAsset(selected[0]);return;}
                    var labels=selected.Select(e=>{using var json=JsonDocument.Parse(e.Json);return json.RootElement.GetProperty("location")+" · "+json.RootElement.GetProperty("quantity");}).ToArray();
                    new AlertDialog.Builder(this).SetTitle("Розподіл між підрозділами").SetItems(labels,(_,args)=>ShowRootAsset(selected[args.Which])).Show();
                });
            }
            if(page>0)Button(list,"← Попередні 50",()=>{page--;Draw();});if((page+1)*50<groups.Length)Button(list,"Наступні 50 →",()=>{page++;Draw();});
            foreach(var row in parsedRows)row.Payload.Dispose();
        }
        search.TextChanged+=(_,_)=>{_assetQuery=search.Text??"";page=0;Draw();};
        var loading=Text(header,"Завантаження захищених залишків…",13);
        async void Load()
        {
            try{records=await Task.Run(()=>{using var store=new MobileChangeStore(this);return store.List("asset",location);});if(generation!=_mirrorReadGeneration || _screen!="assets")return;loading.Text="";Draw();}
            catch(Exception ex){if(generation==_mirrorReadGeneration)loading.Text=ExplainOperationError(ex);}
        }
        _refreshCurrentAssets=Load;Load();return true;
    }
    private async void ShowRootAsset(MobileMirrorRecord record)
    {
        Begin("root-asset","Картка майна");
        if(new MobileAccessStore(this).Load()?.Allows(record.LocationId,true)!=true){Text(_body,"Доступ відкликано.",15);return;}
        using var json=JsonDocument.Parse(record.Json);var p=json.RootElement;
        var card=Card();Text(card,p.GetProperty("name").GetString()!,19,true);Text(card,p.GetProperty("location").GetString()!,15);
        Text(card,"Залишок: "+p.GetProperty("quantity")+" "+p.GetProperty("unit")+" · ціна "+p.GetProperty("price"),15);
        var item=p.GetProperty("itemId").GetInt32();var generation=_mirrorReadGeneration;
        var loading=Text(card,"Завантаження історії…",13);
        IReadOnlyList<MobileMirrorRecord> history;
        try{history=await Task.Run(()=>{using var store=new MobileChangeStore(this);return store.List("document",record.LocationId);});}
        catch(Exception ex){loading.Text=ExplainOperationError(ex);return;}
        if(generation!=_mirrorReadGeneration || _screen!="root-asset")return;loading.Text="";
        if(new MobileAccessStore(this).Load()?.Allows(record.LocationId,true)!=true){loading.Text="Доступ відкликано.";return;}
        var docs=history.Select(r=>(Record:r,Doc:JsonDocument.Parse(r.Json))).ToArray();
        foreach(var doc in docs.OrderByDescending(d=>d.Doc.RootElement.GetProperty("date").GetString(),StringComparer.Ordinal))
        {
            var root=doc.Doc.RootElement;var lines=root.GetProperty("movements").EnumerateArray().Where(l=>l.GetProperty("itemId").GetInt32()==item).ToArray();if(lines.Length==0)continue;
            Text(card,root.GetProperty("date")+" · "+root.GetProperty("type")+" № "+root.GetProperty("number"),15,true);
            foreach(var line in lines)Text(card,"Рух: "+line.GetProperty("quantity")+" · партія "+line.GetProperty("batch")+" · код "+line.GetProperty("code")+" · серії "+line.GetProperty("serials"),13);
            var selected=doc.Record;Button(card,"Документ",()=>ShowRootDocument(selected));
        }
        foreach(var doc in docs)doc.Doc.Dispose();Button(card,"До майна",ShowAssets);
    }
    private void DrawRootDocuments(int? location)
    {
        using(var store=new MobileChangeStore(this))if(!store.HasRecords("document",location))return;
        IReadOnlyList<MobileMirrorRecord> records=[];var generation=++_mirrorReadGeneration;
        var card=Card();Text(card,"Документи головної Мавки",18,true);Text(card,"Акти, накладні та інші зміни обліку. Нові накладні для МВО також з'являються нижче для ручного зарахування.",12);
        var search=ProfileInput(card,"Пошук за номером або підрозділом","");var rows=new Android.Widget.LinearLayout(this){Orientation=Orientation.Vertical};card.AddView(rows);var page=0;
        void Draw()
        {
            rows.RemoveAllViews();var query=(search.Text??"").Trim();
            var parsedDocs=records.Select(r=>(Record:r,Doc:JsonDocument.Parse(r.Json))).ToArray();var currentPolicy=new MobileAccessStore(this).Load();var docs=parsedDocs.Where(r=>currentPolicy?.Allows(r.Record.LocationId)==true).Where(r=>(r.Doc.RootElement.GetProperty("number")+" "+r.Doc.RootElement.GetProperty("location")+" "+r.Doc.RootElement.GetProperty("type")).Contains(query,StringComparison.OrdinalIgnoreCase)).OrderByDescending(r=>r.Doc.RootElement.GetProperty("date").GetString(),StringComparer.Ordinal).ToArray();
            page=Math.Min(page,Math.Max(0,(docs.Length-1)/50));Text(rows,$"Документів: {docs.Length} · сторінка {page+1}",13);
            foreach(var row in docs.Skip(page*50).Take(50))
            {
                var p=row.Doc.RootElement;var selected=row.Record;
                Text(rows,p.GetProperty("type")+" № "+p.GetProperty("number"),16,true);Text(rows,p.GetProperty("date")+" · "+p.GetProperty("location")+" · "+p.GetProperty("status"),13);Button(rows,"Переглянути зміни",()=>ShowRootDocument(selected));
            }
            if(page>0)Button(rows,"← Попередні 50",()=>{page--;Draw();});if((page+1)*50<docs.Length)Button(rows,"Наступні 50 →",()=>{page++;Draw();});foreach(var doc in parsedDocs)doc.Doc.Dispose();
        }
        search.TextChanged+=(_,_)=>{page=0;Draw();};
        var loading=Text(card,"Завантаження захищених документів…",13);
        async void Load()
        {
            try{records=await Task.Run(()=>{using var store=new MobileChangeStore(this);return store.List("document",location);});if(generation!=_mirrorReadGeneration || _screen!="documents")return;loading.Text="";Draw();}
            catch(Exception ex){if(generation==_mirrorReadGeneration)loading.Text=ExplainOperationError(ex);}
        }
        Load();
    }
    private void ShowRootDocument(MobileMirrorRecord record)
    {
        Begin("root-document","Документ головної Мавки");
        // Recheck scope before opening a row captured by an older screen.
        if(new MobileAccessStore(this).Load()?.Allows(record.LocationId)!=true){Text(_body,"Доступ до підрозділу відкликано.",15);return;}
        using var json=JsonDocument.Parse(record.Json);var p=json.RootElement;var card=Card();
        Text(card,p.GetProperty("type")+" № "+p.GetProperty("number"),19,true);Text(card,p.GetProperty("date")+" · "+p.GetProperty("location")+" · "+p.GetProperty("status"),14);
        var movementRows=p.GetProperty("movements").EnumerateArray().Select(r=>r.Clone()).ToArray();var page=0;
        var rows=new Android.Widget.LinearLayout(this){Orientation=Orientation.Vertical};card.AddView(rows);
        void Draw()
        {
            rows.RemoveAllViews();Text(rows,$"Рядків руху: {movementRows.Length} · сторінка {page+1}",13);
            foreach(var row in movementRows.Skip(page*50).Take(50))
            {
                Text(rows,row.GetProperty("name").GetString()!,16,true);Text(rows,"Рух: "+row.GetProperty("quantity")+" "+row.GetProperty("unit")+" · ціна "+row.GetProperty("price")+" · кат. "+row.GetProperty("category"),14);
                Text(rows,"Партія: "+row.GetProperty("batch")+" · код: "+row.GetProperty("code")+" · серії: "+row.GetProperty("serials"),13);
            }
            if(page>0)Button(rows,"← Попередні 50",()=>{page--;Draw();});if((page+1)*50<movementRows.Length)Button(rows,"Наступні 50 →",()=>{page++;Draw();});
        }
        Draw();
        if(p.TryGetProperty("chunkCount",out var chunks) && chunks.GetInt32()>1 && p.GetProperty("type").GetString()!.Contains("накладна",StringComparison.OrdinalIgnoreCase) && new MobileAccessStore(this).Load()?.isMvo==true)
            Text(card,"Для цієї великої накладної доступний повний перегляд; її локальне зарахування на телефоні поки недоступне.",13);
        Text(card,"Збереження змін не означає ручного підтвердження МВО.",12);Button(card,"До документів",ShowDocuments);
    }
}
