using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace Mavka.Mobile;

[BroadcastReceiver(Label = "Мавка — швидкий доступ", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData("android.appwidget.provider", Resource = "@xml/mavka_home_widget")]
public sealed class MavkaHomeWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? ids)
    {
        base.OnUpdate(context, manager, ids);
        if (context is null || manager is null || ids is null) return;
        foreach (var id in ids)
        {
            var views = new RemoteViews(context.PackageName, Resource.Layout.mavka_home_widget);
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
