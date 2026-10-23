#if MAVKA_BOOTSTRAP_QA
using Android.App;
using Android.OS;
using Android.Widget;

namespace Mavka.Mobile;

[Activity(Name="ua.mavka.mobile.WidgetQaActivity", Exported=true)]
public sealed class WidgetQaActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Android.OS.Build.Fingerprint?.Contains("generic") != true && Android.OS.Build.Model?.Contains("sdk") != true)
            throw new InvalidOperationException("Emulator only");
        if(new UserProfileStore(this).Load()?.FullName!="SYNTHETIC-QA-USER" || new GmailCredentialsStore(this).HasCredentials ||
            new RelayIdentity(this).Enabled || new ExchangeKeyStore(this).HasKeyPair)throw new InvalidOperationException("Offline synthetic widget QA only");
        using var job=WidgetSyncJob.CreateJob(this,true);
        if(job.IsPeriodic || job.IsPersisted)throw new Exception("Widget must be nonperiodic and nonpersistent");
        var body=new LinearLayout(this){Orientation=Orientation.Vertical};
        var title=new TextView(this){Text="NATIVE WIDGET QA — NO CREDENTIALS",TextSize=18};body.AddView(title);
        var view=MavkaHomeWidget.CreateViews(this,991).Apply(this,body) ?? throw new Exception("RemoteViews failed");
        body.AddView(view,new LinearLayout.LayoutParams(-1,(int)(260*Resources!.DisplayMetrics!.Density)));
        var check=new Button(this){Text="Перевірити завершення віджета"};body.AddView(check);
        check.Click+=(_,_)=>title.Text=WidgetSyncJob.IsRunning?"WIDGET STILL RUNNING":WidgetSyncJob.LastStatus(this);
        SetContentView(body);
    }
}
#endif
