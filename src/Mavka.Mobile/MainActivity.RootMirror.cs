using Android.App;
using Android.Widget;
using System.Text.Json;

namespace Mavka.Mobile;
public partial class MainActivity
{
    private bool _showRootMirror=true;
    private long _mirrorReadGeneration;
    private DateOnly? _rootAssetsDate;
    private async Task RecoverMirrorAsync()
    {
        if(_busy || _resetRequested)return;
        using var operation=BeginExchange();var cancellation=operation.Token;
        _busy=true;
        try
        {
            if(new UserProfileStore(this).Load() is null && !LocalAccountReset.IsPending(this))return;
            await RelaySync.Gate.WaitAsync(cancellation);
            IncomingBatchResult posting;
            try{posting=await Task.Run(()=>{var pending=LocalAccountReset.IsPending(this);LocalAccountReset.ResumePending(this);if(pending)_messages=[];if(new UserProfileStore(this).Load() is null)return new IncomingBatchResult(0,0,[]);using var store=new MobileChangeStore(this);store.Recover(cancellation);return IncomingBatchPosting.Apply(this,cancellation:cancellation);});}
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
        IReadOnlyList<MobileMirrorRecord> records=[];
        var generation=++_mirrorReadGeneration;
        var header=Card();var title=Text(header,"Поточні залишки",20,true);
        Text(header,"Актуальний облік головної Мавки",14);
        var access=new MobileAccessStore(this).Load();
        Text(header,location is null?(access?.allUnits==true?"Майно військової частини":"Майно доступних підрозділів"):access?.units.FirstOrDefault(u=>u.locationId==location)?.unit??"Обраний підрозділ",15,true);
        var loading=Text(header,"Завантаження захищених залишків…",13);
        DateOnly? appliedDate=null;
        var dateLabel=Text(header,"Станом на: завантаження…",15,true);
        Button(header,"Станом на…",()=>
        {
            var selected=_rootAssetsDate??appliedDate??DateOnly.FromDateTime(DateTime.Today);
            var picker=new DatePickerDialog(this,(_,date)=>{_rootAssetsDate=new(date.Year,date.Month+1,date.DayOfMonth);ShowAssets();},selected.Year,selected.Month-1,selected.Day);
            picker.DatePicker!.MaxDate=new DateTimeOffset(DateTime.Today).ToUnixTimeMilliseconds();picker.Show();
        });
        Button(header,"Останні дані ПК",()=>{_rootAssetsDate=null;ShowAssets();});
        Button(header,"Обрати підрозділ",()=>ChooseMobileUnit(ShowAssets));
        if(access?.isMvo==true)Button(header,"Журнал зарахувань на телефоні",ShowLocalAssets);
        Button(header,"Документи підрозділу",ShowDocuments);
        var search=ProfileInput(header,"Пошук за назвою або NSN",_assetQuery);var list=new Android.Widget.LinearLayout(this){Orientation=Orientation.Vertical};header.AddView(list);var page=0;var drawVersion=0;
        Button(header,"Скинути пошук",()=>search.Text="");
        async void Draw(bool debounce=false)
        {
            var version=++drawVersion;var query=(search.Text??"").Trim();
            var selectedDate=_rootAssetsDate??appliedDate;if(selectedDate is null)return;
            dateLabel.Text="Станом на: "+selectedDate.Value.ToString("dd.MM.yyyy");title.Text=_rootAssetsDate is null?"Поточні залишки":"Залишки за датою";
            try
            {
            if(appliedDate is null || selectedDate>appliedDate)throw new InvalidDataException(appliedDate is {} known?$"Дані ПК отримано лише станом на {known:dd.MM.yyyy}. Оновіть облік для пізнішої дати.":"Дату даних ПК не вказано. Оновіть базу.");
            if(debounce)await Task.Delay(180);
            if(version!=drawVersion || generation!=_mirrorReadGeneration)return;
            var rendered=await Task.Run(()=>
            {
            var parsedRows=records.Select(r=>(Record:r,Payload:JsonDocument.Parse(r.Json))).ToArray();var currentPolicy=new MobileAccessStore(this).Load();
            try
            {
            var rows=InventorySearchSummary.Filter(parsedRows.Where(r=>currentPolicy?.Allows(r.Record.LocationId,true)==true),query,
                r=>r.Payload.RootElement.GetProperty("name").GetString()!,r=>r.Payload.RootElement.TryGetProperty("nsn",out var nsn)?nsn.GetString():null)
                .Select(r=>(r.Record,r.Payload,Quantity:AssetBalanceTimeline.Quantity(r.Payload.RootElement,selectedDate.Value,appliedDate)))
                .Where(r=>r.Quantity!=0).OrderBy(r=>r.Payload.RootElement.GetProperty("name").GetString(),StringComparer.CurrentCulture).ThenBy(r=>r.Record.LocationId).ToArray();
            var groups=rows.GroupBy(r=>location is null?
                r.Payload.RootElement.GetProperty("itemId")+"|"+r.Payload.RootElement.GetProperty("unit")+"|"+r.Payload.RootElement.GetProperty("price")+"|"+r.Payload.RootElement.GetProperty("category"):
                r.Record.Key).ToArray();
                return (parsedRows,groups);
            }
            catch{foreach(var row in parsedRows)row.Payload.Dispose();throw;}
            });
            var parsedRows=rendered.parsedRows;var groups=rendered.groups;
            if(version!=drawVersion || generation!=_mirrorReadGeneration || _screen!="assets"){foreach(var row in parsedRows)row.Payload.Dispose();return;}
            list.RemoveAllViews();
            page=Math.Min(page,Math.Max(0,(groups.Length-1)/50));Text(list,$"Позицій: {groups.Length} · сторінка {page+1}",13);
            if(groups.Length==0)Text(list,query.Length>0?"За цим запитом майна не знайдено.":"Ненульових залишків немає.",14);
            foreach(var group in groups.Skip(page*50).Take(50))
            {
                var entries=group.ToArray();var payload=entries[0].Payload.RootElement;var selected=entries.Select(e=>e.Record).ToArray();
                var quantity=entries.Sum(e=>e.Quantity);
                var detail=(location is null?"Разом у підрозділах: "+entries.Select(e=>e.Record.LocationId).Distinct().Count():payload.GetProperty("location").GetString())+" · кат. "+payload.GetProperty("category")+" · ціна "+payload.GetProperty("price");
                AddAssetRow(list,payload.GetProperty("name").GetString()!,detail,quantity.ToString("0.############################")+" "+payload.GetProperty("unit"),()=>
                {
                    if(location is not null){ShowRootAsset(selected[0],selectedDate,entries[0].Quantity);return;}
                    var distribution=entries.Select(e=>
                    {
                        using var json=JsonDocument.Parse(e.Record.Json);var root=json.RootElement;
                        return (root.GetProperty("location").GetString()!,e.Quantity,selectedDate.Value.ToString("dd.MM.yyyy"),(Action?)(()=>ShowRootAsset(e.Record,selectedDate,e.Quantity)));
                    }).ToArray();
                    using var first=JsonDocument.Parse(selected[0].Json);
                    ShowUnitDistribution(first.RootElement.GetProperty("name").GetString()!,first.RootElement.GetProperty("unit").GetString()!,distribution);
                },doubleTap:location is null);
            }
            if(page>0)Button(list,"← Попередні 50",()=>{page--;Draw();});if((page+1)*50<groups.Length)Button(list,"Наступні 50 →",()=>{page++;Draw();});
            foreach(var row in parsedRows)row.Payload.Dispose();
            }
            catch(Exception error){if(version==drawVersion && generation==_mirrorReadGeneration && _screen=="assets"){list.RemoveAllViews();Text(list,ExplainOperationError(error),14);}}
        }
        search.TextChanged+=(_,_)=>{_assetQuery=search.Text??"";page=0;Draw(true);};
        async void Load()
        {
            try
            {
                var token=_screenReads.Token;
                var result=await Task.Run(()=>{using var store=new MobileChangeStore(this);return (Rows:store.List("asset",location,token),State:store.AppliedState());},token);
                if(generation!=_mirrorReadGeneration || _screen!="assets")return;
                records=result.Rows;
                appliedDate=result.State?.AsOf;
                loading.Text=result.State is null?"Повний пакет з ПК ще не застосовано. Оновіть пошту або сервер і перевірте стан отримання частин.":
                    result.State.AsOf is {} date?"Перевірені дані ПК станом на "+date.ToString("dd.MM.yyyy")+". Оновлення — на головному екрані.":"Дату застосованих даних не вказано. Оновіть облік із ПК.";
                if(result.State is null){list.RemoveAllViews();Button(list,"Стан оновлення бази",ShowQueueManagement);return;}
                Draw();
            }
            catch(Exception ex){if(generation==_mirrorReadGeneration)loading.Text=ExplainOperationError(ex);}
        }
        _refreshCurrentAssets=Load;Load();return true;
    }
    private async void ShowRootAsset(MobileMirrorRecord record,DateOnly? asOf=null,decimal? quantity=null)
    {
        Begin("root-asset","Картка майна");
        if(new MobileAccessStore(this).Load()?.Allows(record.LocationId,true)!=true){Text(_body,"Доступ відкликано.",15);return;}
        using var json=JsonDocument.Parse(record.Json);var p=json.RootElement;
        var card=Card();Text(card,p.GetProperty("name").GetString()!,19,true);Text(card,p.GetProperty("location").GetString()!,15);
        Text(card,(asOf is {} day?"Станом на "+day.ToString("dd.MM.yyyy")+": ":"Залишок: ")+(quantity?.ToString("0.############################")??p.GetProperty("quantity").ToString())+" "+p.GetProperty("unit")+" · ціна "+p.GetProperty("price"),15);
        var item=p.GetProperty("itemId").GetInt32();var generation=_mirrorReadGeneration;
        var loading=Text(card,"Завантаження документів за весь отриманий період…",13);
        IReadOnlyList<MobileMirrorRecord> history;
        try{var token=_screenReads.Token;history=await Task.Run(()=>{using var store=new MobileChangeStore(this);return store.List("document",record.LocationId,token);},token);}
        catch(Exception ex){loading.Text=ExplainOperationError(ex);return;}
        if(generation!=_mirrorReadGeneration || _screen!="root-asset")return;loading.Text="";
        if(new MobileAccessStore(this).Load()?.Allows(record.LocationId,true)!=true){loading.Text="Доступ відкликано.";return;}
        var docs=await Task.Run(()=>
        {
            var result=new List<(MobileMirrorRecord Record,string Title,string[] Lines)>();
            foreach(var entry in history)
            {
                using var json=JsonDocument.Parse(entry.Json);var root=json.RootElement;
                var lines=root.GetProperty("movements").EnumerateArray().Where(l=>l.GetProperty("itemId").GetInt32()==item).Select(l=>"Рух: "+l.GetProperty("quantity")+" · партія "+l.GetProperty("batch")+" · код "+l.GetProperty("code")+" · серії "+l.GetProperty("serials")).ToArray();
                if(lines.Length>0)result.Add((entry,root.GetProperty("date")+" · "+root.GetProperty("type")+" № "+root.GetProperty("number"),lines));
            }
            return result.OrderByDescending(d=>d.Title,StringComparer.Ordinal).ToArray();
        });
        if(generation!=_mirrorReadGeneration || _screen!="root-asset")return;
        var rows=new LinearLayout(this){Orientation=Orientation.Vertical};card.AddView(rows);var page=0;
        void DrawHistory()
        {
            rows.RemoveAllViews();Text(rows,$"Документів руху: {docs.Length} · сторінка {page+1}",13);
            foreach(var doc in docs.Skip(page*50).Take(50))
            {
                Text(rows,doc.Title,15,true);foreach(var line in doc.Lines.Take(50))Text(rows,line,13);
                Button(rows,"Документ",()=>ShowRootDocument(doc.Record));
            }
            if(page>0)Button(rows,"← Попередні 50",()=>{page--;DrawHistory();});if((page+1)*50<docs.Length)Button(rows,"Наступні 50 →",()=>{page++;DrawHistory();});
        }
        DrawHistory();Button(card,"До майна",ShowAssets);
    }
    private void DrawRootDocuments(int? location)
    {
        DocumentBrowseRow[] records=[];var generation=++_mirrorReadGeneration;
        var card=Card();Text(card,"Документи головної Мавки",18,true);
        Text(card,"Акти, накладні та інші документи лише доступних підрозділів. Перегляд не змінює облік.",12);
        var loading=Text(card,"Завантаження захищених документів…",13);
        var search=ProfileInput(card,"Пошук за номером або підрозділом",_rootDocumentQuery);
        var page=0;var drawVersion=0;
        var rows=new LinearLayout(this){Orientation=Orientation.Vertical};
        var summary=Text(card,_rootDocumentFilter.Summary,12);
        Button(card,"Фільтри",()=>ShowDocumentFilters(_rootDocumentFilter,records.Select(d=>d.Type),records.Select(d=>d.Status),value=>
        { _rootDocumentFilter=value;page=0;summary.Text=value.Summary;Draw(); }));
        card.AddView(rows);
        async void Draw(bool debounce=false)
        {
            var version=++drawVersion;var query=(search.Text??"").Trim();var criteria=_rootDocumentFilter;
            if(debounce)await Task.Delay(180);
            if(version!=drawVersion || generation!=_mirrorReadGeneration)return;
            var currentPolicy=new MobileAccessStore(this).Load();
            var docs=await Task.Run(()=>records.Where(r=>currentPolicy?.Allows(r.Record.LocationId)==true)
                .Where(r=>criteria.Matches(r.Type,r.Status,r.Date))
                .Where(r=>(r.Number+" "+r.Unit+" "+r.Type+" "+r.Date).Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray());
            if(version!=drawVersion || generation!=_mirrorReadGeneration || _screen!="documents")return;
            rows.RemoveAllViews();page=Math.Min(page,Math.Max(0,(docs.Length-1)/50));
            Text(rows,$"Документів: {docs.Length} · сторінка {page+1}",13);
            if(docs.Length==0)Text(rows,"Документів за цим відбором немає.",14);
            foreach(var row in docs.Skip(page*50).Take(50))
            {
                Text(rows,row.Type+" № "+row.Number,16,true);
                Text(rows,row.Date+" · "+row.Unit+" · "+row.Status,13);
                Button(rows,"Переглянути зміни",()=>ShowRootDocument(row.Record));
            }
            if(page>0)Button(rows,"← Попередні 50",()=>{page--;Draw();});
            if((page+1)*50<docs.Length)Button(rows,"Наступні 50 →",()=>{page++;Draw();});
        }
        search.TextChanged+=(_,_)=>{_rootDocumentQuery=search.Text??"";page=0;Draw(true);};
        async void Load()
        {
            try
            {
                var token=_screenReads.Token;
                // Decrypt and parse once per screen, not on every keystroke/page.
                var result=await Task.Run(()=>
                {
                    using var store=new MobileChangeStore(this);var state=store.AppliedState();
                    var docs=new List<DocumentBrowseRow>();
                    foreach(var record in store.List("document",location,token))
                    {
                        token.ThrowIfCancellationRequested();using var json=JsonDocument.Parse(record.Json);var p=json.RootElement;
                        docs.Add(new(record,p.GetProperty("type").GetString()!,p.GetProperty("number").GetString()!,
                            p.GetProperty("date").GetString()!,p.GetProperty("location").GetString()!,p.GetProperty("status").GetString()!));
                    }
                    return (Rows:docs.OrderByDescending(d=>d.Date,StringComparer.Ordinal).ToArray(),State:state);
                },token);
                if(generation!=_mirrorReadGeneration || _screen!="documents")return;
                records=result.Rows;
                loading.Text=result.State is null?"Повний пакет з ПК ще не застосовано. Оновіть пошту або сервер.":
                    result.State.AsOf is {} date?"Дані ПК станом на "+date.ToString("dd.MM.yyyy"):"Дату застосованих даних не вказано.";
                Draw();
            }
            catch(OperationCanceledException) { }
            catch(Exception ex){if(generation==_mirrorReadGeneration && _screen=="documents")loading.Text=ExplainOperationError(ex);}
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
        var movementRows=p.GetProperty("movements").EnumerateArray().Select(r=>r.Clone()).ToArray();var page=0;var drawVersion=0;
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
        if(new MobileAccessStore(this).Load()?.isMvo==true)Text(card,"Збереження змін не означає ручного підтвердження МВО.",12);Button(card,"До документів",ShowDocuments);
    }
}
