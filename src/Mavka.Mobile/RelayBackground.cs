using Android.App;
using Android.App.Job;
using Android.Content;
namespace Mavka.Mobile;
internal static class RelayBackground
{
    private const int JobId=36121;
    public static bool IsEnabled(Context context)=>false;
    public static void SetEnabled(Context context,bool enabled)
    {
        (context.GetSystemService(Context.JobSchedulerService) as JobScheduler)?.Cancel(JobId);
        using var editor=context.GetSharedPreferences("mavka_relay_background_v1",FileCreationMode.Private)!.Edit()!;
        if(!editor.PutBoolean("enabled",false)!.Commit())throw new IOException("Не вдалося зберегти вимкнення фонового обміну.");
        if(enabled)throw new InvalidOperationException("Автооновлення вмикається на головному екрані й працює лише у відкритій Мавці.");
    }
}
[Service(Name="ua.mavka.mobile.RelayPollingJobService",Permission="android.permission.BIND_JOB_SERVICE",Exported=true)]
internal sealed class RelayPollingJobService:JobService
{
    // Existing jobs from an older APK must never restart network polling.
    public override bool OnStartJob(JobParameters? parameters)=>false;
    public override bool OnStopJob(JobParameters? parameters)=>false;
}