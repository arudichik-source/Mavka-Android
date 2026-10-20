using Android.App;
using Android.App.Job;
using Android.Content;

namespace Mavka.Mobile;
internal static class RelayBackground
{
    private const int JobId = 36121;
    public static bool IsEnabled(Context context)=>context.GetSharedPreferences("mavka_relay_background_v1",FileCreationMode.Private)!.GetBoolean("enabled",false);
    public static void SetEnabled(Context context, bool enabled)
    {
        var scheduler = context.GetSystemService(Context.JobSchedulerService) as JobScheduler ??
            throw new InvalidOperationException("Android JobScheduler недоступний.");
        if (!enabled) { scheduler.Cancel(JobId); Save(context,false); return; }
        if(!new RelayIdentity(context).Enabled)throw new InvalidOperationException("Спочатку активуй пристрій після дозволу ПК.");
        var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(RelayPollingJobService)));
        var info = new JobInfo.Builder(JobId, component).SetRequiredNetworkType(NetworkType.Any)
            .SetPeriodic(15 * 60 * 1000L, 5 * 60 * 1000L).SetPersisted(true).Build();
        if (scheduler.Schedule(info) != JobScheduler.ResultSuccess)
            throw new InvalidOperationException("Android не дозволив фонову синхронізацію Relay.");
        try{Save(context,true);}catch{scheduler.Cancel(JobId);throw;}
    }
    private static void Save(Context context,bool enabled)
    {
        using var editor=context.GetSharedPreferences("mavka_relay_background_v1",FileCreationMode.Private)!.Edit()!;
        if(!editor.PutBoolean("enabled",enabled)!.Commit())throw new IOException("Не вдалося зберегти вибір фонового обміну.");
    }
}
[Service(Name = "ua.mavka.mobile.RelayPollingJobService",
    Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
internal sealed class RelayPollingJobService : JobService
{
    private CancellationTokenSource? _running;
    public override bool OnStartJob(JobParameters? parameters)
    {
        if (parameters is null || !RelayBackground.IsEnabled(this) || !new RelayIdentity(this).Enabled) return false;
        _running?.Cancel(); var cancellation = _running = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await new RelaySync(this).Synchronize(cancellation.Token); }
            catch { /* No document contents, keys or raw network bodies in Android logs. */ }
            finally { try { JobFinished(parameters, false); } catch { } }
        });
        return true;
    }
    public override bool OnStopJob(JobParameters? parameters)
    { _running?.Cancel(); _running?.Dispose(); _running = null; return false; }
}
