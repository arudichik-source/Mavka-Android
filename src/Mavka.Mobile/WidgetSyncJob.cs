using Android.App;
using Android.App.Job;
using Android.Content;
using Android.OS;
using System.Text.Json;

namespace Mavka.Mobile;

// Explicit widget taps only: no periodic job, boot receiver, retry loop or
// persistent service. Android may defer the single job until a network exists.
[BroadcastReceiver(Exported = false)]
internal sealed class WidgetSyncReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        try { WidgetSyncJob.Schedule(context, intent?.GetBooleanExtra("mail", false) == true); }
        catch { WidgetSyncJob.Status(context, "Не вдалося запустити. Відкрийте Мавку."); }
    }
}

[Service(Name = "ua.mavka.mobile.WidgetSyncJob", Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
internal sealed class WidgetSyncJob : JobService
{
    internal const int JobId = 36123;
    private static CancellationTokenSource? _active;
    private static Task _finished = Task.CompletedTask;
    internal static bool IsRunning => _active is not null;
    private bool _stopped;
    internal static void Status(Context context, string text)
    {
        using var editor = context.GetSharedPreferences("mavka_widget_sync_v1", FileCreationMode.Private)!.Edit()!;
        editor.PutString("status", text)!.PutLong("at", DateTimeOffset.UtcNow.ToUnixTimeSeconds())!.Apply();
        MavkaHomeWidget.Refresh(context);
    }
    internal static string LastStatus(Context context)
    {
        var prefs = context.GetSharedPreferences("mavka_widget_sync_v1", FileCreationMode.Private)!;
        var text = prefs.GetString("status", "Оновлення лише за натисканням")!;
        var at = prefs.GetLong("at", 0);
        if (at > 0 && !IsRunning && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - at > 360 && text.StartsWith("Оновлення…"))
            return "Оновлення перервано. Натисніть ще раз.";
        return text;
    }
    internal static void Schedule(Context context, bool mail)
    {
        var scheduler = (JobScheduler?)context.GetSystemService(JobSchedulerService) ?? throw new IOException("JobScheduler unavailable");
        if (IsRunning || scheduler.GetPendingJob(JobId) is not null) return;
        var job = CreateJob(context, mail);
        if (scheduler.Schedule(job) != JobScheduler.ResultSuccess) throw new IOException("Job rejected");
        Status(context, "Оновлення… очікування мережі / дозволу Android");
    }
    internal static JobInfo CreateJob(Context context,bool mail)
    {
        var extras = new PersistableBundle(); extras.PutBoolean("mail", mail);
        return new JobInfo.Builder(JobId, new ComponentName(context, Java.Lang.Class.FromType(typeof(WidgetSyncJob))))
            .SetRequiredNetworkType(NetworkType.Any)!.SetExtras(extras)!.Build() ?? throw new IOException("Job unavailable");
    }
    internal static async Task CancelAndWait(Context context)
    {
        try{_active?.Cancel();}catch(ObjectDisposedException){}
        (context.GetSystemService(JobSchedulerService) as JobScheduler)?.Cancel(JobId);
        await _finished;
    }
    public override bool OnStartJob(JobParameters? parameters)
    {
        if (parameters is null || IsRunning) return false;
        if (MainActivity.HasActiveExchange) { Status(this, "Обмін у Мавці вже виконується."); return false; }
        _stopped = false;
        var cancellation = _active = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        _finished = Task.Run(async () =>
        {
            try
            {
                LocalAccountReset.EnsureReady(this);
                var mailOnly = parameters.Extras?.GetBoolean("mail", false) == true;
                var settings = GetSharedPreferences("mavka_foreground_sync_v1", FileCreationMode.Private)!;
                var channel = mailOnly ? 0 : Math.Clamp(settings.GetInt("channel", 0), 0, 2);
                Status(this, "Оновлення… отримання захищених змін");
                if (channel is 0 or 2) await ReceiveMail(cancellation.Token);
                if (channel is 1 or 2) await new RelaySync(this).SynchronizeNow(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                Status(this, "Завершено о " + DateTime.Now.ToString("HH:mm") + ". Дані збережено.");
            }
            catch (System.OperationCanceledException) { Status(this, "Зупинено. Отримані частини збережено; можна продовжити."); }
            catch { Status(this, "Не завершено. Перевірте підключення у Мавці."); }
            finally
            {
                _active = null; cancellation.Dispose();
                if (!_stopped) new Handler(Looper.MainLooper!).Post(() => JobFinished(parameters, false));
            }
        });
        return true;
    }
    public override bool OnStopJob(JobParameters? parameters)
    {
        _stopped = true; try{_active?.Cancel();}catch(ObjectDisposedException){} return false; // Never auto-reschedule.
    }
    private async Task ReceiveMail(CancellationToken token)
    {
        var credentials = new GmailCredentialsStore(this);
        var sender = credentials.GetMainMavkaEmail();
        var profile = new UserProfileStore(this).Load();
        if (!credentials.HasCredentials || profile is null || string.IsNullOrWhiteSpace(sender)) throw new InvalidOperationException("Configure Gmail first");
        using var archive = new DocumentArchiveStore(this);
        var access = new MobileAccessStore(this);
        var scope = credentials.GetEmail().Trim().ToLowerInvariant() + "|" + sender.Trim().ToLowerInvariant();
        var session = new GmailRefreshSession(); var retained = 0;
        await RelaySync.Gate.WaitAsync(token);
        try
        {
            while (!session.AtLimit)
            {
                var messages = await GmailConnectionService.ReadFromSenderAsync(credentials.GetEmail(), credentials.GetPassword()!, sender,
                    new ExchangeKeyStore(this), new DocumentPasswordStore(this), profile.ClientId,
                    validity => archive.ListMailProcessingStates(validity, scope), cancellation: token, attempted: session.Attempted);
                if (!session.Register(messages.Select(m => (m.UidValidity, m.MessageUid)))) break;
                foreach (var mail in messages)
                {
                    token.ThrowIfCancellationRequested();
                    if (mail.PreviouslyArchived) continue;
                    var complete = mail.AttachmentsComplete && mail.Attachments.Count > 0;
                    foreach (var attachment in mail.Attachments)
                    {
                        token.ThrowIfCancellationRequested();
                        if (attachment.InstalledKeyPackage) continue;
                        if (!attachment.AuthenticatedJson || attachment.RawJson is not {} raw) { complete = false; continue; }
                        using var mirror = new MobileChangeStore(this);
                        if (mirror.TryAccept(raw, token)) continue;
                        using var json = JsonDocument.Parse(raw);
                        if (json.RootElement.TryGetProperty("format", out var format) && format.GetString() == "MAVKA_ACCESS_UPDATE_V1")
                        {
                            if (json.RootElement.GetProperty("recipientClientId").GetString() != profile.ClientId) throw new InvalidDataException("Wrong client");
                            access.Accept(json.RootElement.GetProperty("access")); continue;
                        }
                        if (access.TryNotice(raw, out var notice) && notice is not null)
                        { using var notices = new MavkaNoticeStore(this); MavkaNoticeAlerts.Receive(this, notices, notice, raw); continue; }
                        if (!access.TryDocument(raw, out var doc) || doc is null) { complete = false; continue; }
                        archive.SaveDocument(doc.DocumentId, doc.Kind, doc.Number, doc.Unit, doc.DocumentDate, attachment.FileName, doc.Json);
                        if (archive.FindDocument(doc.DocumentId) is null) throw new IOException("Archive readback failed");
                        access.AutoPost(doc.Json, cancellation: token);
                    }
                    token.ThrowIfCancellationRequested();
                    // Unsupported PDF/Excel/file transfers stay in Gmail for the
                    // existing foreground viewer. No remote deletion in widget.
                    if (complete) archive.MarkMailReady(mail.UidValidity, mail.MessageUid, scope); else retained++;
                }
                Status(this, "Оновлення… перевірено листів: " + session.Messages);
            }
            var posting = IncomingBatchPosting.Apply(this, cancellation: token);
            if (posting.Issues.Count > 0 || retained > 0) throw new InvalidOperationException("Review retained attachments in app");
        }
        finally { RelaySync.Gate.Release(); }
    }
}
