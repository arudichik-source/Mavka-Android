using Android.App;
using Android.OS;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace Mavka.Mobile;

[Activity(
    Label = "Мавка Mobile",
    MainLauncher = true,
    Exported = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        root.SetPadding(32, 32, 32, 24);
        root.SetBackgroundColor(Color.Rgb(250, 247, 247));

        // Заголовок
        var title = new TextView(this)
        {
            Text = "Мавка",
            TextSize = 30,
            Gravity = GravityFlags.Left
        };

        title.SetTextColor(Color.Rgb(35, 35, 35));
        title.SetPadding(8, 12, 8, 24);

        // Картка синхронізації
        var syncCard = CreateCard();

        var syncTitle = new TextView(this)
        {
            Text = "Синхронізація",
            TextSize = 20
        };

        var syncStatus = new TextView(this)
        {
            Text = "Ще не налаштовано",
            TextSize = 16
        };

        syncStatus.SetPadding(0, 8, 0, 16);

        var refreshButton = new Button(this)
        {
            Text = "Оновити"
        };

        refreshButton.Click += (_, _) =>
        {
            syncStatus.Text = "Перевірка пошти...";
        };

        syncCard.AddView(syncTitle);
        syncCard.AddView(syncStatus);
        syncCard.AddView(refreshButton);

        // Картка документів
        var documentsCard = CreateCard();

        var documentsTitle = new TextView(this)
        {
            Text = "Нові документи",
            TextSize = 20
        };

        var documentsCount = new TextView(this)
        {
            Text = "0",
            TextSize = 34
        };

        documentsCount.SetPadding(0, 8, 0, 4);

        var documentsHint = new TextView(this)
        {
            Text = "Нових накладних поки немає",
            TextSize = 15
        };

        documentsCard.AddView(documentsTitle);
        documentsCard.AddView(documentsCount);
        documentsCard.AddView(documentsHint);

        // Останні документи
        var recentTitle = new TextView(this)
        {
            Text = "Останні документи",
            TextSize = 20
        };

        recentTitle.SetPadding(8, 28, 8, 12);

        var emptyState = new TextView(this)
        {
            Text = "Документи ще не отримані",
            TextSize = 16
        };

        emptyState.SetPadding(8, 8, 8, 24);

        // Простір між контентом і нижнім меню
        var spacer = new Space(this);

        spacer.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1
        );

        // Нижня навігація
       var bottomNav = new LinearLayout(this)
{
    Orientation = Orientation.Horizontal
};

bottomNav.SetGravity(GravityFlags.Center);

        var homeButton = CreateNavButton("Головна");
        var inboxButton = CreateNavButton("Вхідні");
        var settingsButton = CreateNavButton("Налаштування");

        bottomNav.AddView(homeButton);
        bottomNav.AddView(inboxButton);
        bottomNav.AddView(settingsButton);

        root.AddView(title);
        root.AddView(syncCard);
        root.AddView(documentsCard);
        root.AddView(recentTitle);
        root.AddView(emptyState);
        root.AddView(spacer);
        root.AddView(bottomNav);

        SetContentView(root);
    }

    private LinearLayout CreateCard()
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        card.SetPadding(24, 20, 24, 20);
        card.SetBackgroundColor(Color.White);

        var parameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent
        );

        parameters.SetMargins(0, 0, 0, 18);
        card.LayoutParameters = parameters;

        return card;
    }

    private Button CreateNavButton(string text)
    {
        var button = new Button(this)
        {
            Text = text
        };

        button.LayoutParameters = new LinearLayout.LayoutParams(
            0,
            ViewGroup.LayoutParams.WrapContent,
            1
        );

        return button;
    }
}
