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
        if(!policy.isMvo)
        {
            if(!string.IsNullOrWhiteSpace(_incomingPostingIssue)){var issue=Card();Text(issue,"Стан автоматичного зарахування",16,true);Text(issue,_incomingPostingIssue,13);}
            return;
        }
        var card=Card();
        Text(card,policy.isMvo?"Зарахування отриманого майна":"Надходження в доступних підрозділах",16,true);
        Text(card,policy.isMvo
            ? "Можна підтвердити всі збережені надходження свого підрозділу однією дією."
            : "Залишки та нові накладні зараховуються автоматично в усіх доступних підрозділах. Повторно підтверджувати кожен підрозділ не потрібно.",13);
        var status=Text(card,_incomingPostingIssue??"",12);
        Button(card,"Зарахувати всі отримані надходження",()=>
        {
            if(_busy || _resetRequested)return;
            var current=new MobileAccessStore(this).Load();
            if(current is null)return;
            new AlertDialog.Builder(this).SetTitle("Зарахувати всі отримані надходження?")
                .SetMessage($"Доступних підрозділів: {current.units.Length}.\nЗарахуються збережені початкові залишки й накладні всіх цих підрозділів, незалежно від відкритого фільтра. Уже зараховані документи повторно не додаються.")
                .SetNegativeButton("Скасувати",(_,_)=>{})
                .SetPositiveButton("Зарахувати всі",async(_,_)=>
                {
                    if(_busy || _resetRequested)return;
                    using var operation=BeginExchange();var cancellation=operation.Token;
                    _busy=true;status.Text="Зарахування всіх доступних надходжень…";
                    try
                    {
                        await RelaySync.Gate.WaitAsync(cancellation);
                        IncomingBatchResult result;
                        try{result=await Task.Run(()=>IncomingBatchPosting.Apply(this,manual:true,cancellation:cancellation));}
                        finally{RelaySync.Gate.Release();}
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

    private void PromptFullAccountReset(TextView status)
    {
        if(_resetRequested){status.Text="Очищення вже виконується.";return;}
        var word=new EditText(this){Hint="Введіть ОБНУЛИТИ або RESET",InputType=Android.Text.InputTypes.ClassText};
        ConfigureKeyboard(word);word.SetSingleLine(true);
        new AlertDialog.Builder(this).SetTitle("Обнулити облік на телефоні?")
            .SetMessage("Буде остаточно видалено залишки й рухи всіх підрозділів, архів документів разом із кошиком, отримані вкладення та черги передачі документів на цьому телефоні.\n\nАкаунт, підключення, паролі, ключі шифрування й доступ до підрозділів збережуться. Для подальшої роботи отримайте актуальні залишки з головної Мавки повторно.\n\nДля підтвердження введіть ОБНУЛИТИ.")
            .SetView(word).SetNegativeButton("Скасувати",(_,_)=>{})
            .SetPositiveButton("Обнулити",async(_,_)=>
            {
                if(word.Text?.Trim() is not ("ОБНУЛИТИ" or "RESET")){status.Text="Контрольне слово не збігається. Облік не змінено.";return;}
                if(_resetRequested)return;
                if(_localBusy && _exchangeOperation is null){status.Text="Завершіть поточне збереження перед очищенням.";return;}
                _resetRequested=true;
                StopForegroundRefresh();_screenReads.Cancel();_mirrorReadGeneration++;
                status.Text="Скасування обміну перед очищенням…";
                await WidgetSyncJob.CancelAndWait(this);
                var active=_exchangeOperation;active?.Cancel();
                if(active is not null)await active.Finished;
                _busy=true;status.Text="Очищення всього локального обліку…";
                try
                {
                    await RelaySync.Gate.WaitAsync();
                    try{await Task.Run(()=>LocalAccountReset.Clear(this));}
                    finally{RelaySync.Gate.Release();}
                    _messages=[];_documentPage=0;_assetQuery="";_showReferenceSnapshot=false;_showRootMirror=true;_mirrorReadGeneration++;_incomingPostingIssue=null;
                    MavkaHomeWidget.Refresh(this);
                    status.Text="Облік усіх підрозділів і документи очищено. Акаунт, паролі, ключі та доступ збережені.";
                }
                catch(Exception ex){_messages=[];status.Text="Очищення не завершено: "+ExplainOperationError(ex)+". Повторіть очищення; воно також продовжиться після перезапуску.";}
                finally{_busy=false;_resetRequested=false;StartForegroundRefresh();}
            }).Show();
    }
}
