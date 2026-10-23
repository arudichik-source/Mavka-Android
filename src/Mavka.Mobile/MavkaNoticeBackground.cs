using Android.App;
using Android.App.Job;
using Android.Content;

namespace Mavka.Mobile;

// Upgrade bridge only. A historical periodic job must never restart mail I/O.
// Foreground timers and explicit one-shot widget requests own mail refresh.
internal static class MavkaNoticeBackground
{
    internal const int JobId=36120;
    public static bool Enabled(Context context)=>false;
    public static void SetEnabled(Context context,bool enable)
    {
        (context.GetSystemService(Context.JobSchedulerService) as JobScheduler)?.Cancel(JobId);
        using var editor=context.GetSharedPreferences("mavka_notice_preferences",FileCreationMode.Private)!.Edit()!;
        if(!editor.PutBoolean("background_notice_check",false)!.Commit())throw new IOException("Не вдалося вимкнути стару фонову перевірку.");
        if(enable)throw new InvalidOperationException("Налаштуйте оновлення на головному екрані або скористайтеся віджетом.");
    }
}
[Service(Name="ua.mavka.mobile.NoticePollingJobService",Permission="android.permission.BIND_JOB_SERVICE",Exported=true)]
[IntentFilter(new[]{"android.app.job.JobService"})]
internal sealed class MavkaNoticePollingJobService:JobService
{
    public override bool OnStartJob(JobParameters? parameters)=>false;
    public override bool OnStopJob(JobParameters? parameters)=>false;
}
