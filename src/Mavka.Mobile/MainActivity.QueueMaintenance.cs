using Android.App;
using Android.Widget;
namespace Mavka.Mobile;
public partial class MainActivity
{
    private void ShowQueueManagement()
    {
        Begin("queue-management","Оновлення бази та черга");var card=Card();Text(card,"Оновлення з головної Мавки",19,true);Text(card,"Запит містить звіт про застосовані майно й документи. ПК запропонує потрібні зміни. Після їх надсилання оновіть пошту або сервер.",14);
        using var mirror=new MobileChangeStore(this);var status=Text(card,mirror.QueueNotice.Length>0?mirror.QueueNotice:"Застосовані дані зберігаються окремо від частин черги.",13);
        Button(card,"Запросити оновлення бази з ПК",async()=>await RequestStateUpdate(status));Button(card,"Скасувати очікування оновлення з ПК",async()=>await CancelStateUpdate(status));
        Text(card,"Очищення черги",18,true);Text(card,"Застарілі частини прибираються автоматично після перевіреного оновлення. Зберігаються три останні автоматичні копії; ручні копії залишаються окремо.",13);
        Button(card,"Очистити чергу (з резервною копією)",()=>ConfirmQueueClear(status));Button(card,"Перевірити копію черги",async()=>await MaintainQueue(status,false));Button(card,"На головну",ShowHome);
    }
    private void ConfirmQueueClear(TextView status)
    {
        new AlertDialog.Builder(this)!.SetTitle("Очистити отримані частини?")!
            .SetMessage("Спочатку буде збережено перевірену зашифровану копію черги. Застосовані майно, документи, паролі й доступ залишаться. Після очищення надішліть запит оновлення бази з ПК.")!
            .SetPositiveButton("Зберегти копію й очистити",async(_,_)=>await MaintainQueue(status,true))!
            .SetNegativeButton("Скасувати",(_,_)=>{} )!.Show();
    }
    private async Task MaintainQueue(TextView status,bool clear)
    {
        if(_busy || _exchangeOperation is not null){status.Text="Зачекайте завершення поточного обміну.";return;}
        using var operation=BeginExchange();_busy=true;
        try
        {
            status.Text=clear?"Створюю копію та перевіряю чергу…":"Перевіряю копію черги та стан обліку…";
            await RelaySync.Gate.WaitAsync(operation.Token);
            try{status.Text=await Task.Run(()=>{using var mirror=new MobileChangeStore(this);return clear?mirror.CleanQueue(true,operation.Token):mirror.CheckLatestQueueBackup(operation.Token);},operation.Token);}
            finally{RelaySync.Gate.Release();}
        }
        catch(Exception error){status.Text=ExplainOperationError(error);}
        finally{_busy=false;}
    }
}
