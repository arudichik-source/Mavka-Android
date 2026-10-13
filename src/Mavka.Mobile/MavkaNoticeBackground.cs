using Android.App;
using Android.App.Job;
using Android.Content;
using Android.OS;

namespace Mavka.Mobile;

/// <summary>
/// Optional OS-scheduled check, approximately every 15 minutes (Android may
/// defer it). Only the already configured Gmail sender + notice subject are
/// searched. It never handles inventory documents or schedules reminders.
/// </summary>
internal static class MavkaNoticeBackground
{
    private const int JobId = 36120;
    private const string Preferences = "mavka_notice_preferences";
    private const string KeyEnabled = "background_notice_check";
    private const long PeriodMs = 15 * 60 * 1000;

    public static bool Enabled(Context context) =>
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)?
            .GetBoolean(KeyEnabled, false) == true;

    public static void SetEnabled(Context context, bool enable)
    {
        var scheduler = context.GetSystemService(Context.JobSchedulerService)
            as JobScheduler ??
            throw new InvalidOperationException("Android JobScheduler недоступний.");
        if (enable)
        {
            var component = new ComponentName(context,
                Java.Lang.Class.FromType(typeof(MavkaNoticePollingJobService)));
            var info = new JobInfo.Builder(JobId, component)
                .SetRequiredNetworkType(NetworkType.Any)
                .SetPeriodic(PeriodMs, 5 * 60 * 1000)
                .SetPersisted(true)
                .Build();
            if (scheduler.Schedule(info) != JobScheduler.ResultSuccess)
                throw new InvalidOperationException("Android не дозволив фонову перевірку.");
        }
        else scheduler.Cancel(JobId);
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)!
            .Edit()!.PutBoolean(KeyEnabled, enable)!.Apply();
    }
}

[Service(Name = "ua.mavka.mobile.NoticePollingJobService",
    Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
[IntentFilter(new[] { "android.app.job.JobService" })]
internal sealed class MavkaNoticePollingJobService : JobService
{
    private CancellationTokenSource? _running;

    public override bool OnStartJob(JobParameters? parameters)
    {
        if (parameters is null || !MavkaNoticeBackground.Enabled(this)) return false;
        _running?.Cancel();
        var cancellation = _running = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await CheckNoticesAsync(cancellation.Token); }
            catch { /* Background mail must not log confidential details. */ }
            finally
            {
                try { JobFinished(parameters, false); }
                catch { /* Android may have stopped the job already. */ }
            }
        });
        return true;
    }

    public override bool OnStopJob(JobParameters? parameters)
    {
        _running?.Cancel();
        _running?.Dispose();
        _running = null;
        return false; // OS controls next normal periodic run.
    }

    private async Task CheckNoticesAsync(CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested) return;
        var creds = new GmailCredentialsStore(this);
        if (!creds.HasCredentials) return;
        var sender = creds.GetMainMavkaEmail();
        var account = creds.GetEmail();
        var password = creds.GetPassword();
        var profile = new UserProfileStore(this).Load();
        if (profile is null || string.IsNullOrWhiteSpace(sender) ||
            string.IsNullOrWhiteSpace(password)) return;
        using var archive = new DocumentArchiveStore(this);
        using var notices = new MavkaNoticeStore(this);
        var exchange = new ExchangeKeyStore(this);
        var docPassword = new DocumentPasswordStore(this);
        var scope = account.Trim().ToLowerInvariant() + "|" +
            sender.Trim().ToLowerInvariant();
        var results = await GmailConnectionService.ReadFromSenderAsync(
            account, password, sender, exchange, docPassword, profile.ClientId,
            validity => archive.ListMailProcessingStates(validity, scope),
            noticesOnly: true);
        foreach (var mail in results)
        {
            cancellation.ThrowIfCancellationRequested();
            if (mail.PreviouslyArchived || !mail.AttachmentsComplete ||
                mail.Attachments.Count == 0) continue;
            bool allNotices = true;
            foreach (var attachment in mail.Attachments)
            {
                if (!attachment.AuthenticatedJson ||
                    !MavkaNoticeProtocol.TryRead(attachment.RawJson, profile.Unit,
                        DateTimeOffset.UtcNow, out var notice) ||
                    notice is null)
                {
                    allNotices = false;
                    break;
                }
                bool added = notices.Add(notice, attachment.RawJson!);
                if (notices.Find(notice.NoticeId, profile.Unit) is null)
                    throw new InvalidOperationException("Notice readback failed.");
                if (added)
                    MavkaNoticeAlerts.ShowNew(this, notice.NoticeId);
            }
            if (!allNotices) continue; // Never trash a mail with other content.
            archive.MarkMailReady(mail.UidValidity, mail.MessageUid, scope);
            try
            {
                await GmailConnectionService.MoveToTrashAsync(
                    account, password, sender, mail);
                archive.MarkMailTrashed(mail.UidValidity, mail.MessageUid, scope);
            }
            catch
            {
                // Durable ready marker lets the foreground refresh retry
                // remote Trash without touching any attachment again.
            }
        }
    }
}
