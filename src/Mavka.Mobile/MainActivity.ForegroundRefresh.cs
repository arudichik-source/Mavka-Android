using Android.Content;
using Android.Widget;

namespace Mavka.Mobile;
public partial class MainActivity
{
    private const string ForegroundPrefs="mavka_foreground_sync_v1";
    private CancellationTokenSource? _foregroundRun;
    private bool _activityResumed;
    private TextView? _foregroundInfo;
    private DateTimeOffset? _foregroundNext;
    private ISharedPreferences ForegroundSettings=>GetSharedPreferences(ForegroundPrefs,FileCreationMode.Private)!;

    protected override void OnResume()
    {
        base.OnResume();Window?.AddFlags(Android.Views.WindowManagerFlags.KeepScreenOn);_activityResumed=true;StartForegroundRefresh();
    }
    protected override void OnPause()
    {
        Window?.ClearFlags(Android.Views.WindowManagerFlags.KeepScreenOn);_activityResumed=false;StopForegroundRefresh();base.OnPause();
    }
    protected override void OnDestroy()
    {
        StopForegroundRefresh();_exchangeOperation?.Cancel();_screenReads.Cancel();base.OnDestroy();
    }
    private void StopForegroundRefresh()
    {
        _foregroundRun?.Cancel();_foregroundRun=null;_foregroundNext=null;
    }
    private void StartForegroundRefresh()
    {
        StopForegroundRefresh();
        if(!_activityResumed || !ForegroundSettings.GetBoolean("enabled",false))return;
        var cancellation=_foregroundRun=new CancellationTokenSource();
        _=ForegroundLoop(cancellation);
    }
    private void DrawForegroundRefresh(LinearLayout parent)
    {
        Text(parent,"Оновлення",17,true);
        var settings=ForegroundSettings;
        var toggle=new Button(this){Text=settings.GetBoolean("enabled",false)?"Автооновлення: ВКЛ":"Автооновлення: ВИКЛ"};StylePrimaryButton(toggle);parent.AddView(toggle);
        toggle.Click+=(_,_)=>
        {
            var enabled=!ForegroundSettings.GetBoolean("enabled",false);
            using var editor=ForegroundSettings.Edit()!;
            if(!editor.PutBoolean("enabled",enabled)!.Commit())return;
            toggle.Text=enabled?"Автооновлення: ВКЛ":"Автооновлення: ВИКЛ";StartForegroundRefresh();UpdateForegroundInfo();
        };
        var options=new LinearLayout(this){Orientation=Orientation.Vertical,Visibility=Android.Views.ViewStates.Gone};
        Button(parent,"Налаштувати інтервал і канал",()=>options.Visibility=options.Visibility==Android.Views.ViewStates.Gone?Android.Views.ViewStates.Visible:Android.Views.ViewStates.Gone);
        parent.AddView(options);
        Text(options,"Інтервал автооновлення",13);
        var minutes=ForegroundPollingPolicy.Intervals;var interval=new Spinner(this);
        interval.Adapter=new ArrayAdapter<string>(this,Android.Resource.Layout.SimpleSpinnerDropDownItem,minutes.Select(m=>m==60?"Раз на годину":m%60==0?"Раз на "+m/60+" год":m+" хв").ToArray());
        interval.SetSelection(Array.IndexOf(minutes,ForegroundPollingPolicy.Minutes(settings.GetInt("minutes",60))));options.AddView(interval);
        interval.ItemSelected+=(_,e)=>
        {
            var value=minutes[e.Position];if(value==ForegroundPollingPolicy.Minutes(ForegroundSettings.GetInt("minutes",60)))return;
            using var editor=ForegroundSettings.Edit()!;if(editor.PutInt("minutes",value)!.Commit()){StartForegroundRefresh();UpdateForegroundInfo();}
        };
        Text(options,"Канал автооновлення",13);
        var channels=new Spinner(this);channels.Adapter=new ArrayAdapter<string>(this,Android.Resource.Layout.SimpleSpinnerDropDownItem,new[]{"Пошта Gmail","Сервер Cloudflare","Пошта та сервер"});
        channels.SetSelection(Math.Clamp(settings.GetInt("channel",0),0,2));options.AddView(channels);
        channels.ItemSelected+=(_,e)=>
        {
            if(e.Position==ForegroundSettings.GetInt("channel",0))return;
            using var editor=ForegroundSettings.Edit()!;if(editor.PutInt("channel",e.Position)!.Commit())StartForegroundRefresh();
        };
        _foregroundInfo=Text(parent,"",12);UpdateForegroundInfo();
        Text(parent,"Працює лише поки Мавка відкрита на екрані. Після згортання чи закриття перевірки припиняються.",12);
    }
    private void UpdateForegroundInfo(string? result=null)
    {
        if(result is not null)ReportExchangeStatus(null,result,_exchangeOperation is null || !_busy);
        if(_foregroundInfo is null)return;
        var last=ForegroundSettings.GetLong("last",0);
        _foregroundInfo.Text=(last==0?"Перевірок ще не було.":"Остання: "+DateTimeOffset.FromUnixTimeSeconds(last).ToLocalTime().ToString("HH:mm:ss"))+
            (_foregroundNext is { } next?" · наступна: "+next.ToLocalTime().ToString("HH:mm:ss"):" · автооновлення вимкнено або призупинено.")+
            (result is null?"":"\n"+result);
    }
    private async Task ForegroundLoop(CancellationTokenSource cancellation)
    {
        var token=cancellation.Token;var failures=0;
        try
        {
            _foregroundNext=DateTimeOffset.UtcNow+ForegroundPollingPolicy.Delay(ForegroundSettings.GetInt("minutes",60),0);UpdateForegroundInfo();
            while(!token.IsCancellationRequested)
            {
                // Sleep until due instead of waking every five seconds for an
                // hourly interval. OnPause cancels this timer and any active I/O.
                var wait=(_foregroundNext??DateTimeOffset.UtcNow)-DateTimeOffset.UtcNow;
                await Task.Delay(wait>TimeSpan.Zero?wait:TimeSpan.FromSeconds(30),token);
                if(_resetRequested || _foregroundNext is not { } due || DateTimeOffset.UtcNow<due || !ForegroundPollingPolicy.CanRun(ForegroundSettings.GetBoolean("enabled",false),_activityResumed,_busy))continue;
                using var operation=BeginExchange(token,TimeSpan.FromMinutes(15));
                var operationToken=operation.Token;
                _busy=true;string result;
                try
                {
                    LocalAccountReset.EnsureReady(this);
                    var channel=Math.Clamp(ForegroundSettings.GetInt("channel",0),0,2);var reports=new List<string>();
                    if(channel is 0 or 2)
                    {
                        if(!_store.HasCredentials || string.IsNullOrWhiteSpace(_store.GetMainMavkaEmail()))throw new InvalidOperationException("Підключіть Gmail у налаштуваннях.");
                        var password=_store.GetPassword()!;var sender=_store.GetMainMavkaEmail();
                        reports.Add(await ReceiveGmailUpdatesAsync(password,sender,operationToken,text=>UpdateForegroundInfo(text)));
                    }
                    token.ThrowIfCancellationRequested();
                    if(channel is 1 or 2)
                    {
                        if(!new RelayIdentity(this).Enabled)throw new InvalidOperationException("Завершіть QR-підключення сервера.");
                        reports.Add(await Task.Run(()=>new RelaySync(this).SynchronizeNow(operationToken),operationToken));
                    }
                    token.ThrowIfCancellationRequested();
                    using(var editor=ForegroundSettings.Edit()!)if(!editor.PutLong("last",DateTimeOffset.UtcNow.ToUnixTimeSeconds())!.Commit())throw new IOException("Не вдалося зберегти час перевірки.");
                    result=string.Join("\n",reports);failures=0;
                    if(_screen=="assets")_refreshCurrentAssets?.Invoke();
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch(Exception ex){failures++;result="Автооновлення: "+ExplainOperationError(ex);}
                finally{_busy=false;}
                token.ThrowIfCancellationRequested();
                _foregroundNext=DateTimeOffset.UtcNow+ForegroundPollingPolicy.Delay(ForegroundSettings.GetInt("minutes",60),failures);UpdateForegroundInfo(result);
            }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        finally{if(ReferenceEquals(_foregroundRun,cancellation)){_foregroundRun=null;_foregroundNext=null;}cancellation.Dispose();}
    }
}
