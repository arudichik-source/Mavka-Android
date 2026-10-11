using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace Mavka.Mobile;

[Activity(Label = "Мавка Mobile", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    private LinearLayout _body = null!;
    private GmailCredentialsStore _store = null!;
    private Button _home = null!, _inbox = null!, _settings = null!;
    private string _screen = "home";
    private bool _busy;

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        _store = new GmailCredentialsStore(this);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.Rgb(248, 247, 245));
        root.SetOnApplyWindowInsetsListener(new SafeInsets(Dp(14), Dp(10), Dp(10)));
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.LayoutParameters = new LinearLayout.LayoutParams(-1, 0, 1);
        _body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _body.SetPadding(0, Dp(8), 0, Dp(12));
        scroll.AddView(_body);
        var nav = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _home = Nav("Головна", ShowHome);
        _inbox = Nav("Вхідні", ShowInbox);
        _settings = Nav("Налашт.", ShowSettings);
        nav.AddView(_home);
        nav.AddView(_inbox);
        nav.AddView(_settings);
        root.AddView(scroll);
        root.AddView(nav);
        SetContentView(root);
        ShowHome();
    }

    private void Begin(string screen, string title)
    {
        _screen = screen;
        Title = "Мавка Mobile — " + title;
        _body.RemoveAllViews();
        _home.Enabled = screen != "home";
        _inbox.Enabled = screen != "inbox";
        _settings.Enabled = screen != "settings" && screen != "mail";
        Text(_body, title, 25, true);
    }

    private void ShowHome()
    {
        Begin("home", "Головна");
        var card = Card();
        Text(card, "Синхронізація", 20, true);
        var status = Text(card, _store.HasCredentials ? "Gmail налаштовано" : "Gmail не підключено", 16);
        Button(card, "Оновити", async () =>
        {
            if (!_store.HasCredentials)
            {
                status.Text = "Підключи пошту в Налаштування → Пошта Gmail.";
                return;
            }
            if (_busy) return;
            _busy = true;
            status.Text = "Перевірка Gmail…";
            try
            {
                await GmailConnectionService.TestImapAsync(_store.GetEmail(), _store.GetPassword()!);
                status.Text = "Gmail доступний. Імпорт документів ще не реалізовано.";
            }
            catch (MailKit.Security.AuthenticationException) { status.Text = "Gmail відхилив вхід. Перевір налаштування."; }
            catch (System.OperationCanceledException) { status.Text = "Час з’єднання минув."; }
            catch (Exception) { status.Text = "Не вдалося зв’язатися з Gmail."; }
            finally { _busy = false; }
        });
        card = Card();
        Text(card, "Нові документи", 20, true);
        Text(card, "Документи ще не синхронізовано", 16);
        Text(_body, "Останні документи", 20, true);
        Text(_body, "Після налаштування формату обміну документи будуть тут.", 15);
    }

    private void ShowInbox()
    {
        Begin("inbox", "Вхідні");
        var card = Card();
        Text(card, "Вхідні документи", 20, true);
        Text(card, "Отримання службових листів Мавки додамо наступним етапом.", 16);
        Text(card, "Особиста пошта автоматично не завантажується.", 14);
    }

    private void ShowSettings()
    {
        Begin("settings", "Налаштування");
        var card = Card();
        Text(card, "Пошта Gmail", 20, true);
        Text(card, _store.HasCredentials ? _store.GetEmail() : "Не підключено", 16);
        Button(card, "Пошта Gmail →", ShowMail);
        card = Card();
        Text(card, "Профіль Мавки", 20, true);
        Text(card, "Ім’я користувача та ID Мавки додамо пізніше.", 15);
    }

    private void ShowMail()
    {
        Begin("mail", "Пошта Gmail");
        var card = Card();
        Text(card, "Адреса Gmail", 15, true);
        var email = new EditText(this)
        {
            Hint = "example@gmail.com",
            Text = _store.GetEmail(),
            InputType = InputTypes.ClassText | InputTypes.TextVariationEmailAddress
        };
        email.SetSingleLine(true);
        card.AddView(email);
        Text(card, "Пароль застосунку Google", 15, true);
        var secret = new EditText(this)
        {
            Hint = _store.HasCredentials ? "Збережений пароль (можна не вводити)" : "16 символів",
            InputType = InputTypes.ClassText | InputTypes.TextVariationPassword
        };
        secret.SetSingleLine(true);
        secret.TransformationMethod =
    Android.Text.Method.PasswordTransformationMethod.Instance;
        card.AddView(secret);
        Text(card, "Вводь лише пароль застосунку, а не основний пароль Google.", 13);
        var status = Text(card, "Підключення ще не перевірялося.", 14);
        Text(card, "Адреса ГОЛОВНОЇ МАВКИ", 15, true);

var mainMavkaEmail = new EditText(this)
{
    Hint = "Адреса службової пошти",
    Text = _store.GetMainMavkaEmail(),
    InputType = InputTypes.ClassText |
                InputTypes.TextVariationEmailAddress
};

mainMavkaEmail.SetSingleLine(true);
card.AddView(mainMavkaEmail);

Button(card, "Зберегти адресу ГОЛОВНОЇ МАВКИ", () =>
{
    var address = (mainMavkaEmail.Text ?? "").Trim();

    if (!System.Net.Mail.MailAddress.TryCreate(
            address, out var parsed) ||
        !string.Equals(
            parsed?.Address,
            address,
            StringComparison.OrdinalIgnoreCase))
    {
        status.Text = "Введи правильну електронну адресу.";
        return;
    }

    try
    {
        _store.SaveMainMavkaEmail(address);
        status.Text = "Адресу ГОЛОВНОЇ МАВКИ збережено.";
    }
    catch
    {
        status.Text = "Не вдалося зберегти адресу.";
    }
});
        var save = new Button(this) { Text = "Перевірити й зберегти" };
        card.AddView(save);
        save.Click += async (_, _) =>
        {
            if (_busy) return;
            var address = (email.Text ?? "").Trim();
            var password = (secret.Text ?? "").Replace(" ", "").Trim();
            if (!address.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) || address.Contains(' '))
            {
                status.Text = "Введи адресу Gmail.";
                return;
            }
            if (password.Length == 0 && _store.HasCredentials &&
                string.Equals(address, _store.GetEmail(), StringComparison.OrdinalIgnoreCase))
            {
                try { password = _store.GetPassword() ?? ""; }
                catch { status.Text = "Не вдалося прочитати збережений пароль. Введи його знову."; return; }
            }
            if (password.Length == 0) { status.Text = "Введи пароль застосунку Google."; return; }
            _busy = true;
            save.Enabled = false;
            status.Text = "Перевіряємо з’єднання з Gmail…";
            try
         string stage = "1/5: Підключення до Gmail";

try
{
    await GmailConnectionService.TestImapAsync(
        address,
        password,
        message =>
        {
            stage = message;
            status.Text = message;
        });

    stage = "5/5: Захищене збереження Android";
    status.Text = stage;

    _store.Save(address, password);

    secret.Text = "";
    secret.Hint = "Пароль збережено";

    status.Text = "Gmail успішно підключено!";
}
catch (MailKit.Security.AuthenticationException)
{
    status.Text =
        "Помилка авторизації Gmail. Перевір пароль застосунку.";
}
catch (System.OperationCanceledException)
{
    status.Text =
        $"Перевищено час очікування. Етап: {stage}";
}
catch (Exception ex)
{
    status.Text =
        $"Помилка на етапі {stage}: {ex.GetType().Name}";
}
finally
{
    _busy = false;
    save.Enabled = true;
}
        };
        Button(card, "Відключити Gmail", () =>
        {
            try
            {
                _store.Clear();
                email.Text = "";
                secret.Text = "";
                secret.Hint = "16 символів";
                status.Text = "Пошта відключена від цього телефона.";
            }
            catch { status.Text = "Не вдалося очистити налаштування."; }
        });
        Button(_body, "← До налаштувань", ShowSettings);
    }

    public override void OnBackPressed()
    {
        if (_screen == "mail") { ShowSettings(); return; }
        if (_screen != "home") { ShowHome(); return; }
        base.OnBackPressed();
    }

    private LinearLayout Card()
    {
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.SetPadding(Dp(16), Dp(14), Dp(16), Dp(14));
        card.SetBackgroundColor(Color.White);
        var lp = new LinearLayout.LayoutParams(-1, -2);
        lp.SetMargins(0, Dp(7), 0, Dp(10));
        _body.AddView(card, lp);
        return card;
    }

    private TextView Text(LinearLayout parent, string caption, float size, bool bold = false)
    {
        var view = new TextView(this) { Text = caption, TextSize = size };
        view.SetTextColor(bold ? Color.Rgb(34, 34, 36) : Color.Rgb(90, 86, 87));
        if (bold) view.SetTypeface(null, TypefaceStyle.Bold);
        view.SetPadding(0, Dp(5), 0, Dp(10));
        parent.AddView(view);
        return view;
    }

    private void Button(LinearLayout parent, string text, Action callback)
    {
        var view = new Button(this) { Text = text };
        view.Click += (_, _) => callback();
        parent.AddView(view);
    }

    private Button Nav(string title, Action action)
    {
        var view = new Button(this) { Text = title, TextSize = 12 };
        view.SetSingleLine(true);
        view.SetPadding(0, 0, 0, 0);
        view.LayoutParameters = new LinearLayout.LayoutParams(0, Dp(52), 1f);
        view.Click += (_, _) => action();
        return view;
    }

    private int Dp(float value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);

    public sealed class SafeInsets : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        private readonly int _h, _top, _bottom;
        public SafeInsets(int h, int top, int bottom) { _h = h; _top = top; _bottom = bottom; }
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars());
                view.SetPadding(_h + bars.Left, _top + bars.Top, _h + bars.Right, _bottom + bars.Bottom);
            }
            else
            {
                view.SetPadding(_h + insets.SystemWindowInsetLeft, _top + insets.SystemWindowInsetTop,
                    _h + insets.SystemWindowInsetRight, _bottom + insets.SystemWindowInsetBottom);
            }
            return insets;
        }
    }
}
