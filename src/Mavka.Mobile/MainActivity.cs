using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace Mavka.Mobile;

[Activity(Label = "Мавка Mobile", MainLauncher = true, Exported = true,
    ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation |
                           Android.Content.PM.ConfigChanges.ScreenSize |
                           Android.Content.PM.ConfigChanges.KeyboardHidden)]
public class MainActivity : Activity
{
    private LinearLayout _body = null!;
    private GmailCredentialsStore _store = null!;
    private UserProfileStore _profileStore = null!;
    private PropertyLedgerStore _ledger = null!;
    private FrameLayout _drawerOverlay = null!;
    private Button _home = null!, _inbox = null!, _settings = null!;
    private string _screen = "home";
    private bool _busy;
    private IReadOnlyList<MavkaMailItem> _messages = Array.Empty<MavkaMailItem>();

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        // Keep the editable fields visible above the soft keyboard.
        Window?.SetSoftInputMode(SoftInput.AdjustResize | SoftInput.StateAlwaysHidden);
        _store = new GmailCredentialsStore(this);
        _profileStore = new UserProfileStore(this);
        _ledger = new PropertyLedgerStore(this);
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
        Button(sidePanel, "Майно підрозділу", () => { CloseDrawer(); ShowAssets(); });
        Button(sidePanel, "Профіль користувача", () => { CloseDrawer(); ShowProfile(); });
        Button(sidePanel, "Налаштування", () => { CloseDrawer(); ShowSettings(); });
        Button(sidePanel, "Закрити меню", CloseDrawer);

        _drawerOverlay.AddView(sidePanel,
            new FrameLayout.LayoutParams(Dp(292), -1, GravityFlags.Right));
        host.AddView(_drawerOverlay, new FrameLayout.LayoutParams(-1, -1));

