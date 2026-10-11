using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
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
    private DocumentArchiveStore _archive = null!;
    private MavkaExportFileStore _exportedFiles = null!;
    private bool _viewingArchivedDocument;
    private DateOnly _assetsAsOf = DateOnly.FromDateTime(DateTime.Today);
    private AssetIconProvider _assetIcons = null!;
    private ExchangeKeyStore _exchangeKeys = null!;
    private DocumentPasswordStore _documentPasswords = null!;
    private FrameLayout _drawerOverlay = null!;
    private TextView _home = null!, _inbox = null!, _assets = null!, _settings = null!;
    private string _screen = "home";
    private string _inboxQuery = "";
    private string _mailFilter = "all";
    private bool _busy;
    private IReadOnlyList<MavkaMailItem> _messages = Array.Empty<MavkaMailItem>();

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        // Keep the editable fields visible above the soft keyboard.
        Window?.SetSoftInputMode(SoftInput.AdjustResize | SoftInput.StateAlwaysHidden);
        Window?.SetStatusBarColor(Color.Rgb(13, 55, 36));
        Window?.SetNavigationBarColor(Color.White);
        _store = new GmailCredentialsStore(this);
        _profileStore = new UserProfileStore(this);
        _ledger = new PropertyLedgerStore(this);
        _archive = new DocumentArchiveStore(this);
        _exportedFiles = new MavkaExportFileStore(this);
        _assetIcons = new AssetIconProvider(this);
        _exchangeKeys = new ExchangeKeyStore(this);
        _documentPasswords = new DocumentPasswordStore(this);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.Rgb(249, 247, 242));
        root.SetOnApplyWindowInsetsListener(new SafeInsets(Dp(14), Dp(10), Dp(10)));

        var topBar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        topBar.SetGravity(GravityFlags.CenterVertical);
        topBar.SetPadding(Dp(10), 0, Dp(8), 0);
        topBar.SetBackgroundColor(Color.Rgb(13, 55, 36));
        var appMark = new ImageView(this);
        appMark.SetImageResource(Resource.Drawable.mavka_app_icon);
        appMark.SetScaleType(ImageView.ScaleType.FitCenter);
        var logoLayout = new LinearLayout.LayoutParams(Dp(40), Dp(40));
        logoLayout.SetMargins(0, 0, Dp(8), 0);
        topBar.AddView(appMark, logoLayout);
        var brand = new TextView(this) { Text = "Мавка Mobile", TextSize = 20 };
        brand.SetTextColor(Color.White);
        brand.SetTypeface(null, TypefaceStyle.Bold);
        topBar.AddView(brand, new LinearLayout.LayoutParams(0, Dp(52), 1));
        brand.Gravity = GravityFlags.CenterVertical;
        var hamburger = new Android.Widget.Button(this) { Text = "☰", TextSize = 23 };
        hamburger.SetSingleLine(true);
        hamburger.SetTextColor(Color.White);
        hamburger.Background = Rounded(Color.Rgb(25, 82, 53), 12);
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
        _home = Nav("⌂\nГоловна", ShowHome);
        _inbox = Nav("✉\nВхідні", ShowInbox);
        _assets = Nav("▣\nМайно", ShowAssets);
        _settings = Nav("⚙\nНалашт.", ShowSettings);
        _settings.ContentDescription = "Налаштування";
        nav.AddView(_home);
        nav.AddView(_inbox);
        nav.AddView(_assets);
        nav.AddView(_settings);
        nav.SetPadding(Dp(2), Dp(3), Dp(2), Dp(3));
        nav.SetBackgroundColor(Color.White);
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
        sidePanel.SetBackgroundColor(Color.Rgb(249, 247, 242));
        sidePanel.SetPadding(Dp(14), Dp(50), Dp(14), Dp(30));
        Text(sidePanel, "Мавка Mobile", 22, true);
        Text(sidePanel, "Меню", 15);
        Button(sidePanel, "Головна", () => { CloseDrawer(); ShowHome(); });
        Button(sidePanel, "Вхідні", () => { CloseDrawer(); ShowInbox(); });
        Button(sidePanel, "Майно підрозділу", () => { CloseDrawer(); ShowAssets(); });
        Button(sidePanel, "Історія документів", () => { CloseDrawer(); ShowDocuments(); });
        Button(sidePanel, "МаVКа файли", () => { CloseDrawer(); ShowFiles(); });
        Button(sidePanel, "Профіль користувача", () => { CloseDrawer(); ShowProfile(); });
        Button(sidePanel, "Ключ обміну", () => { CloseDrawer(); ShowExchangeKey(); });
        Button(sidePanel, "Пароль документів", () => { CloseDrawer(); ShowDocumentPassword(); });
        Button(sidePanel, "Налаштування", () => { CloseDrawer(); ShowSettings(); });
        Button(sidePanel, "Закрити меню", CloseDrawer);

        // The menu scrolls on short phones instead of losing lower actions.
        var drawerScroll = new ScrollView(this) { FillViewport = true };
        drawerScroll.SetBackgroundColor(Color.Rgb(249, 247, 242));
        drawerScroll.AddView(sidePanel);
        _drawerOverlay.AddView(drawerScroll,
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
        // Keep all tabs clickable; a selected tab is highlighted rather than disabled.
        StyleNav(_home, screen == "home");
        StyleNav(_inbox, screen is "inbox" or "message");
        StyleNav(_assets, screen == "assets");
        StyleNav(_settings, screen is "settings" or "mail" or "profile" or "exchange-key" or "document-password");
        Text(_body, title, 24, true);
    }

    private void ShowHome()
    {
        Begin("home", "Головна");
        var card = Card();
        Text(card, "Синхронізація", 20, true);
        var status = Text(card,
            _store.HasCredentials ? "Gmail налаштовано" : "Gmail не підключено", 16);
        Button(card, "Оновити", async () => await RefreshMessagesAsync(status));
        Button(card, "Повторити надсилання підтверджень", async () =>
        {
            if (_busy) return;
            if (!_store.HasCredentials || string.IsNullOrWhiteSpace(_store.GetMainMavkaEmail()))
            {
                status.Text = "Спочатку підключи Gmail і задай адресу ГОЛОВНОЇ МАВКИ.";
                return;
            }
            _busy = true;
            status.Text = "Повторно надсилаємо збережені підтвердження...";
            try
            {
                var password = _store.GetPassword() ??
                    throw new InvalidOperationException("Пароль Gmail відсутній.");
                var result = await SendPendingReceiptsAsync(password, _store.GetMainMavkaEmail());
                if (_screen == "home") status.Text = result;
            }
            catch (Exception ex)
            {
                if (_screen == "home")
                    status.Text = "Помилка надсилання: " + ex.GetType().Name;
            }
            finally { _busy = false; }
        });
        try
        {
            var pending = _ledger.ListPendingReceiptIds();
            Text(card, "Підтвердження в черзі: " + pending.Count, 14);
        }
        catch (Exception ex) { Text(card, "Черга: " + ex.GetType().Name, 12); }
        card = Card();
        Text(card, "Службові повідомлення", 20, true);
        Text(card, $"Завантажено: {_messages.Count}", 18);
        Button(card, "Перейти до вхідних", ShowInbox);
        Button(card, "Майно підрозділу", ShowAssets);
        Button(card, "Історія документів", ShowDocuments);
        Button(card, "МаVКа файли", ShowFiles);
        var profile = LoadUserProfile();
        if (profile is not null)
        {
            try { Text(card, "У локальному архіві: " + _archive.CountDocuments(profile.Unit), 14); }
            catch (Exception ex) { Text(card, "Архів: " + ex.GetType().Name, 12); }
        }
        Text(_body, "Дозволений відправник", 18, true);
        Text(_body, _store.GetMainMavkaEmail(), 15);
    }

    private void ShowInbox()
    {
        _viewingArchivedDocument = false;
        Begin("inbox", "Вхідні");
        var card = Card();
        Text(card, "Листи ГОЛОВНОЇ МАВКИ", 17, true);
        Text(card, "Лише від " + _store.GetMainMavkaEmail(), 12);

        var search = new EditText(this)
        {
            Hint = "Пошук за номером, датою або типом",
            Text = _inboxQuery,
            TextSize = 14
        };
        search.SetSingleLine(true);
        search.SetPadding(Dp(12), Dp(7), Dp(12), Dp(7));
        search.Background = Rounded(Color.Rgb(248, 248, 245), 12,
            Color.Rgb(234, 236, 229));
        ConfigureKeyboard(search);
        card.AddView(search, new LinearLayout.LayoutParams(-1, Dp(43)));
        search.TextChanged += (_, _) =>
        {
            _inboxQuery = search.Text ?? "";
            if (_screen == "inbox") ShowInboxContent();
        };

        var filters = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        filters.SetPadding(0, Dp(8), 0, Dp(6));
        card.AddView(filters);
        foreach (var (label, value) in new[]
        {
            ("Усі", "all"), ("Накладні", "invoices"), ("Інші", "other")
        })
        {
            var filter = new Button(this) { Text = label, TextSize = 12 };
            filter.SetAllCaps(false);
            filter.SetTextColor(value == _mailFilter ? Color.White : Color.Rgb(31, 73, 47));
            filter.Background = Rounded(value == _mailFilter
                ? Color.Rgb(9, 84, 50) : Color.Rgb(235, 240, 235), 11);
            var layout = new LinearLayout.LayoutParams(0, Dp(37), 1);
            layout.SetMargins(Dp(2), 0, Dp(2), 0);
            filters.AddView(filter, layout);
            filter.Click += (_, _) =>
            {
                _mailFilter = value;
                foreach (var child in Enumerable.Range(0, filters.ChildCount)
                    .Select(index => filters.GetChildAt(index)).OfType<Button>())
                {
                    bool active = child.Text == label;
                    child.SetTextColor(active ? Color.White : Color.Rgb(31, 73, 47));
                    child.Background = Rounded(active ? Color.Rgb(9, 84, 50) :
                        Color.Rgb(235, 240, 235), 11);
                }
                ShowInboxContent();
            };
        }

        var status = Text(card, _store.HasCredentials
            ? "Онови пошту, щоб перевірити нові документи."
            : "Підключи Gmail у Налаштуваннях.", 12);
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
        var dateLabel = Text(top, "Обліковий залишок на кінець дня: " +
            _assetsAsOf.ToString("dd.MM.yyyy"), 14, true);
        Button(top, "📅 Вибрати облікову дату", () =>
        {
            new DatePickerDialog(this, (_, date) =>
            {
                _assetsAsOf = new DateOnly(date.Year, date.Month + 1, date.DayOfMonth);
                dateLabel.Text = "Обліковий залишок на кінець дня: " +
                    _assetsAsOf.ToString("dd.MM.yyyy");
                RenderAssets();
            }, _assetsAsOf.Year, _assetsAsOf.Month - 1, _assetsAsOf.Day).Show();
        });
        Button(top, "Сьогодні", () =>
        {
            _assetsAsOf = DateOnly.FromDateTime(DateTime.Today);
            dateLabel.Text = "Обліковий залишок на кінець дня: " +
                _assetsAsOf.ToString("dd.MM.yyyy");
            RenderAssets();
        });
        var status = Text(top, "Джерело: JSON-лист ГОЛОВНОЇ МАВКИ (MAVKA_UNIT_ASSETS_V1).", 13);
        Button(top, "Оновити дані з пошти", async () =>
        {
            await RefreshMessagesAsync(status);
            if (_screen == "assets") RenderAssets();
        });
        RenderAssets();
    }

    /// <summary>
    /// Read-only visual fixture to verify the eight approved icon categories.
    /// These labels never enter the inventory ledger.
    /// </summary>
    private void ShowIconGallery()
    {
        Begin("icon-gallery", "Перевірка іконок");
        var info = Card();
        Text(info, "Демонстраційна галерея іконок", 18, true);
        Text(info, "Тут показані всі вісім категорій з архіву дизайну. " +
            "Ці записи не є майном, не мають залишків і нічого не зараховують.", 13);
        var entries = new[]
        {
            "Снаряд (демо)",
            "Заряд (демо)",
            "Постріл (демо)",
            "Праймер (демо)",
            "Реактивний (демо)",
            "Підривник (демо)",
            "12×70 мм (демо)",
            "Патрон (демо)"
        };
        foreach (var name in entries)
            AddAssetRow(info, name, "Перевірка відображення іконки", "ДЕМО");
        Button(_body, "← До майна", ShowAssets);
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
                var all = _ledger.ListHoldingsAsOf(profile.Unit, _assetsAsOf);
                var query = (search.Text ?? "").Trim();
                var filtered = all.Where(x => query.Length == 0 ||
                    x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (x.Nsn?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)).ToList();
                Text(localRows, $"Локальних облікових позицій: {filtered.Count} із {all.Count}", 14);
                var knownTotal = all.Where(x => x.TotalValue.HasValue)
                    .Sum(x => x.TotalValue ?? 0m);
                Text(localRows, "Вартість позицій із відомою ціною: " +
                    knownTotal.ToString("0.00") + " грн", 13);
                if (all.Any(x => !x.UnitPrice.HasValue))
                    Text(localRows, "Є позиції з невідомою ціною; їхню вартість не враховано.", 12);
                foreach (var asset in filtered)
                {
                    var current = asset;
                    AddAssetRow(localRows, asset.Name,
                        (string.IsNullOrEmpty(asset.Nsn) ? "Локально підтверджено" :
                            "NSN: " + asset.Nsn) +
                        (asset.UnitPrice.HasValue
                            ? " · ціна " + asset.UnitPrice.Value.ToString("0.00") +
                              " грн · вартість " + asset.TotalValue!.Value.ToString("0.00") + " грн"
                            : " · ціна не зазначена"),
                        asset.Quantity.ToString("0.###") + " " + asset.Unit,
                        () => ShowAssetHistory(profile.Unit, current.Name,
                            current.Unit, current.Nsn));
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
        // Consult independently archived snapshots first: their Gmail letters may
        // already have been moved to Trash after durable storage.
        UnitAssetsSnapshot? offlineSnapshot = null;
        try
        {
            foreach (var summary in _archive.ListSummaries(profile.Unit)
                         .Where(s => s.Kind == "Знімок залишків"))
            {
                var archived = _archive.FindDocument(summary.DocumentId);
                if (MavkaExchangeParser.TryAssets(archived?.Json, out offlineSnapshot) &&
                    offlineSnapshot is not null)
                    break;
            }
        }
        catch (Exception) { /* Archive errors must not fake a valid snapshot. */ }
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

        if (latest?.parsed is null && offlineSnapshot is null)
        {
            Text(_body, "Окремого знімка майна з ГОЛОВНОЇ МАВКИ наразі немає.", 14);
            return;
        }

        var snapshot = offlineSnapshot ?? latest!.parsed!;
        var header = Card();
        Text(header, "Окремий знімок із ГОЛОВНОЇ МАВКИ (не сумується з локальним)", 16, true);
        Text(header, "Підрозділ: " + snapshot.Unit, 17, true);
        Text(header, "Станом на: " +
            (string.IsNullOrWhiteSpace(snapshot.AsOf) ? "дату не зазначено" : snapshot.AsOf), 14);
        Text(header, "Позицій: " + snapshot.Items.Count, 14);
        Text(header, "Перегляд отриманого знімка. Не є підтвердженим залишком у головній базі.", 13);
        Button(header, "Звірити з локальним обліком (без змін)", () =>
            ShowBalanceReconciliation(snapshot, profile.Unit));

        var rows = Card();
        Text(rows, "Номенклатура зі знімка", 16, true);
        foreach (var asset in snapshot.Items)
            AddAssetRow(rows, asset.Name, "Із ГОЛОВНОЇ МАВКИ" +
                (string.IsNullOrWhiteSpace(asset.Nsn) ? "" : " · NSN: " + asset.Nsn) +
                (asset.UnitPrice.HasValue
                    ? " · " + asset.UnitPrice.Value.ToString("0.00") + " грн/од."
                    : " · ціна невідома"),
                asset.Quantity.ToString("0.###") + " " + asset.Measure,
                () => ShowAssetHistory(profile.Unit, asset.Name, asset.Measure, asset.Nsn));

        var importing = Text(header, "Початкові залишки поки не зараховано.", 12);
        Button(header, "Зарахувати початкові залишки", () =>
        {
            if (snapshot.Items.Count == 0)
            {
                importing.Text = "Знімок порожній.";
                return;
            }
            new AlertDialog.Builder(this)
                .SetTitle("Зарахувати початкові залишки?")
                .SetMessage("Підрозділ: " + snapshot.Unit +
                    "\nСтаном на: " + snapshot.AsOf +
                    "\nПозицій: " + snapshot.Items.Count +
                    "\n\nЗарахування можливе лише один раз і тільки до першої накладної. " +
                    "Інакше існує ризик подвійного обліку.")
                .SetNegativeButton("Скасувати", (_, _) => { })
                .SetPositiveButton("Зарахувати", (_, _) =>
                {
                    try
                    {
                        var changed = _ledger.AddOpeningBalances(snapshot);
                        importing.Text = changed
                            ? "Початкові залишки зараховано. Перевідкрий вкладку «Майно»."
                            : "Початкові залишки вже були зараховані; дубль відхилено.";
                    }
                    catch (InvalidOperationException)
                    {
                        importing.Text = "Зарахування відхилено: у підрозділі вже є рухи. " +
                            "Потрібна окрема звірка з ГОЛОВНОЮ МАВКОЮ.";
                    }
                    catch (Exception ex)
                    {
                        importing.Text = "Помилка локальної бази: " + ex.GetType().Name;
                    }
                }).Show();
        });
    }

    private void ShowBalanceReconciliation(UnitAssetsSnapshot snapshot, string unit)
    {
        Begin("reconciliation", "Звірка залишків");
        var details = Card();
        Text(details, "Контрольний знімок · " + unit, 18, true);
        Text(details, "Станом на " + snapshot.AsOf, 14);
        Text(details, "Зіставлення є лише довідковим. Воно не додає, не списує " +
            "та не замінює жодного облікового запису.", 13);
        Text(details, "Для знімка на початок дня рухи цієї дати слід перевірити " +
            "окремо: наведене порівняння виконується на кінець дня.", 13);
        try
        {
            var asOf = LedgerBalanceCalculator.ParseDate(snapshot.AsOf);
            var local = _ledger.ListHoldingsAsOf(unit, asOf)
                .Select(x => new HistoricalHolding(x.Name, x.Unit,
                    x.Nsn, x.Quantity, x.UnitPrice, x.TotalValue));
            var differences = LedgerReconciliation.Compare(local, snapshot.Items);
            Text(details, "Виявлено розбіжностей: " + differences.Count, 15, true);
            if (differences.Count == 0)
                Text(details, "Кількості збігаються для відповідних облікових позицій.", 14);
            foreach (var entry in differences.Take(300))
            {
                var card = Card();
                Text(card, entry.Name, 16, true);
                Text(card, (entry.Nsn is null ? "" : "NSN: " + entry.Nsn + " · ") +
                    "Од.: " + entry.Unit, 13);
                Text(card, "Локально: " + entry.LocalQuantity.ToString("0.###") +
                    " · Контрольний знімок: " + entry.ReferenceQuantity.ToString("0.###"), 14);
                Text(card, "Різниця (локально − контроль): " +
                    entry.Delta.ToString("+#,##0.###;-#,##0.###;0"), 14, true);
                if (!entry.UnitPrice.HasValue)
                    Text(card, "Вартість невідома, її не обчислено.", 12);
            }
            if (differences.Count > 300)
                Text(_body, "Відображено перші 300 розбіжностей.", 13);
        }
        catch (Exception ex)
        {
            Text(details, "Звірка неможлива без коректної дати та локальних записів: " +
                ex.GetType().Name, 14);
        }
        Button(_body, "← До майна", ShowAssets);
    }

    private void ShowAssetHistory(string unit, string name,
        string measure, string? nsn)
    {
        try
        {
            var local = _ledger.ListMovements(unit, name, measure, nsn);
            var lines = new List<string>
            {
                "Локальна історія (підтверджене майно):"
            };
            foreach (var entry in local)
            {
                var label = entry.Number == "ПОЧАТКОВІ ЗАЛИШКИ"
                    ? "Початкові залишки"
                    : "Накладна № " + entry.Number;
                lines.Add(label + " · " + entry.Date +
                    "\nНадійшло: " + entry.Quantity.ToString("0.###") + " " + measure +
                    (entry.UnitPrice.HasValue
                        ? "\nЦіна: " + entry.UnitPrice.Value.ToString("0.00") + " грн"
                        : "\nЦіна не зазначена"));
            }
            if (local.Count == 0)
                lines.Add("Локальних рухів ще немає.");

            // Reference history is read from authorized Gmail documents
            // and kept separate from SQLite accounting and local quantities.
            var offlineHistory = _archive.ListSummaries(unit)
                .Where(s => s.Kind == "Довідкова історія")
                .Select(s => _archive.FindDocument(s.DocumentId)?.Json)
                .Where(json => json is not null);
            var incomingHistory = _messages.SelectMany(mail => mail.Attachments)
                .Select(attachment => attachment.RawJson)
                .Where(json => json is not null);
            var remote = offlineHistory.Concat(incomingHistory)
                .Select(json =>
                    MavkaMovementJournal.TryRead(json, out var journal)
                        ? journal : null)
                .Where(journal => journal is not null)
                .SelectMany(journal => MavkaMovementJournal.ForAsset(
                    journal!, unit, name, measure, nsn))
                .GroupBy(entry => (entry.DocumentId, entry.Name, entry.Kind))
                .Select(group => group.First())
                .OrderByDescending(entry => entry.Date, StringComparer.Ordinal)
                .ToList();

            lines.Add("Рухи за документами ГОЛОВНОЇ МАВКИ (довідково):");
            foreach (var movement in remote)
            {
                var documentKind = movement.Kind switch
                {
                    "writeoff" => "Акт списання",
                    "invoice" => "Накладна",
                    "transfer" => "Документ переміщення",
                    "adjustment" => "Коригування",
                    _ => "Документ"
                };
                lines.Add(documentKind + " № " + movement.DocumentNumber +
                    " · " + movement.Date +
                    "\nКількість: " + movement.Quantity.ToString("0.###") + " " + measure +
                    (movement.UnitPrice.HasValue
                        ? "\nЦіна: " + movement.UnitPrice.Value.ToString("0.00") + " грн"
                        : "\nЦіна не зазначена"));
            }
            if (remote.Count == 0)
                lines.Add("Історію актів та інших документів ще не отримано.");

            lines.Add("Довідкові рухи не змінюють локальний залишок.");
            Begin("asset-history", "Історія майна");
            var identity = Card();
            Text(identity, name, 19, true);
            Text(identity, "Підрозділ: " + unit, 13);
            Text(identity, "Одиниця: " + measure +
                (string.IsNullOrWhiteSpace(nsn) ? "" : " · NSN: " + nsn), 13);
            var historyCard = Card();
            foreach (var line in lines)
                Text(historyCard, line, 14);
            foreach (var item in local)
            {
                if (_archive.FindDocument(item.DocumentId) is not null)
                {
                    var id = item.DocumentId;
                    Button(historyCard, "Відкрити документ № " + item.Number,
                        () => ShowArchivedDocument(id));
                }
            }
            Button(_body, "← До майна", ShowAssets);
        }
        catch (Exception ex)
        {
            new AlertDialog.Builder(this)
                .SetTitle("Історія майна")
                .SetMessage("Помилка читання рухів: " + ex.GetType().Name)
                .SetPositiveButton("Закрити", (_, _) => { })
                .Show();
        }
    }

    private void ShowInboxContent()
    {
        // The header and filter card stay in place while searching.
        while (_body.ChildCount > 2)
            _body.RemoveViewAt(2);

        var visible = _messages.Where(mail =>
        {
            bool invoice = mail.Attachments.Any(a =>
                MavkaExchangeParser.TryInvoice(a.RawJson, out _));
            if (_mailFilter == "invoices" && !invoice) return false;
            if (_mailFilter == "other" && invoice) return false;
            if (_inboxQuery.Length == 0) return true;
            return mail.Subject.Contains(_inboxQuery, StringComparison.OrdinalIgnoreCase) ||
                   mail.DateText.Contains(_inboxQuery, StringComparison.OrdinalIgnoreCase) ||
                   mail.Body.Contains(_inboxQuery, StringComparison.OrdinalIgnoreCase) ||
                   mail.Attachments.Any(a => a.FileName.Contains(
                       _inboxQuery, StringComparison.OrdinalIgnoreCase));
        }).ToList();

        if (visible.Count == 0)
        {
            var empty = Card();
            Text(empty, _messages.Count == 0
                ? "Службових листів поки немає. Натисни «Оновити листи»."
                : "За цим пошуком листів не знайдено.", 15);
            return;
        }

        foreach (var mail in visible)
        {
            var invoice = mail.Attachments
                .Select(a => MavkaExchangeParser.TryInvoice(a.RawJson, out var item)
                    ? item : null).FirstOrDefault(x => x is not null);
            var card = Card();
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            card.AddView(row);

            var glyph = new TextView(this)
            {
                Text = invoice is null ? "✉" : "▤",
                TextSize = 25, Gravity = GravityFlags.Center
            };
            glyph.SetTextColor(invoice is null ?
                Color.Rgb(32, 91, 59) : Color.Rgb(191, 92, 42));
            glyph.Background = Rounded(invoice is null
                ? Color.Rgb(231, 242, 233) : Color.Rgb(252, 237, 224), 12);
            row.AddView(glyph, new LinearLayout.LayoutParams(Dp(48), Dp(48)));

            var data = new LinearLayout(this) { Orientation = Orientation.Vertical };
            var lp = new LinearLayout.LayoutParams(0, -2, 1);
            lp.SetMargins(Dp(11), 0, Dp(5), 0);
            row.AddView(data, lp);
            Text(data, invoice?.Number ?? mail.Subject, 14, true);
            Text(data, invoice is null ? "Службове повідомлення" :
                "Накладна · " + invoice.Recipient, 12);
            Text(data, mail.DateText, 11);
            if (!string.IsNullOrWhiteSpace(mail.ProcessingIssue))
                Text(data, mail.ProcessingIssue, 11);

            var arrow = Text(row, "›", 27, true);
            arrow.SetTextColor(Color.Rgb(48, 81, 58));
            row.Clickable = true;
            row.Click += (_, _) => ShowMessage(mail);
        }
    }

    private void ShowMessage(MavkaMailItem mail)
    {
        Begin("message", "Повідомлення");
        var card = Card();
        Text(card, mail.Subject, 19, true);
        Text(card, mail.DateText, 14);
        Text(card, mail.Body, 15);
        var status = Text(card, mail.ProcessingIssue ??
            "Лист із налаштованої адреси ГОЛОВНОЇ МАВКИ.", 13);
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
                        .SetMessage(attachment.Text.Length <= 8000
                            ? attachment.Text
                            : attachment.Text[..8000] +
                              "\n\nПерегляд скорочено. Структурований документ доступний у картці.")
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
            Text(info, "Дата: " + invoice.Date + "  ·  ID: " + invoice.DocumentId, 13);
            Text(info, "Позицій: " + invoice.Positions.Count, 14, true);
            // Render large invoices incrementally: creating hundreds of Android
            // views in one UI event causes visible stalls on modest phones.
            var positionList = new LinearLayout(this) { Orientation = Orientation.Vertical };
            info.AddView(positionList);
            int displayed = 0;
            var more = new Android.Widget.Button(this) { Text = "Показати ще 25 позицій" };
            info.AddView(more);
            void AppendNextPage()
            {
                int end = Math.Min(displayed + 25, invoice.Positions.Count);
                for (int index = displayed; index < end; index++)
                {
                    var position = invoice.Positions[index];
                    AddAssetRow(positionList, position.Name,
                        string.IsNullOrWhiteSpace(position.Nsn) ? "Номенклатура" :
                        "NSN: " + position.Nsn,
                        position.Quantity.ToString("0.###") + " " + position.Unit +
                        (position.UnitPrice.HasValue
                            ? "\n" + position.UnitPrice.Value.ToString("0.00") + " грн/од."
                            : ""));
                }
                displayed = end;
                more.Visibility = displayed >= invoice.Positions.Count
                    ? ViewStates.Gone : ViewStates.Visible;
                more.Text = "Показати ще 25 (показано " + displayed +
                    " із " + invoice.Positions.Count + ")";
            }
            more.Click += (_, _) => AppendNextPage();
            AppendNextPage();
            Text(info, "Перевір усі позиції перед підтвердженням отримання.", 12);
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
                        bool smtpAccepted = false;
                        try
                        {
                            // SQLite transaction commits receipt and its items atomically.
                            // The document ID is unique; retrying must never double inventory.
                            bool added = _ledger.AddConfirmedInvoice(invoice);
                            if (_archive.FindDocument(invoice.DocumentId) is not null)
                            {
                                _archive.MarkPosted(invoice.DocumentId);
                                _archive.MarkDeliveryQueued(invoice.DocumentId);
                            }
                            status.Text = added
                                ? "Майно зараховано локально. Надсилаємо підтвердження..."
                                : "Накладну вже зараховано. Повторно надсилаємо підтвердження...";

                            var password = _store.GetPassword() ??
                                throw new InvalidOperationException("Пароль Gmail відсутній.");
                            await RegistrationMailService.SendReceiptAsync(
                                _store.GetEmail(), password, _store.GetMainMavkaEmail(),
                                profile, invoice, _exchangeKeys);
                            smtpAccepted = true;
                            _ledger.MarkMailSent(invoice.DocumentId);
                            if (_archive.FindDocument(invoice.DocumentId) is not null)
                                _archive.MarkDeliverySent(invoice.DocumentId);
                            if (_screen == "message")
                                status.Text = "Майно зараховано в локальну базу. " +
                                    "Лист-підтвердження надіслано. " +
                                    "ГОЛОВНА МАВКА має обробити його окремо.";
                        }
                        catch (Exception ex)
                        {
                            if (_screen == "message")
                                status.Text = smtpAccepted
                                    ? "Gmail прийняв підтвердження, але локальне оновлення " +
                                      "статусу не завершилося (" + ex.GetType().Name +
                                      "). Не підтверджуй накладну повторно; перевір чергу."
                                    : "Не вдалося завершити відправлення (" +
                                      ex.GetType().Name +
                                      "). Проведення збережено, підтвердження очікує " +
                                      "повторної спроби. Не проводь накладну вдруге.";
                        }
                        finally { _busy = false; }
                    })
                    .Show();
            });
        }

        // Gmail cleanup is automatic only after verified durable import.
        // No manual bypass: a message with unread attachments stays in Inbox.
        Button(_body, _viewingArchivedDocument ? "← До архіву" : "← До вхідних",
            _viewingArchivedDocument ? ShowDocuments : ShowInbox);
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
                _store.GetEmail(), password, sender, _exchangeKeys,
                _documentPasswords, LoadUserProfile()?.ClientId);
            var report = await ArchiveAndCleanupAsync(password, sender);
            var syncReport = await SendPendingReceiptsAsync(password, sender);
            if (_screen is "home" or "inbox" or "assets")
                status.Text = $"Листів у вхідних: {_messages.Count}. " + report +
                    " " + syncReport;
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

    private async Task<string> SendPendingReceiptsAsync(string password, string sender)
    {
        var profile = LoadUserProfile();
        if (profile is null || !profile.IsMvo)
            return "Для синхронізації підтверджень потрібен профіль МВО.";

        int sent = 0, deferred = 0;
        // A bounded batch avoids a long chain of sequential SMTP timeouts.
        foreach (var id in _ledger.ListPendingReceiptIds(limit: 3))
        {
            try
            {
                // The ledger is the authoritative immutable record of confirmed
                // positions. Pre-archive receipts are therefore still resendable.
                var invoice = _ledger.LoadConfirmedInvoice(id);
                if (invoice is null ||
                    !string.Equals(invoice.Recipient, profile.Unit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    deferred++;
                    continue;
                }
                await RegistrationMailService.SendReceiptAsync(
                    _store.GetEmail(), password, sender, profile, invoice, _exchangeKeys);
                _ledger.MarkMailSent(id);
                if (_archive.FindDocument(id) is not null)
                    _archive.MarkDeliverySent(id);
                sent++;
            }
            catch (Exception)
            {
                // Keep mail_status=pending, preserving the item across reboots.
                // Server-side handling must remain idempotent by documentId.
                deferred++;
            }
        }
        return $"Надіслано підтверджень: {sent}; залишено в черзі: {deferred}.";
    }

    private async Task<string> ArchiveAndCleanupAsync(string password, string sender)
    {
        var profile = LoadUserProfile();
        int moved = 0, saved = 0, kept = 0;
        var remaining = new List<MavkaMailItem>();
        foreach (var mail in _messages)
        {
            // Never discard mail if any attachment is unread, unsupported or corrupt.
            if (!mail.AttachmentsComplete || mail.Attachments.Count == 0)
            {
                remaining.Add(mail with
                {
                    ProcessingIssue = mail.Attachments.Count == 0
                        ? "Немає придатного вкладення для збереження. Лист залишено."
                        : "Частина вкладень не прочитана або не підтримується. Лист залишено."
                });
                kept++;
                continue;
            }
            bool ready = true;
            string problem = "";
            foreach (var attachment in mail.Attachments)
            {
                if (attachment.InstalledKeyPackage) continue;
                if (profile is null ||
                    !ArchiveDocumentClassifier.TryClassify(
                        attachment.RawJson, profile.Unit, out var document) ||
                    document is null)
                {
                    ready = false;
                    problem = profile is null
                        ? "Налаштуй профіль перед імпортом."
                        : attachment.RawJson is null
                            ? "Вкладення не розшифровано або воно не є перевіреним JSON-документом."
                            : "Документ не підтримується або належить іншому підрозділу.";
                    break;
                }
                try
                {
                    _archive.SaveDocument(document.DocumentId, document.Kind,
                        document.Number, document.Unit, document.DocumentDate,
                        attachment.FileName, document.Json);
                    // Force a verified read-back even on duplicate delivery.
                    if (_archive.FindDocument(document.DocumentId) is null)
                        throw new InvalidOperationException("Archive read-back failed.");
                    saved++;
                }
                catch (Exception ex)
                {
                    ready = false;
                    problem = "Не вдалося зберегти документ: " + ex.GetType().Name +
                        ". Лист збережено у вхідних.";
                    break;
                }
            }
            if (!ready)
            {
                remaining.Add(mail with { ProcessingIssue = problem });
                kept++;
                continue;
            }
            try
            {
                _archive.MarkMailReady(mail.UidValidity, mail.MessageUid, sender);
                if (!_archive.IsMailReady(mail.UidValidity, mail.MessageUid, sender))
                    throw new InvalidOperationException("Mail journal not persisted.");
                await GmailConnectionService.MoveToTrashAsync(
                    _store.GetEmail(), password, sender, mail);
                _archive.MarkMailTrashed(mail.UidValidity, mail.MessageUid, sender);
                moved++;
            }
            catch (Exception ex)
            {
                // Both archived documents and the ready marker survive disconnection.
                // Re-fetching will not post anything or duplicate the archive.
                remaining.Add(mail with
                {
                    ProcessingIssue = "Документ збережено локально, але Gmail Trash " +
                        "недоступний (" + ex.GetType().Name +
                        "). Перенесення буде повторено під час оновлення."
                });
                kept++;
            }
        }
        _messages = remaining;
        return $"Архівовано/перевірено вкладень: {saved}; у кошик: {moved}; залишено: {kept}.";
    }

    private void ShowDocuments()
    {
        _viewingArchivedDocument = false;
        Begin("documents", "Історія документів");
        var profile = LoadUserProfile();
        if (profile is null)
        {
            Text(_body, "Для перегляду архіву заповни профіль.", 15);
            Button(_body, "Профіль", ShowProfile);
            return;
        }
        var card = Card();
        Text(card, "Локальний архів · " + profile.Unit, 17, true);
        Text(card, "Документи доступні без Gmail. Збереження не означає проведення.", 13);
        var search = ProfileInput(card, "Пошук за номером, типом, датою", "");
        var rows = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.AddView(rows);
        void Render()
        {
            rows.RemoveAllViews();
            try
            {
                var filter = (search.Text ?? "").Trim();
                var documents = _archive.ListSummaries(profile.Unit);
                var visible = documents.Where(d => filter.Length == 0 ||
                    d.Number.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    d.Kind.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    d.DocumentDate.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                Text(rows, $"Документів: {visible.Count} із {documents.Count}", 13);
                foreach (var entry in visible)
                {
                    var item = entry;
                    var tile = new LinearLayout(this) { Orientation = Orientation.Vertical };
                    rows.AddView(tile);
                    Text(tile, item.Kind + " № " + item.Number, 16, true);
                    Text(tile, item.DocumentDate + " · " + item.Unit +
                        " · " + item.Status, 13);
                    Text(tile, "Облік: " + item.PostingStatus +
                        " · Передавання: " + item.DeliveryStatus +
                        " · Синхронізація: " + item.SyncStatus, 12);
                    Button(tile, "Переглянути документ", () => ShowArchivedDocument(item.DocumentId));
                }
            }
            catch (Exception ex)
            {
                Text(rows, "Помилка читання архіву: " + ex.GetType().Name, 14);
            }
        }
        search.TextChanged += (_, _) => Render();
        Render();
    }

    private void ShowArchivedDocument(string documentId)
    {
        try
        {
            // The ledger and encrypted archive are intentionally separate SQLite
            // files. Heal the UI statuses after a crash between their commits.
            var ledgerStatus = _ledger.GetMailStatus(documentId);
            if (ledgerStatus is "pending" or "sent")
            {
                _archive.MarkPosted(documentId);
                if (ledgerStatus == "sent") _archive.MarkDeliverySent(documentId);
                else _archive.MarkDeliveryQueued(documentId);
            }
            var doc = _archive.FindDocument(documentId) ??
                throw new InvalidOperationException("Document not found.");
            var attachment = new MavkaTextAttachment(doc.FileName,
                MavkaJsonPreview.Format(doc.Json), doc.Json);
            // Reuse the existing invoice preview and confirmation flow, but with
            // no Gmail UID so the local-only copy can never be moved or deleted.
            var local = new MavkaMailItem(doc.Kind + " № " + doc.Number,
                doc.DocumentDate, "Збережено в локальному архіві · " + doc.Status,
                new[] { attachment }, 0, 0);
            _viewingArchivedDocument = true;
            ShowMessage(local);
            if (MavkaExchangeParser.TryInvoice(doc.Json, out var invoice) &&
                invoice is not null && _exportedFiles.IsSupported)
            {
                var exportCard = Card();
                Text(exportCard, "Захищений PDF", 17, true);
                Text(exportCard, "PDF формується з локальної копії накладної, " +
                    "шифрується паролем AES-256 та зберігається у " +
                    "Download/МаVКа файли. Незахищена копія не створюється.", 12);
                var exportStatus = Text(exportCard, "Файл ще не сформовано.", 13);
                Button(exportCard, "Зберегти PDF із паролем", () =>
                {
                    if (_busy) return;
                    var secret = new EditText(this)
                    {
                        Hint = "Пароль PDF (12–128 символів)",
                        InputType = InputTypes.ClassText | InputTypes.TextVariationPassword
                    };
                    secret.TransformationMethod =
                        Android.Text.Method.PasswordTransformationMethod.Instance;
                    new AlertDialog.Builder(this)
                        .SetTitle("Пароль для захищеного PDF")
                        .SetMessage("Пароль не зберігається у програмі. " +
                            "Без нього експортований PDF неможливо відкрити.")
                        .SetView(secret)
                        .SetNegativeButton("Скасувати", (_, _) => { })
                        .SetPositiveButton("Створити", async (_, _) =>
                        {
                            var password = secret.Text?.ToString() ?? "";
                            if (password.Length is < 12 or > 128)
                            {
                                exportStatus.Text = "Пароль повинен мати 12–128 символів.";
                                return;
                            }
                            if (_busy) return;
                            _busy = true;
                            byte[]? bytes = null;
                            try
                            {
                                exportStatus.Text = "Шифруємо PDF...";
                                bytes = await Task.Run(() =>
                                    ProtectedPdfBuilder.CreateInvoice(invoice, password));
                                var cleanNumber = new string(invoice.Number.Where(ch =>
                                    char.IsLetterOrDigit(ch) || ch is '-' or '_')
                                    .Take(55).ToArray());
                                if (string.IsNullOrWhiteSpace(cleanNumber))
                                    cleanNumber = "document";
                                var name = "Накладна-" + cleanNumber + "-" +
                                    DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".pdf";
                                var protectedBytes = bytes;
                                await Task.Run(() =>
                                    _exportedFiles.SaveProtectedPdf(protectedBytes, name));
                                if (_screen == "message")
                                    exportStatus.Text = "Захищений PDF збережено у МаVКа файли.";
                            }
                            catch (Exception ex)
                            {
                                if (_screen == "message")
                                    exportStatus.Text = "Не вдалося експортувати PDF: " +
                                        ex.GetType().Name +
                                        ". Незахищений файл не збережено.";
                            }
                            finally
                            {
                                if (bytes is not null)
                                    System.Security.Cryptography.CryptographicOperations
                                        .ZeroMemory(bytes);
                                _busy = false;
                            }
                        }).Show();
                });
            }
            var statuses = Card();
            Text(statuses, "Історія статусів документа", 16, true);
            foreach (var change in _archive.ListStatusHistory(documentId))
                Text(statuses, change, 12);
        }
        catch (Exception ex)
        {
            new AlertDialog.Builder(this).SetTitle("Помилка архіву")
                .SetMessage(ex.GetType().Name)
                .SetPositiveButton("Закрити", (_, _) => { }).Show();
        }
    }

    private void ShowFiles()
    {
        Begin("files", MavkaExportPolicy.FolderTitle);
        var header = Card();
        Text(header, "Експортовані PDF та Excel", 18, true);
        Text(header, "Тільки копії з папки Download/МаVКа файли. " +
            "Видалення цих копій не видаляє накладні, залишки, " +
            "підтвердження чи історію з внутрішньої бази Мавки.", 13);

        if (!_exportedFiles.IsSupported)
        {
            Text(header, "На цій версії Android для папки Downloads необхідний " +
                "окремий режим Storage Access Framework. Керування поки недоступне.", 14);
            Button(_body, "← На головну", ShowHome);
            return;
        }

        var search = ProfileInput(header, "Пошук файлів PDF / XLSX", "");
        var output = new LinearLayout(this) { Orientation = Orientation.Vertical };
        header.AddView(output);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        bool nameSorting = false;

        void RenderList()
        {
            output.RemoveAllViews();
            IReadOnlyList<MavkaExportFile> files;
            try { files = _exportedFiles.ListFiles(); }
            catch (Exception ex)
            {
                Text(output, "Не вдалося прочитати папку: " + ex.GetType().Name, 14);
                return;
            }

            var query = (search.Text ?? "").Trim();
            var matches = files.Where(file =>
                query.Length == 0 ||
                file.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            var visible = nameSorting
                ? matches.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
                : matches.OrderByDescending(x => x.ModifiedAt).ToList();

            Text(output, "Знайдено: " + visible.Count + " із " + files.Count, 14);
            if (visible.Count == 0)
                Text(output, "Експортованих PDF/XLSX немає або Android не надав " +
                    "доступу до цих копій. Облікові документи зберігаються окремо.", 13);
            foreach (var file in visible.Take(100))
            {
                var item = file;
                var tile = new LinearLayout(this) { Orientation = Orientation.Vertical };
                output.AddView(tile);
                var marker = new CheckBox(this) { Text = item.Name };
                marker.Checked = selected.Contains(item.Uri.ToString()!);
                marker.CheckedChange += (_, _) =>
                {
                    var id = item.Uri.ToString()!;
                    if (marker.Checked) selected.Add(id);
                    else selected.Remove(id);
                };
                tile.AddView(marker);
                Text(tile, (item.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                    ? "PDF" : "Excel") + " · " +
                    item.ModifiedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") +
                    " · " + (item.Size / 1024d).ToString("0.#") + " КБ", 12);
                Button(tile, "Відкрити", () =>
                {
                    try { _exportedFiles.OpenFile(item); }
                    catch (Exception ex)
                    {
                        Toast.MakeText(this, "Немає програми для відкриття: " +
                            ex.GetType().Name, ToastLength.Long)?.Show();
                    }
                });
                Button(tile, "Поділитися", () =>
                {
                    try { _exportedFiles.ShareFile(item); }
                    catch (Exception ex)
                    {
                        Toast.MakeText(this, "Не вдалося поділитися: " +
                            ex.GetType().Name, ToastLength.Long)?.Show();
                    }
                });
            }
            if (visible.Count > 100)
                Text(output, "Відображено перші 100 файлів. Скористайся пошуком.", 13);
        }

        search.TextChanged += (_, _) => RenderList();
        Button(header, "Сортувати: " + (nameSorting ? "назва" : "дата") +
            " (перемкнути)", () =>
        {
            nameSorting = !nameSorting;
            RenderList();
        });
        Button(header, "Оновити список", RenderList);

        void ConfirmDeletion(bool all)
        {
            if (_busy) return;
            IReadOnlyList<MavkaExportFile> candidates;
            try
            {
                var current = _exportedFiles.ListFiles();
                candidates = all
                    ? current
                    : current.Where(item => selected.Contains(item.Uri.ToString()!)).ToList();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Помилка читання: " + ex.GetType().Name,
                    ToastLength.Long)?.Show();
                return;
            }
            if (candidates.Count == 0)
            {
                Toast.MakeText(this, "Файлів для видалення немає.",
                    ToastLength.Short)?.Show();
                return;
            }
            new AlertDialog.Builder(this)
                .SetTitle(all ? "Очистити папку МаVКа файли?" : "Видалити вибрані копії?")
                .SetMessage("Кількість файлів: " + candidates.Count +
                    "\n\nБудуть видалені лише PDF/XLSX-копії з папки експорту. " +
                    "Внутрішня база обліку й історія документів не зміняться.")
                .SetNegativeButton("Скасувати", (_, _) => { })
                .SetPositiveButton("Видалити", async (_, _) =>
                {
                    if (_busy) return;
                    _busy = true;
                    int deleted = 0;
                    string? error = null;
                    try
                    {
                        await Task.Run(() =>
                        {
                            foreach (var item in candidates)
                            {
                                try { deleted += _exportedFiles.DeleteFile(item); }
                                catch (Exception ex)
                                {
                                    error = ex.GetType().Name;
                                    break;
                                }
                            }
                        });
                    }
                    finally { _busy = false; }
                    if (_screen == "files") ShowFiles();
                    Toast.MakeText(this, "Видалено: " + deleted +
                        (error is null ? "" : "; перервано: " + error),
                        ToastLength.Long)?.Show();
                }).Show();
        }

        Button(header, "Видалити вибрані", () => ConfirmDeletion(false));
        Button(header, "Очистити папку", () => ConfirmDeletion(true));
        RenderList();
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
        Text(card, "Ідентифікатор користувача: " +
            (current?.ClientId ?? "з’явиться після першого збереження профілю"), 13);
        if (current is not null)
            Button(card, "Копіювати ID для ГОЛОВНОЇ МАВКИ", () =>
            {
                var clipboard = (Android.Content.ClipboardManager?)
                    GetSystemService(Android.Content.Context.ClipboardService);
                if (clipboard is not null)
                    clipboard.PrimaryClip = Android.Content.ClipData.NewPlainText(
                        "Mavka Client ID", current.ClientId);
            });

        Text(card, "Підрозділ", 15, true);
        var unit = ProfileInput(card, "Назва підрозділу", current?.Unit);
        Text(card, "ПІБ", 15, true);
        var fullName = ProfileInput(card, "Прізвище Ім’я По батькові", current?.FullName);
        Text(card, "Військове звання", 15, true);
        var rank = ProfileInput(card, "Звання", current?.Rank);
        Text(card, "Посада", 15, true);
        var position = ProfileInput(card, "Посада користувача", current?.Position);
        Text(card, "Номер телефону", 15, true);
        var phone = ProfileInput(card, "+380XXXXXXXXX або міжнародний номер", current?.PhoneNumber);
        phone.InputType = InputTypes.ClassPhone;
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
                if (current is not null &&
                    !string.Equals(current.Unit, u, StringComparison.OrdinalIgnoreCase) &&
                    (_ledger.HasReceipts(current.Unit) ||
                     _archive.CountDocuments(current.Unit) > 0))
                {
                    status.Text = "Зміна підрозділу заблокована: наявні локальні документи " +
                        "або облікові записи. Потрібна окрема звірка, аби не змішати майно.";
                    return null;
                }
                var stored = _profileStore.Save(u, name, r, p, yes.Checked,
                    phone.Text?.ToString());
                status.Text = "Профіль успішно збережено на телефоні.";
                return stored;
            }
            catch (Exception ex)
            {
                status.Text = ex is ArgumentException ? ex.Message :
                    "Помилка збереження профілю: " + ex.GetType().Name;
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
                            sender, password, recipient, profile, _exchangeKeys);
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

    private void ShowExchangeKey()
    {
        Begin("exchange-key", "Ключі обміну");
        var card = Card();
        Text(card, "Ключі отримуються автоматично через Gmail", 18, true);
        Text(card, "ГОЛОВНА МАВКА надсилає захищений паролем файл MAVKA_KEY_PACKAGE_V1 " +
            "із поточним і майбутнім ключами. Мобільна Мавка відкриває його з паролем, " +
            "збереженим у налаштуваннях, та захищено зберігає обидва ключі.", 14);

        var status = Text(card, _exchangeKeys.HasKeyPair
            ? "Покоління: " + _exchangeKeys.Generation +
              "\nПоточний ключ: " + _exchangeKeys.CurrentKeyId +
              "\nМайбутній ключ: " + _exchangeKeys.FutureKeyId
            : "Немає автоматично встановленої пари ключів.", 15);

        Button(card, "Оновити ключі з Gmail", async () =>
        {
            if (!_documentPasswords.IsConfigured)
            {
                status.Text = "Спочатку один раз збережи пароль документів у налаштуваннях.";
                return;
            }
            if (LoadUserProfile() is null)
            {
                status.Text = "Спочатку заповни профіль користувача.";
                return;
            }
            await RefreshMessagesAsync(status);
            if (_screen == "exchange-key")
                status.Text = _exchangeKeys.HasKeyPair
                    ? "Ключі встановлено. Покоління: " + _exchangeKeys.Generation +
                      "\nПоточний: " + _exchangeKeys.CurrentKeyId +
                      "\nМайбутній: " + _exchangeKeys.FutureKeyId
                    : "Ключів не знайдено. Перевір пароль, Gmail і адресата.";
        });
        Text(card, "Шифрування вихідних листів активується автоматично після отримання ключів. " +
            "Під час початкової реєстрації, до видачі ключів, лист ще не зашифрований.", 13);
        Text(card, "Тестовий пароль 090699 слабкий. Лише для випробувань без реальних службових даних.", 13);

        Button(card, "Видалити обидва ключі", () =>
            new AlertDialog.Builder(this)
                .SetTitle("Видалити поточний і майбутній ключі?")
                .SetMessage("Зашифровані старими ключами документи можуть стати недоступними. " +
                    "Це не видаляє локальну базу майна.")
                .SetNegativeButton("Скасувати", (_, _) => { })
                .SetPositiveButton("Видалити", (_, _) =>
                {
                    try
                    {
                        _exchangeKeys.Clear();
                        _messages = Array.Empty<MavkaMailItem>();
                        status.Text = "Ключі видалено. Для повторного отримання потрібна нова реєстрація.";
                    }
                    catch (Exception ex) { status.Text = "Помилка: " + ex.GetType().Name; }
                })
                .Show());
    }

    private void ShowDocumentPassword()
    {
        Begin("document-password", "Пароль документів");
        var card = Card();

        Text(card, "Пароль для файлів з ГОЛОВНОЇ МАВКИ", 18, true);
        Text(card, "Введи пароль один раз. Надалі програма використовуватиме його " +
            "автоматично під час відкриття захищених файлів. " +
            "Повторно вводити потрібно лише після зміни пароля.", 14);
        Text(card, _documentPasswords.IsConfigured
            ? "Пароль уже захищено збережений на телефоні."
            : "Початковий тестовий пароль: 090699. Натисни «Зберегти», щоб увімкнути.", 14);

        var input = new EditText(this)
        {
            Hint = "Пароль документів",
            Text = _documentPasswords.IsConfigured ? "" : "090699",
            InputType = InputTypes.ClassText | InputTypes.TextVariationPassword
        };
        ConfigureKeyboard(input, password: true);
        input.TransformationMethod = Android.Text.Method.PasswordTransformationMethod.Instance;
        card.AddView(input);
        var status = Text(card, "", 14);
        Button(card, "Зберегти / змінити пароль", () =>
        {
            try
            {
                _documentPasswords.Save(input.Text ?? "");
                input.Text = "";
                _messages = Array.Empty<MavkaMailItem>();
                status.Text = "Пароль збережено. Наступні захищені файли читатимуться автоматично.";
            }
            catch (ArgumentException)
            {
                status.Text = "Пароль має містити від 6 до 128 символів.";
            }
            catch (Exception ex)
            {
                status.Text = "Помилка захищеного збереження: " + ex.GetType().Name;
            }
        });
        Text(card, "Зміна пароля впливає на нові файли. Якщо ГОЛОВНА МАВКА " +
            "ще використовує попередній пароль, нові пакети ключів не відкриються.", 13);
    }

    private void ShowSettings()
    {
        Begin("settings", "Налаштування");
        var card = Card();
        Text(card, "Пошта Gmail", 20, true);
        Text(card, _store.HasCredentials ? _store.GetEmail() : "Не підключено", 16);
        Button(card, "Пошта Gmail →", ShowMail);
        Button(card, "Ключ обміну →", ShowExchangeKey);
        Button(card, "Пароль документів →", ShowDocumentPassword);
        card = Card();
        Text(card, "Адреса ГОЛОВНОЇ МАВКИ", 18, true);
        Text(card, _store.GetMainMavkaEmail(), 16);
        var diagnostics = Card();
        Text(diagnostics, "Діагностика", 17, true);
        Button(diagnostics, "Галерея іконок (демо)", ShowIconGallery);
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
        if (_screen is "mail" or "exchange-key" or "document-password") { ShowSettings(); return; }
        if (_screen == "message") { if (_viewingArchivedDocument) ShowDocuments(); else ShowInbox(); return; }
        if (_screen == "asset-history") { ShowAssets(); return; }
        if (_screen == "reconciliation") { ShowAssets(); return; }
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
        field.SetPadding(Dp(12), Dp(7), Dp(12), Dp(7));
        field.Background = Rounded(Color.Rgb(251, 250, 245), 12, Color.Rgb(219, 227, 218));
    }

    private GradientDrawable Rounded(Color fill, float radius, Color? border = null)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        drawable.SetCornerRadius(Dp(radius));
        if (border.HasValue)
            drawable.SetStroke(Dp(1), border.Value);
        return drawable;
    }

    private LinearLayout Card()
    {
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.SetPadding(Dp(15), Dp(13), Dp(15), Dp(13));
        card.Background = Rounded(Color.White, 18, Color.Rgb(238, 235, 227));
        card.Elevation = Dp(2);
        var lp = new LinearLayout.LayoutParams(-1, -2);
        lp.SetMargins(Dp(2), Dp(7), Dp(2), Dp(8));
        _body.AddView(card, lp);
        return card;
    }

    private TextView Text(LinearLayout parent, string caption, float size, bool bold = false)
    {
        var view = new TextView(this) { Text = caption, TextSize = size };
        view.SetTextColor(bold ? Color.Rgb(20, 47, 33) : Color.Rgb(92, 100, 94));
        if (bold) view.SetTypeface(null, TypefaceStyle.Bold);
        view.SetPadding(0, Dp(3), 0, Dp(7));
        parent.AddView(view);
        return view;
    }

    private void Button(LinearLayout parent, string text, Action callback)
    {
        var view = new Button(this) { Text = text, TextSize = 14 };
        view.SetAllCaps(false);
        var isSecondary = text.StartsWith("←") || text.StartsWith("Закрити") ||
                          text.StartsWith("Забути") || text.StartsWith("Видалити");
        view.SetTextColor(isSecondary ? Color.Rgb(21, 69, 44) : Color.White);
        view.Background = Rounded(isSecondary ? Color.Rgb(230, 240, 231) :
            Color.Rgb(9, 83, 50), 13);
        var layout = new LinearLayout.LayoutParams(-1, Dp(47));
        layout.SetMargins(0, Dp(4), 0, Dp(5));
        parent.AddView(view, layout);
        view.Click += (_, _) => callback();
    }

    private TextView Nav(string title, Action action)
    {
        // TextView has no platform Button minWidth/minHeight: all four tabs
        // fit on small screens and Android large-font configurations.
        var view = new TextView(this)
        {
            Text = title,
            TextSize = 12,
            Gravity = GravityFlags.Center
        };
        view.SetMaxLines(2);
        view.SetPadding(Dp(2), Dp(4), Dp(2), Dp(4));
        var layout = new LinearLayout.LayoutParams(0, Dp(59), 1f);
        layout.SetMargins(Dp(2), 0, Dp(2), 0);
        view.LayoutParameters = layout;
        view.Clickable = true;
        view.Click += (_, _) => action();
        return view;
    }

    private void StyleNav(TextView item, bool selected)
    {
        item.SetTextColor(selected ? Color.Rgb(11, 78, 47) : Color.Rgb(105, 112, 106));
        item.SetTypeface(null, selected ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        item.Background = Rounded(selected ? Color.Rgb(230, 241, 231) : Color.White, 13);
    }

    private void AddAssetRow(LinearLayout parent, string name, string description,
        string quantity, Action? showHistory = null)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(9), Dp(9), Dp(8), Dp(9));
        row.Background = Rounded(Color.Rgb(250, 249, 245), 14,
            Color.Rgb(238, 235, 227));
        var layout = new LinearLayout.LayoutParams(-1, -2);
        layout.SetMargins(0, Dp(4), 0, Dp(4));
        parent.AddView(row, layout);

        row.AddView(_assetIcons.Create(name, 57));
        var details = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var detailLayout = new LinearLayout.LayoutParams(0, -2, 1);
        detailLayout.SetMargins(Dp(9), 0, Dp(6), 0);
        row.AddView(details, detailLayout);
        var primary = Text(details, name, 13, true);
        primary.SetPadding(0, 0, 0, Dp(2));
        var secondary = Text(details, description, 11);
        secondary.SetPadding(0, 0, 0, 0);

        var amount = new TextView(this)
        {
            Text = quantity, TextSize = 13,
            Gravity = GravityFlags.CenterVertical | GravityFlags.Right
        };
        amount.SetTextColor(Color.Rgb(15, 47, 32));
        amount.SetTypeface(null, TypefaceStyle.Bold);
        row.AddView(amount, new LinearLayout.LayoutParams(Dp(82), -2));

        if (showHistory is not null)
        {
            row.Clickable = true;
            row.Click += (_, _) => showHistory();
            row.LongClickable = true;
            row.LongClick += (_, _) => showHistory();
        }
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
