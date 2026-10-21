using Android.App;
using Android.Widget;
namespace Mavka.Mobile;
public partial class MainActivity
{
    private void ShowUnitDistribution(string name,string measure,(string Unit,decimal Quantity,string Date,Action? Open)[] rows)
    {
        var policy=new MobileAccessStore(this).Load();if(policy?.canViewAssets!=true)return;
        rows=rows.Where(r=>policy.units.Any(u=>u.unit.Equals(r.Unit,StringComparison.OrdinalIgnoreCase))).OrderBy(r=>r.Unit,StringComparer.CurrentCulture).ToArray();
        var body=new LinearLayout(this){Orientation=Orientation.Vertical};body.SetPadding(Dp(16),Dp(8),Dp(16),Dp(8));
        Text(body,name,18,true);Text(body,"Усього: "+rows.Sum(r=>r.Quantity).ToString("0.############################")+" "+measure,17,true);
        Text(body,"Підрозділів: "+rows.Select(r=>r.Unit).Distinct(StringComparer.OrdinalIgnoreCase).Count(),13);
        foreach(var row in rows)
        {
            Text(body,row.Unit+" — "+row.Quantity.ToString("0.############################")+" "+measure+(row.Date.Length>0?" · "+row.Date:""),15);
            if(row.Open is not null)Button(body,"Картка: "+row.Unit,row.Open);
        }
        var scroll=new ScrollView(this);scroll.AddView(body);
        new AlertDialog.Builder(this).SetTitle("Розподіл майна між підрозділами").SetView(scroll).SetPositiveButton("Закрити",(_,_)=>{}).Show();
    }
}
