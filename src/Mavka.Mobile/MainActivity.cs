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
    private UserProfileStore _profileStore = null!;
    private FrameLayout _drawerOverlay = null!;
    private Button _home = null!, _inbox = null!, _settings = null!;
    private string _screen = "home";
    private bool _busy;
    private IReadOnlyList<MavkaMailItem> _messages = Array.Empty<MavkaMailItem>();

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        _store = new GmailCredentialsStore(this);
        _profileStore = new UserProfileStore(this);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.Rgb(248, 247, 245));
        root.SetOnApplyWindowInsetsListener(new SafeInsets(Dp(14), Dp(10), Dp(10)));

        var topBar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        topBar.SetGravity(GravityFlags.CenterVertical);
        var brand = new TextView(this) { Text = "Мавка Mobile", TextSize = 21 };
        brand.SetTextColor(Color.Rgb(31, 36, 36));
        brand.SetTypeface(null, TypefaceStyle.Bold);
        topBar.AddView(brand, new LinearLayout.LayoutParams(0, Dp(52), 1));
        brand.Gravity = GravityFlags.CenterVertical;
        var hamburger = new Android.Widget.Button(this) { Text = "☰", TextSize = 23 };
        hamburger.SetSingleLine(true);
        hamburger.SetPadding(0, 0, 0, 0);
        topBar.AddView(hamburger, new LinearLayout.LayoutParams(Dp(58), Dp(52)));
        hamburger.Click += (_, _) => ShowDrawer();
        root.AddView(topBar);

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

        var host = new FrameLayout(this);
        host.AddView(root, new FrameLayout.LayoutParams(-1, -1));

        _drawerOverlay = new FrameLayout(this);
        _drawerOverlay.SetBackgroundColor(Color.Argb(115, 0, 0, 0));
        _drawerOverlay.Visibility = ViewStates.Gone;
        _drawerOverlay.Click += (_, _) => CloseDrawer();

        var sidePanel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        sidePanel.Clickable = true;
        sidePanel.SetBackgroundColor(Color.White);
        sidePanel.SetPadding(Dp(14), Dp(50), Dp(14), Dp(30));
        Text(sidePanel, "Мавка Mobile", 22, true);
        Text(sidePanel, "Меню", 15);
        Button(sidePanel, "Головна", () => { CloseDrawer(); ShowHome(); });
        Button(sidePanel, "Вхідні", () => { CloseDrawer(); ShowInbox(); });
        Button(sidePanel, "Профіль користувача", () => { CloseDrawer(); ShowProfile(); });
        Button(sidePanel, "Налаштування", () => { CloseDrawer(); ShowSettings(); });
        Button(sidePanel, "Закрити меню", CloseDrawer);

        _drawerOverlay.AddView(sidePanel,
            new FrameLayout.LayoutParams(Dp(292), -1, GravityFlags.Right));
        host.AddView(_drawerOverlay, new FrameLayout.LayoutParams(-1, -1));

        SetContentView(host);
        ShowHome();
    }

    private void Begin(string screen, string title)
    {
        _screen = screen;
        Title = "Мавка Mobile — " + title;
        _body.RemoveAllViews();
        _home.Enabled = screen != "home";
        _inbox.Enabled = screen != "inbox" && screen != "message";
        _settings.Enabled = screen != "settings" && screen != "mail";
        Text(_body, title, 25, true);
    }

    private void ShowHome()
    {
        Begin("home", "Головна");
        var card = Card();
        Text(card, "Синхронізація", 20, true);
        var status = Text(card,
            _store.HasCredentials ? "Gmail налаштовано" : "Gmail не підключено", 16);
        Button(card, "Оновити", async () => await RefreshMessagesAsync(status));
        card = Card();
        Text(card, "Службові повідомлення", 20, true);
        Text(card, $"Завантажено: {_messages.Count}", 18);
        Button(card, "Перейти до вхідних", ShowInbox);
        Text(_body, "Дозволений відправник", 18, true);
        Text(_body, _store.GetMainMavkaEmail(), 15);
        Text(_body, "Пошук лише у «Вхідних» Gmail, без зміни стану листів.", 13);
    }

    private void ShowInbox()
    {
        Begin("inbox", "Вхідні");
        var card = Card();
        Text(card, "Листи ГОЛОВНОЇ МАВКИ", 18, true);
        Text(card, "Адреса: " + _store.GetMainMavkaEmail(), 14);
        var status = Text(card, _store.HasCredentials
            ? "Листи доступні після синхронізації."
            : "Спочатку підключи Gmail у Налаштуваннях.", 14);
        Button(card, "Оновити листи", async () =>
        {
            await RefreshMessagesAsync(status);
            if (_screen == "inbox") ShowInboxContent();
        });
        ShowInboxContent();
    }

    private void ShowInboxContent()
    {
        // The two first child views are the title and controls card.
        while (_body.ChildCount > 2)
            _body.RemoveViewAt(2);
        if (_messages.Count == 0)
        {
            Text(_body, "Службових листів поки немає.", 16);
            return;
        }
        foreach (var mail in _messages)
        {
            var card = Card();
            Text(card, mail.Subject, 17, true);
            Text(card, mail.DateText, 13);
            Button(card, "Читати повідомлення", () => ShowMessage(mail));
        }
    }

    private void ShowMessage(MavkaMailItem mail)
    {
        Begin("message", "Повідомлення");
        var card = Card();
        Text(card, mail.Subject, 19, true);
        Text(card, mail.DateText, 14);
        Text(card, mail.Body, 15);
        Button(_body, "← До вхідних", ShowInbox);
    }

    private async Task RefreshMessagesAsync(TextView status)
    {
        if (_busy) return;
        if (!_store.HasCredentials)
        {
            status.Text = "Підключи Gmail у меню «Налаштування → Пошта Gmail».";
            return;
        }
        var sender = _store.GetMainMavkaEmail();
        if (string.IsNullOrWhiteSpace(sender))
        {
            status.Text = "У налаштуваннях не задана адреса ГОЛОВНОЇ МАВКИ.";
            return;
        }
        _busy = true;
        status.Text = "Перевірка службових листів...";
        try
        {
            var password = _store.GetPassword();
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Пароль не збережений.");
            _messages = await GmailConnectionService.ReadFromSenderAsync(
                _store.GetEmail(), password, sender);
            if (_screen is "home" or "inbox")
                status.Text = $"Завантажено листів: {_messages.Count}. Тільки від дозволеної адреси.";
        }
        catch (MailKit.Security.AuthenticationException)
        {
            if (_screen is "home" or "inbox")
                status.Text = "Google відхилив авторизацію. Перевір пароль застосунку.";
        }
        catch (System.OperationCanceledException)
        {
            if (_screen is "home" or "inbox")
                status.Text = "Перевищено час очікування з’єднання.";
        }
        catch (Exception ex)
        {
            if (_screen is "home" or "inbox")
                status.Text = $"Помилка читання пошти: {ex.GetType().Name}";
        }
        finally { _busy = false; }
    }

    private void ShowDrawer()
    {
        _drawerOverlay.Visibility = ViewStates.Visible;
        _drawerOverlay.BringToFront();
    }

    private void CloseDrawer()
    {
        _drawerOverlay.Visibility = ViewStates.Gone;
    }

    private UserProfile? LoadUserProfile()
    {
        try { return _profileStore.Load(); }
        catch { return null; }
    }

    private void ShowProfile()
    {
        Begin("profile", "Профіль користувача");
        var current = LoadUserProfile();
        var card = Card();

        Text(card, "Підрозділ", 15, true);
        var unit = ProfileInput(card, "Назва підрозділу", current?.Unit);
        Text(card, "ПІБ", 15, true);
        var fullName = ProfileInput(card, "Прізвище Ім’я По батькові", current?.FullName);
        Text(card, "Військове звання", 15, true);
        var rank = ProfileInput(card, "Звання", current?.Rank);
        Text(card, "Посада", 15, true);
        var position = ProfileInput(card, "Посада користувача", current?.Position);
        Text(card, "Матеріально відповідальна особа (МВО)", 15, true);

        var mvoRow = new RadioGroup(this) { Orientation = Orientation.Horizontal };
        var yes = new RadioButton(this) { Text = "МВО (+)" };
        var no = new RadioButton(this) { Text = "МВО (−)" };
        yes.Id = View.GenerateViewId();
        no.Id = View.GenerateViewId();
        mvoRow.AddView(yes);
        mvoRow.AddView(no);
        mvoRow.Check(current?.IsMvo == true ? yes.Id : no.Id);
        card.AddView(mvoRow);

        Text(card, "Поля потрібні для реєстраційного листа до ГОЛОВНОЇ МАВКИ.", 13);
        var status = Text(card,
            current is null ? "Профіль ще не збережено." : "Профіль збережено локально.", 14);

        UserProfile? CollectAndSave()
        {
            var u = (unit.Text ?? "").Trim();
            var name = (fullName.Text ?? "").Trim();
            var r = (rank.Text ?? "").Trim();
            var p = (position.Text ?? "").Trim();
            if (new[] { u, name, r, p }.Any(x => x.Length == 0 || x.Length > 160))
            {
                status.Text = "Заповни всі поля (не більш ніж 160 символів кожне).";
                return null;
            }
            try
            {
                var stored = _profileStore.Save(u, name, r, p, yes.Checked);
                status.Text = "Профіль успішно збережено на телефоні.";
                return stored;
            }
            catch (Exception ex)
            {
                status.Text = "Помилка збереження профілю: " + ex.GetType().Name;
                return null;
            }
        }

        Button(card, "Зберегти профіль", () => { CollectAndSave(); });

        Button(card, "Переглянути реєстраційний лист", () =>
        {
            var profile = CollectAndSave();
            if (profile is null) return;
            var recipient = _store.GetMainMavkaEmail();
            var body = RegistrationMailService.BuildBody(profile, _store.GetEmail());
            new AlertDialog.Builder(this)
                .SetTitle("Реєстрація → " + recipient)
                .SetMessage(body)
                .SetPositiveButton("Закрити", (_, _) => { })
                .Show();
        });

        var sendButton = new Android.Widget.Button(this) { Text = "Надіслати реєстрацію ГОЛОВНІЙ МАВЦІ" };
        card.AddView(sendButton);
        sendButton.Click += (_, _) =>
        {
            if (_busy) return;
            var profile = CollectAndSave();
            if (profile is null) return;
            if (!_store.HasCredentials)
            {
                status.Text = "Спочатку підключи Gmail у Налаштуваннях.";
                return;
            }
            var sender = _store.GetEmail();
            var recipient = _store.GetMainMavkaEmail();
            new AlertDialog.Builder(this)
                .SetTitle("Надіслати реєстраційний лист?")
                .SetMessage("З пошти: " + sender + "\nКому: " + recipient +
                            "\n\nБуде передано збережені поля профілю. " +
                            "Надсилання не означає, що головна Мавка вже зареєструвала користувача.")
                .SetNegativeButton("Скасувати", (_, _) => { })
                .SetPositiveButton("Надіслати", async (_, _) =>
                {
                    if (_busy) return;
                    _busy = true;
                    sendButton.Enabled = false;
                    status.Text = "Відправлення листа через Gmail…";
                    try
                    {
                        var password = _store.GetPassword();
                        if (string.IsNullOrEmpty(password))
                            throw new InvalidOperationException("Gmail credentials missing.");
                        await RegistrationMailService.SendAsync(
                            sender, password, recipient, profile);
                        if (_screen == "profile")
                            status.Text = "Лист надіслано. Очікуємо підтвердження реєстрації від ГОЛОВНОЇ МАВКИ.";
                    }
                    catch (MailKit.Security.AuthenticationException)
                    {
                        if (_screen == "profile")
                            status.Text = "Gmail відхилив пароль застосунку.";
                    }
                    catch (System.OperationCanceledException)
                    {
                        if (_screen == "profile")
                            status.Text = "Перевищено час очікування надсилання.";
                    }
                    catch (Exception ex)
                    {
                        if (_screen == "profile")
                            status.Text = "Помилка надсилання: " + ex.GetType().Name;
                    }
                    finally { _busy = false; sendButton.Enabled = true; }
                })
                .Show();
        };
    }

    private EditText ProfileInput(LinearLayout parent, string hint, string? value)
    {
        var field = new EditText(this)
        {
            Hint = hint,
            Text = value ?? "",
            InputType = InputTypes.ClassText | InputTypes.TextFlagCapSentences
        };
        field.SetSingleLine(true);
        parent.AddView(field);
        return field;
    }

    private void ShowSettings()
    {
        Begin("settings", "Налаштування");
        var card = Card();
        Text(card, "Пошта Gmail", 20, true);
        Text(card, _store.HasCredentials ? _store.GetEmail() : "Не підключено", 16);
        Button(card, "Пошта Gmail →", ShowMail);
        card = Card();
        Text(card, "Адреса ГОЛОВНОЇ МАВКИ", 18, true);
        Text(card, _store.GetMainMavkaEmail(), 16);
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
            Hint = _store.HasCredentials ? "Збережений пароль (вводити знову не потрібно)" : "16 символів",
            InputType = InputTypes.ClassText | InputTypes.TextVariationPassword
        };
        secret.SetSingleLine(true);
        secret.TransformationMethod = Android.Text.Method.PasswordTransformationMethod.Instance;
        card.AddView(secret);
        Text(card, "Пароль вводиться лише на телефоні й зберігається захищено.", 13);
        var status = Text(card, "Підключення ще не перевірялося.", 14);

        Text(card, "Адреса ГОЛОВНОЇ МАВКИ", 15, true);
        var mainMavkaEmail = new EditText(this)
        {
            Hint = "Адреса службової пошти",
            Text = _store.GetMainMavkaEmail(),
            InputType = InputTypes.ClassText | InputTypes.TextVariationEmailAddress
        };
        mainMavkaEmail.SetSingleLine(true);
        card.AddView(mainMavkaEmail);

        Button(card, "Зберегти адресу ГОЛОВНОЇ МАВКИ", () =>
        {
            var address = (mainMavkaEmail.Text ?? "").Trim();
            if (!System.Net.Mail.MailAddress.TryCreate(address, out var parsed) ||
                !string.Equals(parsed?.Address, address, StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "Введи правильну адресу головної Мавки.";
                return;
            }
            try
            {
                _store.SaveMainMavkaEmail(address);
                _messages = Array.Empty<MavkaMailItem>(); // Do not retain messages after sender change.
                status.Text = "Адресу головної Мавки збережено.";
            }
            catch { status.Text = "Не вдалося зберегти адресу."; }
        });

        var save = new Button(this) { Text = "Перевірити й зберегти" };
        card.AddView(save);
        save.Click += async (_, _) =>
        {
            if (_busy) return;
            var address = (email.Text ?? "").Trim();
            var password = (secret.Text ?? "").Replace(" ", "").Trim();
            if (!address.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) ||
                !System.Net.Mail.MailAddress.TryCreate(address, out var parsed) ||
                !string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "Введи коректну адресу Gmail.";
                return;
            }
            if (password.Length == 0 && _store.HasCredentials &&
                string.Equals(address, _store.GetEmail(), StringComparison.OrdinalIgnoreCase))
            {
                try { password = _store.GetPassword() ?? ""; }
                catch { status.Text = "Не вдалося відкрити збережений пароль."; return; }
            }
            if (password.Length == 0) { status.Text = "Введи пароль застосунку Google."; return; }

            _busy = true;
            save.Enabled = false;
            string stage = "1/5: Підключення до Gmail";
            try
            {
                await GmailConnectionService.TestImapAsync(
                    address, password, text => { stage = text; status.Text = text; });
                stage = "5/5: Захищене збереження Android";
                status.Text = stage;
                _store.Save(address, password);
                _messages = Array.Empty<MavkaMailItem>();
                secret.Text = "";
                secret.Hint = "Збережений пароль (вводити знову не потрібно)";
                status.Text = "Gmail підключено. Можна читати службові листи.";
            }
            catch (MailKit.Security.AuthenticationException)
            {
                status.Text = "Google відхилив вхід. Перевір пароль застосунку.";
            }
            catch (System.OperationCanceledException)
            {
                status.Text = $"Час очікування минув. Етап: {stage}";
            }
            catch (Exception ex)
            {
                status.Text = $"Помилка на етапі {stage}: {ex.GetType().Name}";
            }
            finally { _busy = false; save.Enabled = true; }
        };

        Button(card, "Відключити Gmail", () =>
        {
            try
            {
                _store.Clear();
                _messages = Array.Empty<MavkaMailItem>();
                email.Text = _store.GetEmail();
                secret.Text = "";
                secret.Hint = "16 символів";
                status.Text = "Gmail відключено, адреса ГОЛОВНОЇ МАВКИ збережена.";
            }
            catch { status.Text = "Не вдалося очистити налаштування."; }
        });
        Button(_body, "← До налаштувань", ShowSettings);
    }

    public override void OnBackPressed()
    {
        if (_drawerOverlay.Visibility == ViewStates.Visible) { CloseDrawer(); return; }
        if (_screen == "mail") { ShowSettings(); return; }
        if (_screen == "message") { ShowInbox(); return; }
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
