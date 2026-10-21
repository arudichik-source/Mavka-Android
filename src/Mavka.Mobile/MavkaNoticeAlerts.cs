using Android;
using Android.App;
using Android.Content;
using Android.OS;
using System.Security.Cryptography;
using System.Text;

namespace Mavka.Mobile;

/// <summary>
/// Local notifications and inexact offline reminders from the trusted PC or user.
/// Generic OS content avoids putting confidential messages on lock screens.
/// </summary>
internal static class MavkaNoticeAlerts
{
    private const string ChannelMessages = "mavka_main_messages_v1";
    private const string ChannelReminders = "mavka_local_reminders_v1";
    public const string ReminderAction = "ua.mavka.mobile.REMIND_NOTICE";
    public const string TestReminderAction = "ua.mavka.mobile.REMIND_LOCAL_TEST";
    private const int TestReminderCode = 36121;

    public static void EnsureChannels(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        manager?.CreateNotificationChannel(new NotificationChannel(ChannelMessages,
            "Повідомлення ГОЛОВНОЇ МАВКИ", NotificationImportance.Default)
        { Description = "Нові захищені службові повідомлення" });
        manager?.CreateNotificationChannel(new NotificationChannel(ChannelReminders,
            "Нагадування Мавки", NotificationImportance.High)
        { Description = "Нагадування від головної Мавки та створені на телефоні" });
    }

    public static bool Allowed(Context context) =>
        Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu ||
        context.CheckSelfPermission(Manifest.Permission.PostNotifications) ==
            Android.Content.PM.Permission.Granted;

    public static void ShowNew(Context context, string id)
    {
        Show(context, id, false);
    }

    // Called only after exact sender, password/AES authentication and unit/client
    // access checks. Persist the notice AND reminder before any transport ACK.
    public static bool Receive(Context context,MavkaNoticeStore store,MavkaNotice notice,string authenticatedJson)
    {
        var inserted=store.Add(notice,authenticatedJson);
        if(!store.ContainsSaved(notice.NoticeId,notice.Unit,authenticatedJson))throw new IOException("Сповіщення не збережено.");
        var saved=store.Find(notice.NoticeId,notice.Unit);
        if(saved is null)return inserted; // deleted tombstone: never resurrect
        if(saved.RemindUtc is { } at && !saved.Reminded)
        {
            if(at<=DateTimeOffset.UtcNow)
            {
                if(store.ConsumeReminder(notice.NoticeId,DateTimeOffset.UtcNow))ShowReminder(context,notice.NoticeId);
                return inserted;
            }
            SetReminder(context,notice.NoticeId,at); // stable PendingIntent replaces, never duplicates
        }
        if(inserted)ShowNew(context,notice.NoticeId);
        return inserted;
    }

    public static void ShowReminder(Context context, string id)
    {
        Show(context, id, true);
    }

    private static void Show(Context context, string id, bool reminder)
    {
        EnsureChannels(context);
        if (!Allowed(context)) return;
        var target = new Intent(context, typeof(MainActivity));
        target.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        target.PutExtra("show_mavka_notices", true);
        var open = PendingIntent.GetActivity(context, StableCode(id), target,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(context, reminder ? ChannelReminders : ChannelMessages)
            : new Notification.Builder(context);
        builder.SetSmallIcon(Resource.Drawable.mavka_app_icon)
            .SetContentTitle(reminder ? "Нагадування Мавки" :
                "Нове повідомлення від ГОЛОВНОЇ МАВКИ")
            .SetContentText("Відкрий Мавку, щоб переглянути захищений текст.")
            .SetAutoCancel(true)
            .SetContentIntent(open)
            .SetCategory(Notification.CategoryMessage);
        (context.GetSystemService(Context.NotificationService) as NotificationManager)?
            .Notify(reminder ? StableCode(id) + 1 : StableCode(id), builder.Build());
    }

    public static void SetReminder(Context context, string id,
        DateTimeOffset? whenUtc)
    {
        var alarm = context.GetSystemService(Context.AlarmService) as AlarmManager ??
            throw new InvalidOperationException("Android AlarmManager недоступний.");
        var intent = new Intent(context, typeof(MavkaNoticeReminderReceiver));
        intent.SetAction(ReminderAction);
        intent.PutExtra("notice_id", id);
        var pending = PendingIntent.GetBroadcast(context, StableCode(id), intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable) ??
            throw new InvalidOperationException("Не вдалося запланувати нагадування.");
        alarm.Cancel(pending);
        if (whenUtc is not null)
        {
            if (whenUtc <= DateTimeOffset.UtcNow)
                throw new ArgumentException("Нагадування має бути у майбутньому.");
            alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup,
                whenUtc.Value.ToUnixTimeMilliseconds(), pending);
        }
    }

