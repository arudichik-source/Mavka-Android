using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace Mavka.Mobile;

/// <summary>
/// Native 4x2 launcher widget. Only generic counters are displayed; decrypted
/// notice titles, message bodies, names and inventory never leave the app UI.
/// </summary>
[BroadcastReceiver(Label = "Мавка — швидкий доступ", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData("android.appwidget.provider", Resource = "@xml/mavka_home_widget")]
public sealed class MavkaHomeWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? ids)
    {
        base.OnUpdate(context, manager, ids);
        if (context is null || manager is null || ids is null) return;
        UpdateWidgets(context, manager, ids);
    }

    public static void Refresh(Context context)
    {
        try
        {
            var manager = AppWidgetManager.GetInstance(context);
            var component = new ComponentName(context,
                Java.Lang.Class.FromType(typeof(MavkaHomeWidget)));
            var ids = manager?.GetAppWidgetIds(component);
            if (manager is null || ids is null || ids.Length == 0) return;
            UpdateWidgets(context, manager, ids);
        }
        catch
        {
            // Launcher widgets are optional; they may not interrupt
            // durable storage, mail import, or background synchronization.
        }
    }

    private static void UpdateWidgets(Context context, AppWidgetManager manager, int[] ids)
    {
        int unread = 0, reminders = 0;
        try
        {
            var profile = new UserProfileStore(context).Load();
            if (profile is not null)
            {
                using var store = new MavkaNoticeStore(context);
                unread = store.UnreadCount(profile.Unit);
                reminders = store.PendingReminderCount(profile.Unit);
            }
        }
        catch
        {
            // Locked Keystore, missing profile or database migration: neutral
            // labels instead of confidential content or an app crash.
        }

        foreach (var id in ids)
        {
            var views = new RemoteViews(context.PackageName, Resource.Layout.mavka_home_widget);
            views.SetTextViewText(Resource.Id.widget_header,
                reminders > 0 ? "МАВКА  ·  Нагадувань: " + reminders
                              : "МАВКА  ·  ШВИДКИЙ ДОСТУП");
            views.SetTextViewText(Resource.Id.widget_notices,
                unread > 0 ? "♢\n" + unread + " нових" : "♢\nСповіщення");
            views.SetOnClickPendingIntent(Resource.Id.widget_library, Open(context, "library", id * 10 + 1));
            views.SetOnClickPendingIntent(Resource.Id.widget_notices, Open(context, "notices", id * 10 + 2));
            views.SetOnClickPendingIntent(Resource.Id.widget_assets, Open(context, "assets", id * 10 + 3));
            views.SetOnClickPendingIntent(Resource.Id.widget_inbox, Open(context, "inbox", id * 10 + 4));
            manager.UpdateAppWidget(id, views);
        }
    }

    private static PendingIntent Open(Context context, string destination, int requestCode)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetAction("ua.mavka.mobile.WIDGET_" + destination);
        intent.PutExtra("mavka_widget_destination", destination);
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(context, requestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }
}
