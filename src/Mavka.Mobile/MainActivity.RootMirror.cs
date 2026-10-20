using Android.App;
using Android.Widget;
using System.Text.Json;

namespace Mavka.Mobile;
public partial class MainActivity
{
    private bool _showRootMirror=true;
    private bool DrawRootAssets(int? location)
    {
        using var store=new MobileChangeStore(this);var records=store.List("asset",location);
        if(records.Count==0)return false;
        var header=Card();Text(header,"Актуальний облік головної Мавки",18,true);
        Text(header,location is null?"Усі доступні підрозділи":"Обраний підрозділ",13);
        Text(header,"Останні збережені зміни. Оновлення — на головному екрані.",12);
        Button(header,"Обрати підрозділ",()=>ChooseMobileUnit(ShowAssets));
        Button(header,"Локально зараховане майно",()=>{_showRootMirror=false;ShowAssets();});
        var search=ProfileInput(header,"Пошук майна","");var list=new Android.Widget.LinearLayout(this){Orientation=Orientation.Vertical};header.AddView(list);var page=0;
        void Draw()
        {
            list.RemoveAllViews();var query=(search.Text??"").Trim();
            var rows=records.Select(r=>(Record:r,Payload:JsonDocument.Parse(r.Json))).Where(r=>r.Payload.RootElement.GetProperty("name").GetString()!.Contains(query,StringComparison.OrdinalIgnoreCase) && r.Payload.RootElement.GetProperty("quantity").GetDecimal()!=0).OrderBy(r=>r.Payload.RootElement.GetProperty("name").GetString(),StringComparer.CurrentCulture).ThenBy(r=>r.Record.LocationId).ToArray();
            page=Math.Min(page,Math.Max(0,(rows.Length-1)/50));Text(list,$"Позицій: {rows.Length} · сторінка {page+1}",13);
            foreach(var row in rows.Skip(page*50).Take(50))
            {
                var payload=row.Payload.RootElement;var selected=row.Record;
                AddAssetRow(list,payload.GetProperty("name").GetString()!,payload.GetProperty("location").GetString()+" · кат. "+payload.GetProperty("category")+" · ціна "+payload.GetProperty("price"),payload.GetProperty("quantity")+" "+payload.GetProperty("unit"),()=>ShowRootAsset(selected));
            }
            if(page>0)Button(list,"← Попередні 50",()=>{page--;Draw();});if((page+1)*50<rows.Length)Button(list,"Наступні 50 →",()=>{page++;Draw();});
            foreach(var row in rows)row.Payload.Dispose();
        }
        search.TextChanged+=(_,_)=>{page=0;Draw();};Draw();return true;
    }
    private void ShowRootAsset(MobileMirrorRecord record)
    {
        Begin("root-asset","Картка майна");
        if(new MobileAccessStore(this).Load()?.Allows(record.LocationId,true)!=true){Text(_body,"Доступ відкликано.",15);return;}
        using var json=JsonDocument.Parse(record.Json);var p=json.RootElement;
        var card=Card();Text(card,p.GetProperty("name").GetString()!,19,true);Text(card,p.GetProperty("location").GetString()!,15);
        Text(card,"Залишок: "+p.GetProperty("quantity")+" "+p.GetProperty("unit")+" · ціна "+p.GetProperty("price"),15);
        using var store=new MobileChangeStore(this);
        var item=p.GetProperty("itemId").GetInt32();var docs=store.List("document",record.LocationId).Select(r=>(Record:r,Doc:JsonDocument.Parse(r.Json))).ToArray();
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
        using var store=new MobileChangeStore(this);var records=store.List("document",location);if(records.Count==0)return;
        var card=Card();Text(card,"Документи головної Мавки",18,true);Text(card,"Акти, накладні та інші зміни обліку. Нові накладні для МВО також з'являються нижче для ручного зарахування.",12);
        var search=ProfileInput(card,"Пошук за номером або підрозділом","");var rows=new Android.Widget.LinearLayout(this){Orientation=Orientation.Vertical};card.AddView(rows);var page=0;
        void Draw()
        {
            rows.RemoveAllViews();var query=(search.Text??"").Trim();
            var docs=records.Select(r=>(Record:r,Doc:JsonDocument.Parse(r.Json))).Where(r=>(r.Doc.RootElement.GetProperty("number")+" "+r.Doc.RootElement.GetProperty("location")+" "+r.Doc.RootElement.GetProperty("type")).Contains(query,StringComparison.OrdinalIgnoreCase)).OrderByDescending(r=>r.Doc.RootElement.GetProperty("date").GetString(),StringComparer.Ordinal).ToArray();
            page=Math.Min(page,Math.Max(0,(docs.Length-1)/50));Text(rows,$"Документів: {docs.Length} · сторінка {page+1}",13);
            foreach(var row in docs.Skip(page*50).Take(50))
            {
                var p=row.Doc.RootElement;var selected=row.Record;
                Text(rows,p.GetProperty("type")+" № "+p.GetProperty("number"),16,true);Text(rows,p.GetProperty("date")+" · "+p.GetProperty("location")+" · "+p.GetProperty("status"),13);Button(rows,"Переглянути зміни",()=>ShowRootDocument(selected));
            }
            if(page>0)Button(rows,"← Попередні 50",()=>{page--;Draw();});if((page+1)*50<docs.Length)Button(rows,"Наступні 50 →",()=>{page++;Draw();});foreach(var doc in docs)doc.Doc.Dispose();
        }
        search.TextChanged+=(_,_)=>{page=0;Draw();};Draw();
    }
    private void ShowRootDocument(MobileMirrorRecord record)
    {
        Begin("root-document","Документ головної Мавки");
        // Recheck scope before opening a row captured by an older screen.
        if(new MobileAccessStore(this).Load()?.Allows(record.LocationId)!=true){Text(_body,"Доступ до підрозділу відкликано.",15);return;}
        using var json=JsonDocument.Parse(record.Json);var p=json.RootElement;var card=Card();
        Text(card,p.GetProperty("type")+" № "+p.GetProperty("number"),19,true);Text(card,p.GetProperty("date")+" · "+p.GetProperty("location")+" · "+p.GetProperty("status"),14);
        foreach(var row in p.GetProperty("movements").EnumerateArray())
        {
            Text(card,row.GetProperty("name").GetString()!,16,true);Text(card,"Рух: "+row.GetProperty("quantity")+" "+row.GetProperty("unit")+" · ціна "+row.GetProperty("price")+" · кат. "+row.GetProperty("category"),14);
            Text(card,"Партія: "+row.GetProperty("batch")+" · код: "+row.GetProperty("code")+" · серії: "+row.GetProperty("serials"),13);
        }
        Text(card,"Збереження змін не означає ручного підтвердження МВО.",12);Button(card,"До документів",ShowDocuments);
    }
}
