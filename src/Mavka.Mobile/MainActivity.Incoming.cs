using Android.App;
using Android.Widget;

namespace Mavka.Mobile;

public partial class MainActivity
{
    private string? _incomingPostingIssue;

    private void DrawIncomingBatchAction()
    {
        var policy=new MobileAccessStore(this).Load();
        if(policy is null || !policy.canViewAssets && !policy.canViewDocuments)return;
        var card=Card();
        Text(card,policy.isMvo?"Зарахування отриманого майна":"Надходження в доступних підрозділах",16,true);
        Text(card,policy.isMvo
            ? "Можна підтвердити всі збережені надходження свого підрозділу однією дією."
            : "Залишки та нові накладні зараховуються автоматично в усіх доступних підрозділах. Повторно підтверджувати кожен підрозділ не потрібно.",13);
        var status=Text(card,_incomingPostingIssue??"",12);
        Button(card,"Зарахувати всі отримані надходження",()=>
        {
            if(_busy)return;
            var current=new MobileAccessStore(this).Load();
            if(current is null)return;
            new AlertDialog.Builder(this).SetTitle("Зарахувати всі отримані надходження?")
                .SetMessage($"Доступних підрозділів: {current.units.Length}.\nЗарахуються збережені початкові залишки й накладні всіх цих підрозділів, незалежно від відкритого фільтра. Уже зараховані документи повторно не додаються.")
                .SetNegativeButton("Скасувати",(_,_)=>{})
                .SetPositiveButton("Зарахувати всі",async(_,_)=>
                {
                    if(_busy)return;_busy=true;status.Text="Зарахування всіх доступних надходжень…";
                    try
                    {
                        var result=await Task.Run(()=>IncomingBatchPosting.Apply(this,manual:true));
                        _incomingPostingIssue=result.Issues.Count>0?result.Summary:null;
                        if(IsFinishing)return;
                        if(_screen=="assets")ShowAssets();else if(_screen=="documents")ShowDocuments();
                        Toast.MakeText(this,$"Залишків підрозділів: {result.OpeningUnits}; накладних: {result.Invoices}.",ToastLength.Long)?.Show();
                        if(result.Issues.Count>0)new AlertDialog.Builder(this).SetTitle("Результат зарахування")
                            .SetMessage(result.Summary).SetPositiveButton("Зрозуміло",(_,_)=>{}).Show();
                    }
                    catch(Exception ex){status.Text="Зарахування не завершено: "+ExplainOperationError(ex)+". Можна безпечно повторити.";}
                    finally{_busy=false;}
                }).Show();
        });
    }
}
