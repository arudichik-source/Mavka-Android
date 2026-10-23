using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace Mavka.Mobile;

/// <summary>
/// Native resizable launcher widget. Only generic counters are displayed; decrypted
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

        // Existing navigation plus two explicit one-shot exchange actions.
        // Re-read theme on refresh so the home widget follows the app palette.
        foreach(var id in ids) manager.UpdateAppWidget(id,CreateViews(context,id,unread,reminders));
    }
    internal static RemoteViews CreateViews(Context context,int id,int unread=0,int reminders=0,bool? blueTheme=null)
    {
        var blue=blueTheme??MavkaThemePalette.Load(context).IsBlue;
        var views = new RemoteViews(context.PackageName,
                blue ? Resource.Layout.mavka_home_widget_blue
                     : Resource.Layout.mavka_home_widget);
        views.SetTextViewText(Resource.Id.widget_status,
                $"Нові: {unread} · Нагад.: {reminders}");
        views.SetOnClickPendingIntent(Resource.Id.widget_library, Open(context, "library", id * 10 + 1));
        views.SetOnClickPendingIntent(Resource.Id.widget_notices, Open(context, "notices", id * 10 + 2));
        views.SetOnClickPendingIntent(Resource.Id.widget_assets, Open(context, "assets", id * 10 + 3));
        views.SetOnClickPendingIntent(Resource.Id.widget_inbox, Open(context, "inbox", id * 10 + 4));
        views.SetTextViewText(Resource.Id.widget_sync_status,WidgetSyncJob.LastStatus(context));
        views.SetOnClickPendingIntent(Resource.Id.widget_refresh_base,Sync(context,false,id*10+5));
        views.SetOnClickPendingIntent(Resource.Id.widget_check_mail,Sync(context,true,id*10+6));
        return views;
    }

    private static PendingIntent Sync(Context context,bool mail,int requestCode)
    {
        var intent=new Intent(context,typeof(WidgetSyncReceiver));intent.PutExtra("mail",mail);
        intent.SetAction(mail?"ua.mavka.mobile.WIDGET_CHECK_MAIL":"ua.mavka.mobile.WIDGET_UPDATE_BASE");
        return PendingIntent.GetBroadcast(context,requestCode,intent,PendingIntentFlags.UpdateCurrent|PendingIntentFlags.Immutable)!;
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