    /// <summary>
    /// Independent local OS-alarm smoke test. It never creates a trusted notice,
    /// adds a record to the encrypted inbox, or sends an email.
    /// </summary>
    public static void ScheduleLocalTest(Context context)
    {
        if (!Allowed(context))
            throw new InvalidOperationException(
                "Дозволь сповіщення для Мавки в налаштуваннях Android.");
        var alarm = context.GetSystemService(Context.AlarmService) as AlarmManager ??
            throw new InvalidOperationException("Android AlarmManager недоступний.");
        var intent = new Intent(context, typeof(MavkaNoticeReminderReceiver));
        intent.SetAction(TestReminderAction);
        var pending = PendingIntent.GetBroadcast(context, TestReminderCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable) ??
            throw new InvalidOperationException("Android не створив тестове нагадування.");
        // Use the same Doze-compatible (inexact) Android alarm mode as real reminders.
        alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup,
            DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds(), pending);
    }

    public static void ShowLocalTest(Context context)
    {
        EnsureChannels(context);
        if (!Allowed(context)) return;
        var target = new Intent(context, typeof(MainActivity));
        target.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        target.PutExtra("show_mavka_notices", true);
        var open = PendingIntent.GetActivity(context, TestReminderCode, target,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(context, ChannelReminders)
            : new Notification.Builder(context);
        builder.SetSmallIcon(Resource.Drawable.mavka_app_icon)
            .SetContentTitle("Тестове нагадування Мавки")
            .SetContentText("Локальна перевірка. Службових даних немає.")
            .SetAutoCancel(true)
            .SetContentIntent(open)
            .SetCategory(Notification.CategoryReminder);
        (context.GetSystemService(Context.NotificationService) as NotificationManager)?
            .Notify(TestReminderCode, builder.Build());
    }

    public static void Remove(Context context,string id)
    {
        SetReminder(context,id,null);
        var manager=context.GetSystemService(Context.NotificationService) as NotificationManager;
        manager?.Cancel(StableCode(id));manager?.Cancel(unchecked(StableCode(id)+1));
    }

    public static void RescheduleOnStart(Context context, MavkaNoticeStore store)
    {
        foreach (var row in store.PendingReminders())
        {
            try
            {
                if (row.At > DateTimeOffset.UtcNow)
                    SetReminder(context, row.Id, row.At);
                else if (store.ConsumeReminder(row.Id, DateTimeOffset.UtcNow))
                    ShowReminder(context, row.Id);
            }
            catch { /* One invalid reminder must never prevent app startup. */ }
        }
    }

    private static int StableCode(string id)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return BitConverter.ToInt32(hash, 0) & int.MaxValue;
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
internal sealed class MavkaNoticeReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        if (intent?.Action == MavkaNoticeAlerts.TestReminderAction)
        {
            MavkaNoticeAlerts.ShowLocalTest(context);
            return;
        }
        if (intent?.Action != MavkaNoticeAlerts.ReminderAction) return;
        var id = intent.GetStringExtra("notice_id");
        if (!Guid.TryParse(id, out _)) return;
        try
        {
            using var store = new MavkaNoticeStore(context);
            if (store.ConsumeReminder(id!, DateTimeOffset.UtcNow))
                MavkaNoticeAlerts.ShowReminder(context, id!);
        }
        catch { /* No plaintext or IDs from failed notifications in logs. */ }
    }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
internal sealed class MavkaNoticeBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionBootCompleted)
            return;
        try
        {
            using var store = new MavkaNoticeStore(context);
            MavkaNoticeAlerts.RescheduleOnStart(context, store);
        }
        catch { /* Keystore may still be locked; reschedule next app launch. */ }
    }
}
