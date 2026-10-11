using Android.App;
using Android.OS;
using Android.Widget;

namespace Mavka.Mobile;

[Activity(
    Label = "Mavka Mobile",
    MainLauncher = true,
    Exported = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var layout = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        layout.SetPadding(40, 80, 40, 40);

        var title = new TextView(this)
        {
            Text = "Мавка Mobile",
            TextSize = 28
        };

        var status = new TextView(this)
        {
            Text = "Статус синхронізації: ще не налаштовано",
            TextSize = 18
        };

        var refreshButton = new Button(this)
        {
            Text = "Оновити"
        };

        refreshButton.Click += (_, _) =>
        {
            status.Text = "Перевірка синхронізації...";
        };

        layout.AddView(title);
        layout.AddView(status);
        layout.AddView(refreshButton);

        SetContentView(layout);
    }
}