        SetContentView(host);
        ShowHome();
        // Orientation changes are handled by this Activity, not a new OnCreate.
        // Do not replay welcome after restoring an existing Activity.
        if (state is null)
            StartWelcomeSplash(host);
    }

    private async void StartWelcomeSplash(FrameLayout host)
    {
        // Approved artwork is bundled inside the APK; no download at startup.
        var welcome = new FrameLayout(this);
        welcome.SetBackgroundColor(Color.Rgb(12, 55, 41));

        try
        {
            using var stream = Assets!.Open("mavka_splash.webp");
            var bitmap = BitmapFactory.DecodeStream(stream);
            if (bitmap is not null)
            {
                var picture = new ImageView(this);
                picture.SetScaleType(ImageView.ScaleType.CenterCrop);
                picture.SetImageBitmap(bitmap);
                welcome.AddView(picture, new FrameLayout.LayoutParams(-1, -1));
            }
        }
        catch (Java.IO.IOException)
        {
            var fallback = new TextView(this)
            {
                Text = "Mavka Mobile\n\nВас вітає компанія RAM Systems",
                TextSize = 24,
                Gravity = GravityFlags.Center
            };
            fallback.SetTextColor(Color.White);
            welcome.AddView(fallback, new FrameLayout.LayoutParams(-1, -1));
        }

        host.AddView(welcome, new FrameLayout.LayoutParams(-1, -1));
        welcome.BringToFront();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(3500));
        }
        finally
        {
            if (welcome.Parent == host)
                host.RemoveView(welcome);
        }
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
        Button(card, "Майно підрозділу", ShowAssets);
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

    private void ShowAssets()
    {
        Begin("assets", "Майно підрозділу");
        var top = Card();
        var profile = LoadUserProfile();
        Text(top, "Підрозділ: " + (profile?.Unit ?? "не визначений у профілі"), 17, true);
        var status = Text(top, "Джерело: JSON-лист ГОЛОВНОЇ МАВКИ (MAVKA_UNIT_ASSETS_V1).", 13);
        Button(top, "Оновити дані з пошти", async () =>
        {
            await RefreshMessagesAsync(status);
            if (_screen == "assets") RenderAssets();
        });
        RenderAssets();
    }

    private void RenderAssets()
    {
        while (_body.ChildCount > 2)
            _body.RemoveViewAt(2);

        var profile = LoadUserProfile();
        if (profile is null || string.IsNullOrWhiteSpace(profile.Unit))
        {
            Text(_body, "Спочатку заповни підрозділ у профілі користувача.", 16);
            Button(_body, "Профіль користувача", ShowProfile);
            return;
        }

        // Local holdings reflect only manually confirmed, transactionally stored receipts.
        var localCard = Card();
        Text(localCard, "Майно, підтверджене на телефоні", 18, true);
        var search = new EditText(this)
        {
            Hint = "Пошук за назвою або NSN",
            InputType = InputTypes.ClassText
        };
        ConfigureKeyboard(search);
        localCard.AddView(search);
        var localRows = new LinearLayout(this) { Orientation = Orientation.Vertical };
        localCard.AddView(localRows);
        void DrawLocal()
        {
            localRows.RemoveAllViews();
            try
            {
                var all = _ledger.ListHoldings(profile.Unit);
                var query = (search.Text ?? "").Trim();
                var filtered = all.Where(x => query.Length == 0 ||
                    x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (x.Nsn?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)).ToList();
                Text(localRows, $"Локальних позицій: {filtered.Count} із {all.Count}", 14);
                foreach (var asset in filtered)
                {
                    Text(localRows, asset.Name + " — " + asset.Quantity.ToString("0.###") +
                        " " + asset.Unit + (string.IsNullOrEmpty(asset.Nsn) ? "" : " · " + asset.Nsn), 14);
                }
                if (all.Count == 0)
                    Text(localRows, "Ще немає підтверджених накладних.", 14);
            }
            catch (Exception ex)
            {
                Text(localRows, "Помилка локальної бази: " + ex.GetType().Name, 14);
            }
        }
        search.TextChanged += (_, _) => DrawLocal();
        DrawLocal();

        // Only snapshots received from the configured sender are present in _messages.
        // Never merge snapshots from different units or add quantities from separate dates.
        var latest = _messages.SelectMany(mail => mail.Attachments
                .Where(a => a.RawJson is not null)
                .Select(a => new { mail, attachment = a }))
            .Select(entry => new
            {
                entry.mail,
                parsed = MavkaExchangeParser.TryAssets(entry.attachment.RawJson, out var snapshot)
                    ? snapshot : null
            })
            .Where(entry => entry.parsed is not null &&
                string.Equals(entry.parsed.Unit, profile.Unit, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.mail.MessageUid)
            .FirstOrDefault();

        if (latest?.parsed is null)
        {
            Text(_body, "Окремого знімка майна з ГОЛОВНОЇ МАВКИ наразі немає.", 14);
            return;
        }

        var snapshot = latest.parsed;
        var header = Card();
        Text(header, "Окремий знімок із ГОЛОВНОЇ МАВКИ (не сумується з локальним)", 16, true);
        Text(header, "Підрозділ: " + snapshot.Unit, 17, true);
        Text(header, "Станом на: " +
            (string.IsNullOrWhiteSpace(snapshot.AsOf) ? "дату не зазначено" : snapshot.AsOf), 14);
        Text(header, "Позицій: " + snapshot.Items.Count, 14);
        Text(header, "Перегляд отриманого знімка. Не є підтвердженим залишком у головній базі.", 13);

        foreach (var asset in snapshot.Items)
        {
            var card = Card();
            Text(card, asset.Name, 16, true);
            Text(card, asset.Quantity.ToString("0.###") + " " + asset.Measure, 15);
        }
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
        var status = Text(card, "Лист із налаштованої адреси ГОЛОВНОЇ МАВКИ.", 13);
        if (mail.Attachments.Count > 0)
        {
            Text(_body, "Вкладення TXT / JSON", 19, true);
            foreach (var attachment in mail.Attachments)
            {
                var fileCard = Card();
                Text(fileCard, attachment.FileName, 16, true);
                Text(fileCard, "Перегляд без збереження й автоматичного імпорту", 13);
                Button(fileCard, "Переглянути файл", () =>
                    new AlertDialog.Builder(this)
                        .SetTitle(attachment.FileName)
                        .SetMessage(attachment.Text)
                        .SetPositiveButton("Закрити", (_, _) => { })
                        .Show());
            }
        }
        foreach (var attachment in mail.Attachments)
        {
            if (!MavkaExchangeParser.TryInvoice(attachment.RawJson, out var invoice) ||
                invoice is null) continue;

            var info = Card();
            Text(info, "Накладна № " + invoice.Number, 17, true);
            Text(info, "Отримувач: " + invoice.Recipient, 14);
            Button(info, "Підтвердити отримання майна", () =>
            {
                if (_busy) return;
                var profile = LoadUserProfile();
                if (profile is null)
                {
                    status.Text = "Спочатку заповни профіль користувача.";
                    return;
                }
                if (!profile.IsMvo)
                {
                    status.Text = "Для підтвердження потрібен профіль МВО (+).";
                    return;
                }
                if (!string.Equals(profile.Unit.Trim(), invoice.Recipient.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    status.Text = "Отримувач накладної не збігається з підрозділом у профілі.";
                    return;
                }
                if (!_store.HasCredentials)
                {
                    status.Text = "Спочатку підключи Gmail у Налаштуваннях.";
                    return;
                }
                string? existingStatus;
                try { existingStatus = _ledger.GetMailStatus(invoice.DocumentId); }
                catch (Exception ex)
                {
                    status.Text = "Помилка локальної бази: " + ex.GetType().Name;
                    return;
                }
                if (existingStatus == "sent")
                {
                    status.Text = "Накладна вже зарахована до локальної бази й підтвердження надіслано.";
                    return;
                }
                new AlertDialog.Builder(this)
                    .SetTitle("Підтвердження отримання")
                    .SetMessage("Документ № " + invoice.Number +
                        "\nПідрозділ: " + profile.Unit +
                        "\nМВО: " + profile.FullName +
                        "\n\nПозицій: " + invoice.Positions.Count +
                        "\nПісля натискання майно буде додано до локальної бази телефону, " +
                        "а підтвердження надіслано ГОЛОВНІЙ МАВЦІ. " +
                        "Це не означає автоматичне проведення документа в головній базі.")
                    .SetNegativeButton("Скасувати", (_, _) => { })
                    .SetPositiveButton("Підтвердити й надіслати", async (_, _) =>
                    {
                        if (_busy) return;
                        _busy = true;
                        status.Text = "Надсилаємо підтвердження...";
                        try
                        {
                            // SQLite transaction commits receipt and its items atomically.
                            // The document ID is unique; retrying must never double inventory.
                            bool added = _ledger.AddConfirmedInvoice(invoice);
                            status.Text = added
                                ? "Майно зараховано локально. Надсилаємо підтвердження..."
                                : "Накладну вже зараховано. Повторно надсилаємо підтвердження...";

                            var password = _store.GetPassword() ??
                                throw new InvalidOperationException("Пароль Gmail відсутній.");
                            await RegistrationMailService.SendReceiptAsync(
                                _store.GetEmail(), password, _store.GetMainMavkaEmail(),
                                profile, invoice);
                            _ledger.MarkMailSent(invoice.DocumentId);
                            if (_screen == "message")
                                status.Text = "Майно зараховано в локальну базу. " +
                                    "Лист-підтвердження надіслано. " +
                                    "ГОЛОВНА МАВКА має обробити його окремо.";
                        }
                        catch (Exception ex)
                        {
                            if (_screen == "message")
                                status.Text = "Помилка: " + ex.GetType().Name +
                                    ". Якщо майно вже зараховано локально, воно не буде " +
                                    "зараховане вдруге. Повтори надсилання підтвердження.";
                        }
                        finally { _busy = false; }
                    })
                    .Show();
            });
        }

        Button(_body, "Перемістити лист у кошик Gmail", () =>
        {
            if (_busy) return;
            new AlertDialog.Builder(this)
                .SetTitle("Перемістити лист у кошик?")
                .SetMessage("Лист «" + mail.Subject +
                    "» буде переміщено в кошик Gmail. Це не видалить документ з головної Мавки.")
                .SetNegativeButton("Скасувати", (_, _) => { })
                .SetPositiveButton("У кошик", async (_, _) =>
                {
                    if (_busy) return;
                    _busy = true;
                    status.Text = "Переміщення листа в кошик...";
                    try
                    {
                        var pass = _store.GetPassword() ??
                            throw new InvalidOperationException("Пароль Gmail відсутній.");
                        await GmailConnectionService.MoveToTrashAsync(
                            _store.GetEmail(), pass, _store.GetMainMavkaEmail(), mail);
                        _messages = _messages.Where(m => !(m.MessageUid == mail.MessageUid &&
                            m.UidValidity == mail.UidValidity)).ToList();
                        ShowInbox();
                    }
                    catch (Exception ex)
                    {
                        if (_screen == "message")
                            status.Text = "Помилка переміщення: " + ex.GetType().Name;
                    }
                    finally { _busy = false; }
                })
                .Show();
        });
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
            if (_screen is "home" or "inbox" or "assets")
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
        ConfigureKeyboard(field);
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
        ConfigureKeyboard(email);
        card.AddView(email);

        Text(card, "Пароль застосунку Google", 15, true);
        var secret = new EditText(this)
        {
            Hint = _store.HasCredentials ? "Збережений пароль (вводити знову не потрібно)" : "16 символів",
            InputType = InputTypes.ClassText | InputTypes.TextVariationPassword
        };
        ConfigureKeyboard(secret, password: true);
        secret.TransformationMethod = Android.Text.Method.PasswordTransformationMethod.Instance;
        card.AddView(secret);
        Text(card, "Пароль вводиться лише на телефоні й зберігається захищено.", 13);
        var status = Text(card, "Підключення ще не перевірялося.", 14);

        Button(card, "Діагностика SSL/TLS (без пароля)", async () =>
        {
            if (_busy) return;
            _busy = true;
            status.Text = "Перевірка HTTPS та IMAP...";
            try
            {
                var diagnostic = await GmailConnectionService.DiagnoseTlsAsync();
                if (_screen != "mail") return;
                status.Text = "Діагностика завершена. Результати відкрито нижче.";
                new AlertDialog.Builder(this)
                    .SetTitle("Перевірка SSL/TLS (пароль не потрібен)")
                    .SetMessage(diagnostic)
                    .SetPositiveButton("Закрити", (_, _) => { })
                    .Show();
            }
            catch (Exception ex)
            {
                if (_screen == "mail")
                    status.Text = "Діагностика: " + ex.GetType().Name;
            }
            finally { _busy = false; }
        });

        Text(card, "Адреса ГОЛОВНОЇ МАВКИ", 15, true);
        var mainMavkaEmail = new EditText(this)
        {
            Hint = "Адреса службової пошти",
            Text = _store.GetMainMavkaEmail(),
            InputType = InputTypes.ClassText | InputTypes.TextVariationEmailAddress
        };
        ConfigureKeyboard(mainMavkaEmail);
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
                var revocationFallback = await GmailConnectionService.TestImapAsync(
                    address, password, text => { stage = text; status.Text = text; });
                stage = "5/5: Захищене збереження Android";
                status.Text = stage;
                _store.Save(address, password);
                _messages = Array.Empty<MavkaMailItem>();
                secret.Text = "";
                secret.Hint = "Збережений пароль (вводити знову не потрібно)";
                status.Text = revocationFallback
                    ? "Gmail підключено. Увага: перевірка відкликання сертифіката (OCSP/CRL) недоступна; інші перевірки TLS збережено."
                    : "Gmail підключено. Можна читати службові листи.";
            }
            catch (MailKit.Security.SslHandshakeException)
            {
                status.Text = "SSL/TLS не вдалося встановити. Натисни «Діагностика SSL/TLS (без пароля)», щоб дізнатися причину.";
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

    private void ConfigureKeyboard(EditText field, bool password = false)
    {
        field.SetSingleLine(true);
        // Some Android keyboards show a full-screen editor in landscape mode.
        // NO_FULLSCREEN requests the ordinary embedded keyboard instead.
        const int noFullscreen = 0x02000000;
        const int noPersonalizedLearning = 0x01000000;
        field.ImeOptions = (Android.Views.InputMethods.ImeAction)(
            (int)Android.Views.InputMethods.ImeAction.Done |
            noFullscreen |
            (password ? noPersonalizedLearning : 0));
        field.SetSelectAllOnFocus(false);
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
