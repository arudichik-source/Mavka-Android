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

[Activity(
#if MAVKA_BOOTSTRAP_QA
    Label = "Mavka Registration Test",
#elif MAVKA_RELAY_QA
    Label = "Mavka Relay QA",
#else
    Label = "Мавка Mobile",
#endif
    MainLauncher = true, Exported = true,
    ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation |
                           Android.Content.PM.ConfigChanges.ScreenSize |
                           Android.Content.PM.ConfigChanges.KeyboardHidden)]
public partial class MainActivity : Activity
{
    private LinearLayout _body = null!;
    private MavkaThemePalette _theme = null!;
    private GmailCredentialsStore _store = null!;
    private QrPairingStore _qrPairing = null!;
    private UserProfileStore _profileStore = null!;
    private PropertyLedgerStore _ledger = null!;
    private DocumentArchiveStore _archive = null!;
    private MavkaNoticeStore _noticeStore = null!;
    private MavkaExportFileStore _exportedFiles = null!;
    private PrivateAttachmentStore _privateFiles = null!;
    private string? _pendingPrivateExportId;
    private GmailInboxCache _inboxCache = null!;
    private ScrollView _scroll = null!;
    private bool _viewingArchivedDocument;
    private DateOnly _assetsAsOf = DateOnly.FromDateTime(DateTime.Today);
    private AssetIconProvider _assetIcons = null!;
    private ExchangeKeyStore _exchangeKeys = null!;
    private DocumentPasswordStore _documentPasswords = null!;
    private FrameLayout _drawerOverlay = null!;
    private ScrollView _leftDrawer = null!, _rightDrawer = null!;
    private string _screen = "home";
    private const int PickDocumentRequest = 1526;
    private const int ExportBackupRequest = 1593;
    private const int ImportBackupRequest = 1594;
    private byte[]? _pendingEncryptedBackup;
    private string? _pendingBackupPassword;
    private const int QrImagePickRequest = 1591;
    private const int QrCameraCaptureRequest = 1592;
    private const int ExportPrivateCopyRequest = 1527;
    private const int PickEncryptAndSendRequest = 1528;
    private string _inboxQuery = "";
    private string _documentQuery = "";
    private int _documentPage;
    private string _mailFilter = "all";
    private bool _busy;
    private IReadOnlyList<MavkaMailItem> _messages = Array.Empty<MavkaMailItem>();

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        _theme = MavkaThemePalette.Load(this);
        // Keep the editable fields visible above the soft keyboard.
        Window?.SetSoftInputMode(SoftInput.AdjustResize | SoftInput.StateAlwaysHidden);
        Window?.SetStatusBarColor(_theme.Header);
        Window?.SetNavigationBarColor(_theme.Surface);
        _store = new GmailCredentialsStore(this);
        _qrPairing = new QrPairingStore(this);
        _profileStore = new UserProfileStore(this);
        _ledger = new PropertyLedgerStore(this);
        _archive = new DocumentArchiveStore(this);
        _noticeStore = new MavkaNoticeStore(this);
        MavkaHomeWidget.Refresh(this);
        MavkaNoticeAlerts.EnsureChannels(this);
        MavkaNoticeAlerts.RescheduleOnStart(this, _noticeStore);
        _exportedFiles = new MavkaExportFileStore(this);
        _privateFiles = new PrivateAttachmentStore(this);
        _assetIcons = new AssetIconProvider(this);
        _exchangeKeys = new ExchangeKeyStore(this);
        _documentPasswords = new DocumentPasswordStore(this);
        _inboxCache = new GmailInboxCache(this);
        if (_store.HasCredentials)
            _messages = _inboxCache.Load(_store.GetEmail(), _store.GetMainMavkaEmail());
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(_theme.Surface);
        root.SetOnApplyWindowInsetsListener(new SafeInsets(Dp(14), Dp(10), Dp(10)));

        var topBar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        topBar.SetGravity(GravityFlags.CenterVertical);
        topBar.SetPadding(Dp(10), 0, Dp(8), 0);
        topBar.SetBackgroundColor(_theme.Header);
        var appMark = new ImageView(this);
        appMark.SetImageResource(Resource.Drawable.mavka_app_icon);
        appMark.SetScaleType(ImageView.ScaleType.FitCenter);
        var logoLayout = new LinearLayout.LayoutParams(Dp(40), Dp(40));
        logoLayout.SetMargins(0, 0, Dp(8), 0);
        topBar.AddView(appMark, logoLayout);
        appMark.ContentDescription = "Відкрити ліве меню навігації";
        appMark.Clickable = true;
        appMark.Click += (_, _) => ShowDrawer(left: true);
        var brand = new TextView(this) { Text = "Мавка Mobile", TextSize = 20 };
        brand.SetSingleLine(true);
        brand.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        brand.SetTextColor(Color.White);
        brand.SetTypeface(null, TypefaceStyle.Bold);
        topBar.AddView(brand, new LinearLayout.LayoutParams(0, Dp(52), 1));
        brand.Gravity = GravityFlags.CenterVertical;
        var hamburger = new Android.Widget.Button(this) { Text = "☰", TextSize = 23 };
        hamburger.SetSingleLine(true);
        hamburger.SetTextColor(Color.White);
        hamburger.Background = Rounded(_theme.HeaderButton, 12);
        hamburger.SetPadding(0, 0, 0, 0);
        topBar.AddView(hamburger, new LinearLayout.LayoutParams(Dp(58), Dp(52)));
        hamburger.ContentDescription = "Відкрити праве меню команд";
        hamburger.Click += (_, _) => ShowDrawer(left: false);
        root.AddView(topBar);

        var scroll = new ScrollView(this) { FillViewport = true };
        _scroll = scroll;
        scroll.LayoutParameters = new LinearLayout.LayoutParams(-1, 0, 1);
        _body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _body.SetPadding(0, Dp(8), 0, Dp(12));
        scroll.AddView(_body);
        root.AddView(scroll);

        var host = new FrameLayout(this);
        host.AddView(root, new FrameLayout.LayoutParams(-1, -1));

        _drawerOverlay = new FrameLayout(this);
        _drawerOverlay.SetBackgroundColor(Color.Argb(115, 0, 0, 0));
        _drawerOverlay.Visibility = ViewStates.Gone;
        _drawerOverlay.Click += (_, _) => CloseDrawer();

        var leftPanel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        leftPanel.Clickable = true;
        leftPanel.SetBackgroundColor(_theme.Surface);
        leftPanel.SetPadding(Dp(14), Dp(50), Dp(14), Dp(30));
        Text(leftPanel, "Розділи Мавки", 21, true);
        Button(leftPanel, "Головна", () => { CloseDrawer(); ShowHome(); });
        Button(leftPanel, "Вхідні", () => { CloseDrawer(); ShowInbox(); });
        Button(leftPanel, "Сповіщення та нагадування", () => { CloseDrawer(); ShowNotices(); });
        Button(leftPanel, "Майно підрозділу", () => { CloseDrawer(); ShowAssets(); });
        Button(leftPanel, "Історія документів", () => { CloseDrawer(); ShowDocuments(); });
        Button(leftPanel, "Бібліотека документів", () => { CloseDrawer(); ShowFiles(); });
        _leftDrawer = new ScrollView(this) { FillViewport = true };
        _leftDrawer.SetBackgroundColor(_theme.Surface);
        _leftDrawer.AddView(leftPanel);
        _drawerOverlay.AddView(_leftDrawer,
            new FrameLayout.LayoutParams(Dp(292), -1, GravityFlags.Left));

        var rightPanel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        rightPanel.Clickable = true;
        rightPanel.SetBackgroundColor(_theme.Surface);
        rightPanel.SetPadding(Dp(14), Dp(50), Dp(14), Dp(30));
        Text(rightPanel, "Команди і налаштування", 21, true);
        Button(rightPanel, "Синхронізація Gmail", () => { CloseDrawer(); ShowSynchronization(); });
        Button(rightPanel, "Профіль користувача", () => { CloseDrawer(); ShowProfile(); });
        Button(rightPanel, "Налаштування", () => { CloseDrawer(); ShowSettings(); });
        Button(rightPanel, "Ключ обміну", () => { CloseDrawer(); try{ShowExchangeKey();}catch(Exception ex){ShowSafeFailure("Ключ обміну",ex);} });
        Button(rightPanel, "Пароль документів", () => { CloseDrawer(); ShowDocumentPassword(); });
        _rightDrawer = new ScrollView(this) { FillViewport = true };
        _rightDrawer.SetBackgroundColor(_theme.Surface);
        _rightDrawer.AddView(rightPanel);
        _drawerOverlay.AddView(_rightDrawer,
            new FrameLayout.LayoutParams(Dp(292), -1, GravityFlags.Right));
        host.AddView(_drawerOverlay, new FrameLayout.LayoutParams(-1, -1));

        SetContentView(host);
        if (Intent?.GetBooleanExtra("show_mavka_notices", false) == true)
            ShowNotices();
        else if (Intent?.GetStringExtra("mavka_widget_destination") is { } widgetDestination)
            OpenWidgetDestination(widgetDestination);
        else if (state?.GetString("mavka_restored_screen") == "settings")
            ShowSettings();
        else ShowHome();
        _ = RecoverMirrorAsync();
        // Orientation changes are handled by this Activity, not a new OnCreate.
        // Do not replay welcome after restoring an existing Activity.
        if (state is null)
            StartWelcomeSplash(host);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.GetBooleanExtra("show_mavka_notices", false) == true)
            ShowNotices();
        else if (intent?.GetStringExtra("mavka_widget_destination") is { } destination)
            OpenWidgetDestination(destination);
    }

    private void OpenWidgetDestination(string destination)
    {
        switch (destination)
        {
            case "library": ShowFiles(); break;
            case "notices": ShowNotices(); break;
            case "assets": ShowAssets(); break;
            case "inbox": ShowInbox(); break;
            default: ShowHome(); break;
        }
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutString("mavka_restored_screen", _screen);
        base.OnSaveInstanceState(outState);
    }

    private async void StartWelcomeSplash(FrameLayout host)
    {
        // Approved artwork is bundled inside the APK; no download at startup.
        var welcome = new FrameLayout(this);
        welcome.SetBackgroundColor(_theme.Header);

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
        _mirrorReadGeneration++;
        _screen = screen;
        Title = "Мавка Mobile — " + title;
        _body.RemoveAllViews();
        // Every new screen starts at its own top, not at the previous scroll offset.
        _scroll.ScrollTo(0, 0);
        _scroll.Post(() => { if (_screen == screen) _scroll.ScrollTo(0, 0); });
        Text(_body, title, 24, true);
    }

    private void ShowHome()
    {
        Begin("home", "Головна");
        var overview = Card();
        Text(overview, "Стан синхронізації", 18, true);
        Text(overview, _store.HasCredentials
            ? "Gmail підключено" : "Пошта Gmail не налаштована", 15);
        Text(overview, "Листів у локальному перегляді: " + _messages.Count, 14);
        Text(overview, new RelayIdentity(this).Enabled ? "Cloudflare Relay активовано" : "Cloudflare Relay не підключено", 14);
        var activeProfile = LoadUserProfile();
        if (activeProfile is not null)
        {
            try
            {
                var unread = _noticeStore.UnreadCount(activeProfile.Unit);
                Text(overview, "Непрочитаних службових сповіщень: " + unread, 14);
                Button(overview, "Сповіщення та нагадування", ShowNotices);
            }
            catch { Text(overview, "Сповіщення поки недоступні.", 13); }
        }
        var syncStatus=Text(overview,"Оновлення виконується лише за вашим запитом.",13);
        Button(overview,"Оновити пошту",async()=>await RefreshMessagesAsync(syncStatus));
        Button(overview,"Оновити сервер",async()=>await RefreshRelayAsync(syncStatus));
        var profile = LoadUserProfile();
        var card = Card();
        Text(card, "Майно та документи", 19, true);
        Text(card, "Підрозділ: " + (profile?.Unit ?? "не визначено"), 15);
        if (profile is not null)
        {
            try
            {
                Text(card, "Документів у захищеному архіві: " +
                    _archive.CountDocuments(profile.Unit), 14);
                Text(card, "Підтверджень у черзі: " +
                    _ledger.ListPendingReceiptIds().Count, 14);
            }
            catch (Exception ex)
            {
                Text(card, "Не вдалося прочитати стан локальних даних: " +
                    ExplainOperationError(ex), 13);
            }
        }
        Button(card,"Майно",ShowAssets);
        Button(card,"Документи",ShowDocuments);
        Button(card, "Бібліотека файлів", ShowFiles);
    }

    private void ShowSynchronization()
    {
        Begin("sync", "Синхронізація Gmail");
        var card = Card();
        Text(card, "Підключення до Google", 19, true);
        var status = Text(card, _store.HasCredentials
            ? "Gmail налаштовано. Зчитуються тільки листи ГОЛОВНОЇ МАВКИ."
            : "Gmail не підключено.", 14);
        Button(card, "Оновити повідомлення", async () =>
            await RefreshMessagesAsync(status));
        Text(card, "Оновлення пошти автоматично повторює відправлення " +
            "підтверджень із черги. Окрема кнопка не потрібна.", 12);
        Text(card, "Повідомлення, що вже надійно оброблені, " +
            "не потрібно завантажувати повторно.", 12);
        var relayCard = Card();
        Text(relayCard, "Додатковий канал Cloudflare", 18, true);
        Text(relayCard, "Захищений обмін із ГОЛОВНОЮ МАВКОЮ. Наявний Gmail зберігається.", 13);
        Button(relayCard, "Cloudflare Relay →", ShowRelay);
        var journal = Card();
        Text(journal, "Журнал синхронізації", 18, true);
        Text(journal, "Лише стани операцій і час; вміст документів, " +
            "паролі та ключі тут не записуються.", 12);
        Button(journal, "Показати останні події", () =>
        {
            try
            {
                var events = _archive.ListSyncEvents(50);
                var details = events.Count == 0
                    ? "Подій поки немає."
                    : string.Join("\n", events.Select(e =>
                        e.Utc + " · " + e.State + " · " + e.Detail));
                new AlertDialog.Builder(this)
                    .SetTitle("Синхронізація — останні 50 подій")
                    .SetMessage(details)
                    .SetPositiveButton("Закрити", (_, _) => { }).Show();
            }
            catch (Exception)
            {
                Toast.MakeText(this, "Не вдалося відкрити журнал.",
                    ToastLength.Long)?.Show();
            }
        });
    }

        private void ShowInbox()
    {
        _viewingArchivedDocument = false;
        Begin("inbox", "Вхідні");
        var card = Card();
        Text(card, "Листи ГОЛОВНОЇ МАВКИ", 17, true);
        var mainSender = _store.GetMainMavkaEmail();
        if (string.IsNullOrWhiteSpace(mainSender))
        {
            Text(card, "Службову адресу ГОЛОВНОЇ МАВКИ ще не налаштовано. " +
                "Вкажи її самостійно перед синхронізацією.", 13);
            Button(card, "Налаштувати службову пошту", ShowMail);
        }
        else
            Text(card, "Лише від " + mainSender, 12);

        var search = new EditText(this)
        {
            Hint = "Пошук за номером, датою або типом",
            Text = _inboxQuery,
            TextSize = 14
        };
        search.SetSingleLine(true);
        search.SetPadding(Dp(12), Dp(7), Dp(12), Dp(7));
        search.Background = Rounded(_theme.InputSearch, 12,
            _theme.InputSearchOutline);
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
            filter.SetTextColor(value == _mailFilter ? Color.White : _theme.FilterText);
            filter.Background = Rounded(value == _mailFilter
                ? _theme.FilterAction : _theme.SoftFilter, 11);
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
                    child.SetTextColor(active ? Color.White : _theme.FilterText);
                    child.Background = Rounded(active ? _theme.FilterAction :
                        _theme.SoftFilter, 11);
                }
                ShowInboxContent();
            };
        }

        var status = Text(card, _store.HasCredentials
            ? _messages.Any(mail => mail.CachedPreview)
                ? "Показано збережені копії листів: " + _messages.Count +
                  ". Онови Gmail для перевірки нових повідомлень."
                : "Онови пошту, щоб перевірити нові документи."
            : "Підключи Gmail у Налаштуваннях.", 12);
        Text(card, "Щоб оновити пошту, відкрий меню ☰ → Синхронізація Gmail.", 12);
        ShowInboxContent();
    }

    private void ShowAssets()
    {
        Begin("assets", "Майно підрозділу");
        var selected=LoadUserProfile();
        if(selected is null || !new MobileAccessStore(this).Allows(selected.Unit,true)){Text(_body,"Немає дозволу на перегляд залишків. Налаштування надає головна Мавка.",15);return;}
        if(_allMobileUnits && new MobileAccessStore(this).Load() is {isMvo:false,units.Length:>1}){ShowGrantedAssets();return;}
        var top = Card();
        Button(top,"Змінити підрозділ",()=>ChooseMobileUnit(ShowAssets));
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
        Text(top, "Оновити пошту й сервер можна на головному екрані.", 12);
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

    private bool _showReferenceSnapshot;
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

        if(_showRootMirror && DrawRootAssets(new MobileAccessStore(this).Load()!.units.Single(u=>u.unit==profile.Unit).locationId))return;
        if(!_showRootMirror){var rootLink=Card();Button(rootLink,"Актуальний облік головної Мавки",()=>{_showRootMirror=true;ShowAssets();});}
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
                var filtered = InventorySearchSummary.Filter(
                    all, query, x => x.Name, x => x.Nsn);
                Text(localRows, $"Локальних облікових позицій: {filtered.Count} із {all.Count}", 14);
                var visibleSummary = InventorySearchSummary.Summarize(
                    filtered, x => x.TotalValue);
                Text(localRows, query.Length == 0
                    ? "Вартість позицій із відомою ціною: " +
                      visibleSummary.KnownTotal.ToString("0.00") + " грн"
                    : "Вартість знайдених позицій із відомою ціною: " +
                      visibleSummary.KnownTotal.ToString("0.00") + " грн", 13);
                if (query.Length > 0)
                {
                    var overallSummary = InventorySearchSummary.Summarize(
                        all, x => x.TotalValue);
                    Text(localRows, "Вартість усіх облікових позицій: " +
                        overallSummary.KnownTotal.ToString("0.00") + " грн", 12);
                }
                if (visibleSummary.UnknownPriceCount > 0)
                    Text(localRows, "Серед показаних позицій є майно з невідомою ціною; " +
                        "його вартість не враховано.", 12);
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
                Text(localRows, "Помилка локальної бази: " + ExplainOperationError(ex), 14);
            }
        }
        search.TextChanged += (_, _) => DrawLocal();
        DrawLocal();

        var openingPosted=_ledger.HasOpeningBalances(profile.Unit);
        if(openingPosted && !_showReferenceSnapshot)
        {
            var imported=Card();Text(imported,"Початкові залишки зараховано",16,true);
            Button(imported,"Відкрити вихідний знімок для звірки",()=>{_showReferenceSnapshot=true;RenderAssets();});
            return;
        }
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

        if(openingPosted)
        {
            Text(header,"Цей підрозділ уже має зараховані початкові залишки. Повторне зарахування недоступне.",13);
            Button(header,"Згорнути вихідний знімок",()=>{_showReferenceSnapshot=false;RenderAssets();});
            return;
        }
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
                        _showReferenceSnapshot=false;RenderAssets();
                        Toast.MakeText(this,changed?"Початкові залишки зараховано.":"Початкові залишки вже зараховані; дубль відхилено.",ToastLength.Long)?.Show();
                    }
                    catch (InvalidOperationException)
                    {
                        importing.Text = "Зарахування відхилено: у підрозділі вже є рухи. " +
                            "Потрібна окрема звірка з ГОЛОВНОЮ МАВКОЮ.";
                    }
                    catch (Exception ex)
                    {
                        importing.Text = "Помилка локальної бази: " + ExplainOperationError(ex);
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
                ExplainOperationError(ex), 14);
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
                .SetMessage("Помилка читання рухів: " + ExplainOperationError(ex))
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
            bool invoice = !string.IsNullOrWhiteSpace(mail.CachedInvoiceNumber) ||
                mail.Attachments.Any(a => MavkaExchangeParser.TryInvoice(a.RawJson, out _));
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
                ? "Службових листів поки немає. Онови їх у меню ☰ → Синхронізація Gmail."
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
                _theme.PositiveIcon : Color.Rgb(191, 92, 42));
            glyph.Background = Rounded(invoice is null
                ? _theme.PositiveIconFill : Color.Rgb(252, 237, 224), 12);
            row.AddView(glyph, new LinearLayout.LayoutParams(Dp(48), Dp(48)));

            var data = new LinearLayout(this) { Orientation = Orientation.Vertical };
            var lp = new LinearLayout.LayoutParams(0, -2, 1);
            lp.SetMargins(Dp(11), 0, Dp(5), 0);
            row.AddView(data, lp);
            Text(data, invoice?.Number ?? mail.CachedInvoiceNumber ?? mail.Subject, 14, true);
            Text(data, invoice is null && mail.CachedInvoiceNumber is null
                ? "Службове повідомлення"
                : "Документ · " + (invoice?.Recipient ?? mail.CachedInvoiceRecipient ?? ""), 12);
            Text(data, mail.DateText, 11);
            if (mail.CachedPreview)
                Text(data, "Локальна копія · онови Gmail для актуального стану", 11);
            if (!string.IsNullOrWhiteSpace(mail.ProcessingIssue))
                Text(data, mail.ProcessingIssue, 11);

            var arrow = Text(row, "›", 27, true);
            arrow.SetTextColor(_theme.NavigationArrow);
            row.Clickable = true;
            row.Click += (_, _) => ShowMessage(mail);
            if (!mail.CachedPreview && mail.MessageUid != 0 && mail.UidValidity != 0)
            {
                row.LongClickable = true;
                row.LongClick += (_, args) =>
                {
                    args.Handled = true;
                    ConfirmMailTrash(mail);
                };
            }
        }
    }

    private void ConfirmMailTrash(MavkaMailItem mail)
    {
        if (_busy || mail.CachedPreview || mail.MessageUid == 0 ||
            mail.UidValidity == 0) return;
        new AlertDialog.Builder(this)
            .SetTitle("Перемістити лист у кошик Gmail?")
            .SetMessage("Тема: " + mail.Subject +
                "\n\nНезбережені вкладення не будуть імпортовані. " +
                "Переміщення до кошика можна скасувати в Gmail. " +
                "Ключі й документи на телефоні залишаться.")
            .SetNegativeButton("Скасувати", (_, _) => { })
            .SetPositiveButton("У кошик", async (_, _) =>
            {
                if (_busy) return;
                _busy = true;
                try
                {
                    var password = _store.GetPassword() ??
                        throw new InvalidOperationException("Пароль Gmail відсутній.");
                    await GmailConnectionService.MoveToTrashAsync(
                        _store.GetEmail(), password,
                        _store.GetMainMavkaEmail(), mail);
                    _messages = _messages.Where(item =>
                        item.UidValidity != mail.UidValidity ||
                        item.MessageUid != mail.MessageUid).ToList();
                    SaveInboxCacheSafe();
                    ShowInbox();
                }
                catch (Exception)
                {
                    Toast.MakeText(this, "Не вдалося перемістити лист у кошик; " +
                        "він залишився у Gmail.", ToastLength.Long)?.Show();
                }
                finally { _busy = false; }
            }).Show();
    }

    private void ShowMailImagePreview(string name, byte[] imageBytes)
    {
        if (imageBytes.Length > MailImagePreviewPolicy.MaxBytes)
        {
            Toast.MakeText(this, "Зображення перевищує обмеження розміру.",
                ToastLength.Long)?.Show();
            return;
        }
        // Decode only a downsampled preview to avoid excessive allocations
        // when a small compressed image declares very large pixel dimensions.
        var dimensions = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length, dimensions);
        if (dimensions.OutWidth <= 0 || dimensions.OutHeight <= 0 ||
            dimensions.OutWidth > 10000 || dimensions.OutHeight > 10000)
        {
            Toast.MakeText(this, "Розмір зображення не підтримується.",
                ToastLength.Long)?.Show();
            return;
        }
        int scale = 1;
        while (dimensions.OutWidth / scale > 1300 ||
               dimensions.OutHeight / scale > 1300)
            scale *= 2;
        var options = new BitmapFactory.Options { InSampleSize = scale };
        using var bitmap = BitmapFactory.DecodeByteArray(
            imageBytes, 0, imageBytes.Length, options);
        if (bitmap is null)
        {
            Toast.MakeText(this, "Зображення пошкоджене.",
                ToastLength.Long)?.Show();
            return;
        }
        var preview = new ImageView(this);
        preview.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8));
        preview.SetScaleType(ImageView.ScaleType.FitCenter);
        // AlertDialog keeps the image until dismissal, so hand Android its
        // own reference rather than disposing a bitmap while it is drawn.
        preview.SetImageDrawable(new BitmapDrawable(Resources, bitmap.Copy(bitmap.GetConfig() ?? Bitmap.Config.Argb8888, false)));
        new AlertDialog.Builder(this)
            .SetTitle(name)
            .SetView(preview)
            .SetPositiveButton("Закрити", (_, _) => { })
            .Show();
    }

    private void ShowMessage(MavkaMailItem mail)
    {
        Begin("message", "Повідомлення");
        var accessStore=new MobileAccessStore(this);
        bool Denied(MavkaTextAttachment a)
        {
            if(MavkaExchangeParser.TryInvoice(a.RawJson,out var invoice) && invoice is not null)return !accessStore.Allows(invoice.Recipient);
            if(MavkaExchangeParser.TryAssets(a.RawJson,out var assets) && assets is not null)return !accessStore.Allows(assets.Unit,true);
            return false;
        }
        if(mail.Attachments.Any(Denied)){Text(_body,"Доступ до даних цього підрозділу відкликано або не надано.",16);return;}
        var card = Card();
        Text(card, mail.Subject, 19, true);
        Text(card, mail.DateText, 14);
        Text(card, mail.Body, 15);
        if (mail.CachedPreview)
            Text(card, "Кешований перегляд: вкладення доступні лише для читання. " +
                "Онови Gmail перед будь-якими діями з листом.", 12);
        var status = Text(card, mail.ProcessingIssue ??
            "Лист із налаштованої адреси ГОЛОВНОЇ МАВКИ.", 13);
        if (mail.Attachments.Count > 0)
        {
            Text(_body, "Вкладення", 19, true);
            foreach (var attachment in mail.Attachments)
            {
                var fileCard = Card();
                Text(fileCard, attachment.FileName, 16, true);
                Text(fileCard, _viewingArchivedDocument
                    ? "Збережено в захищеному локальному архіві · лише перегляд"
                    : mail.CachedPreview
                        ? "Локальна кешована копія · без імпорту й редагування"
                        : "Перегляд вкладення без автоматичного збереження", 13);
                var metadataOnly = MailAttachmentDisplayPolicy.Detect(attachment.FileName) !=
                    MailAttachmentDisplayPolicy.PreviewType.None;
                if (attachment.ImageBytes is { Length: > 0 } image)
                    Button(fileCard, "Переглянути зображення",
                        () => ShowMailImagePreview(attachment.FileName, image));
                else
                    Button(fileCard, metadataOnly ? "Інформація про вкладення" :
                        "Переглянути файл", () =>
                        new AlertDialog.Builder(this)
                            .SetTitle("Перегляд вкладення")
                            .SetMessage(attachment.FileName + "\n\n" +
                                (attachment.Text.Length <= 8000
                                    ? attachment.Text
                                    : attachment.Text[..8000] +
                                      "\n\nПерегляд скорочено."))
                            .SetPositiveButton("Закрити", (_, _) => { })
                            .Show());

                if (attachment.SecureFileEnvelope is string sealedFile &&
                    !mail.CachedPreview && !_viewingArchivedDocument)
                {
                    var fileStatus = Text(fileCard,
                        "Файл ще не збережено. У Gmail лист залишається у вхідних.", 12);
                    Button(fileCard, "Перевірити й зберегти захищений PDF/Excel/Word",
                        async () =>
                        {
                            if (_busy) return;
                            _busy = true;
                            fileStatus.Text = "Перевіряємо файл і зберігаємо...";
                            try
                            {
                                var stored = await Task.Run(() =>
                                {
                                    using var received = MavkaEncryptedFileEnvelope.Decrypt(
                                        sealedFile, id => _exchangeKeys.GetKey(id));
                                    return _privateFiles.Save(received);
                                });
                                if (_screen == "message")
                                    fileStatus.Text = "Збережено в захищених файлах: " +
                                        stored.Name + ". Перевірено локальне зчитування.";
                                if (mail.SingleSecureFile && mail.MessageUid != 0 &&
                                    mail.UidValidity != 0 && _store.HasCredentials)
                                {
                                    try
                                    {
                                        // Mark verified read-back before attempting IMAP.
                                        // Network failure must not trigger repeated
                                        // attachment decryption on future refreshes.
                                        _archive.MarkMailReady(
                                            mail.UidValidity, mail.MessageUid,
                                            MailProcessingScope());
                                        var password = _store.GetPassword() ??
                                            throw new InvalidOperationException(
                                                "Не налаштовано Gmail.");
                                        await GmailConnectionService.MoveToTrashAsync(
                                            _store.GetEmail(), password,
                                            _store.GetMainMavkaEmail(), mail);
                                        _archive.MarkMailTrashed(
                                            mail.UidValidity, mail.MessageUid,
                                            MailProcessingScope());
                                        _messages = _messages.Where(item =>
                                            item.UidValidity != mail.UidValidity ||
                                            item.MessageUid != mail.MessageUid).ToList();
                                        SaveInboxCacheSafe();
                                        if (_screen == "message")
                                            fileStatus.Text +=
                                                " Лист перенесено в кошик Gmail.";
                                    }
                                    catch (Exception)
                                    {
                                        if (_screen == "message")
                                            fileStatus.Text += " Файл збережено, " +
                                                "але перенести лист у кошик не вдалося. " +
                                                "Він залишився у Gmail.";
                                    }
                                }
                                else if (_screen == "message")
                                    fileStatus.Text += " Лист із кількома вкладеннями " +
                                        "залишається у Gmail до окремої перевірки.";
                            }
                            catch (KeyNotFoundException)
                            {
                                if (_screen == "message")
                                    fileStatus.Text = "Немає потрібного ключа обміну.";
                            }
                            catch (System.Security.Cryptography.CryptographicException)
                            {
                                if (_screen == "message")
                                    fileStatus.Text = "Автентичність файла не підтверджено. " +
                                        "Вміст відхилено та не збережено.";
                            }
                            catch (Exception)
                            {
                                if (_screen == "message")
                                    fileStatus.Text = "Не вдалося перевірити або зберегти PDF/Excel/Word. " +
                                        "Лист залишається у Gmail.";
                            }
                            finally { _busy = false; }
                        });
                }
            }
        }
        if (mail.AllSecureFileAttachments &&
            mail.Attachments.Count > 1 && !mail.CachedPreview && !_viewingArchivedDocument)
        {
            var bulkStatus = Text(_body,
                "Усі захищені вкладення можна перевірити та зберегти за один раз.", 13);
            Button(_body, "Зберегти всі захищені вкладення", async () =>
            {
                if (_busy) return;
                _busy = true;
                try
                {
                    int count = await Task.Run(() =>
                    {
                        int saved = 0;
                        foreach (var entry in mail.Attachments)
                        {
                            using var received = MavkaEncryptedFileEnvelope.Decrypt(
                                entry.SecureFileEnvelope!,
                                id => _exchangeKeys.GetKey(id));
                            _privateFiles.Save(received); // includes vault read-back
                            saved++;
                        }
                        return saved;
                    });
                    bulkStatus.Text = "Перевірено й захищено збережено: " + count +
                        " із " + mail.Attachments.Count + " вкладень.";
                    if (mail.MessageUid > 0 && mail.UidValidity > 0)
                    {
                        var sender = _store.GetMainMavkaEmail();
                        _archive.MarkMailReady(mail.UidValidity, mail.MessageUid, MailProcessingScope());
                        try
                        {
                            var password = _store.GetPassword() ??
                                throw new InvalidOperationException("Gmail не підключено.");
                            await GmailConnectionService.MoveToTrashAsync(
                                _store.GetEmail(), password, sender, mail);
                            _archive.MarkMailTrashed(mail.UidValidity, mail.MessageUid, MailProcessingScope());
                            _messages = _messages.Where(m =>
                                m.MessageUid != mail.MessageUid ||
                                m.UidValidity != mail.UidValidity).ToArray();
                            SaveInboxCacheSafe();
                            bulkStatus.Text += " Лист перенесено до кошика Gmail.";
                        }
                        catch (Exception)
                        {
                            bulkStatus.Text += " Лист уже опрацьовано. Перенесення в " +
                                "кошик буде повторено під час наступного оновлення пошти.";
                        }
                    }
                }
                catch (KeyNotFoundException)
                {
                    bulkStatus.Text = "Немає потрібного ключа. Збережені раніше " +
                        "вкладення не видалено, лист залишається у Gmail.";
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    bulkStatus.Text = "AES-GCM: одне з вкладень не пройшло " +
                        "перевірку. Лист не перенесено до кошика.";
                }
                catch (Exception ex)
                {
                    bulkStatus.Text = "Не вдалося зберегти всі файли: " +
                        ExplainOperationError(ex) + ". Лист залишено у Gmail.";
                }
                finally { _busy = false; }
            });
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
            StylePrimaryButton(more);
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
                if (new MobileAccessStore(this).Load() is not {canConfirmInvoices:true,isMvo:true} || !new MobileAccessStore(this).Allows(invoice.Recipient))
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
                if(_ledger.GetMailStatus(invoice.DocumentId)=="sent"){status.Text="Накладну вже зараховано локально.";return;}
                new AlertDialog.Builder(this).SetTitle("Підтвердити отримання майна?")
                    .SetMessage("Документ № "+invoice.Number+"\nПідрозділ: "+profile.Unit+"\nПозицій: "+invoice.Positions.Count+"\nМайно буде додано до локального обліку телефона. Окреме підтвердження головній Мавці не надсилається.")
                    .SetNegativeButton("Скасувати",(_,_)=>{})
                    .SetPositiveButton("Підтвердити локально",(_,_)=>
                    {
                        try
                        {
                            using var mirror=new MobileChangeStore(this);
                            if(!mirror.IsCurrentInvoice(invoice))throw new InvalidOperationException("Цю версію накладної змінено або скасовано. Відкрийте оновлену накладну після синхронізації.");
                            if(MobileChangeStore.ReceiptFamily(invoice.DocumentId) is {} family && _ledger.HasReceiptFamily(family))throw new InvalidOperationException("Майно за цією накладною вже зараховано. Інша версія не зараховується повторно.");
                            _ledger.AddConfirmedInvoice(invoice);_ledger.MarkMailSent(invoice.DocumentId);
                            if(_archive.FindDocument(invoice.DocumentId) is not null){_archive.MarkPosted(invoice.DocumentId);_archive.MarkDeliverySent(invoice.DocumentId);}
                            status.Text="Майно зараховано до локального обліку.";
                        }
                        catch(Exception ex){ShowSafeFailure("Облік МВО",ex);}
                    }).Show();
            });
        }

        // Automatic Gmail cleanup requires a verified durable import.
        // Separately, a user can explicitly move a displayed incoming message
        // to Gmail Trash. This is a reversible mailbox action, not an import,
        // expunge, local-document deletion, or key reset.
        if (!_viewingArchivedDocument && !mail.CachedPreview &&
            mail.MessageUid != 0 && mail.UidValidity != 0)
        {
            Button(_body, "Перемістити лист у кошик Gmail", () =>
            {
                if (_busy) return;
                new AlertDialog.Builder(this)
                    .SetTitle("Перемістити цей лист у кошик?")
                    .SetMessage("Лист буде переміщено до кошика Gmail, звідки його можна " +
                        "відновити. Якщо вкладення не були імпортовані, вони не збережуться " +
                        "в локальному архіві автоматично. Збережені ключі та локальні " +
                        "документи не видаляються. Продовжити?")
                    .SetNegativeButton("Скасувати", (_, _) => { })
                    .SetPositiveButton("У кошик", async (_, _) =>
                    {
                        if (_busy) return;
                        _busy = true;
                        status.Text = "Перенесення листа до кошика Gmail...";
                        try
                        {
                            var password = _store.GetPassword();
                            if (string.IsNullOrEmpty(password))
                                throw new InvalidOperationException("Пароль Gmail відсутній.");

                            // Gmail revalidates UIDVALIDITY, the UID and exact
                            // authorized sender before the reversible move.
                            await GmailConnectionService.MoveToTrashAsync(
                                _store.GetEmail(), password,
                                _store.GetMainMavkaEmail(), mail);
                            _messages = _messages.Where(item =>
                                item.UidValidity != mail.UidValidity ||
                                item.MessageUid != mail.MessageUid).ToList();
                            SaveInboxCacheSafe();

                            if (_screen == "message" && !_viewingArchivedDocument)
                                ShowInbox();
                        }
                        catch (MailKit.Security.AuthenticationException)
                        {
                            if (_screen == "message")
                                status.Text = "Gmail відхилив авторизацію. Перевір пароль застосунку.";
                        }
                        catch (System.OperationCanceledException)
                        {
                            if (_screen == "message")
                                status.Text = "Gmail не відповів вчасно. Лист залишено у вхідних.";
                        }
                        catch (Exception ex)
                        {
                            if (_screen == "message")
                                status.Text = ex is InvalidOperationException
                                    ? ex.Message
                                    : ExplainMailError(ex) +
                                      " Лист залишено у вхідних Gmail.";
                        }
                        finally { _busy = false; }
                    })
                    .Show();
            });
        }

        Button(_body, _viewingArchivedDocument ? "← До архіву" : "← До вхідних",
            _viewingArchivedDocument ? ShowDocuments : ShowInbox);
    }

    private void SaveInboxCacheSafe()
    {
        try
        {
            if (_store.HasCredentials)
                _inboxCache.Save(_store.GetEmail(), _store.GetMainMavkaEmail(), _messages);
        }
        catch (Exception) { /* Gmail and archive state stay authoritative. */ }
    }

    private string CachedMailHint() => _messages.Count > 0
        ? " Збережені листи доступні для перегляду: " + _messages.Count + "."
        : "";

    private static string ExplainOperationError(Exception error) => error switch
    {
        KeyNotFoundException => "Потрібний ключ обміну недоступний; дані залишено для повторної спроби",
        System.Security.Cryptography.CryptographicException => "Не вдалося перевірити автентичність захищених даних",
        System.OperationCanceledException => "Перевищено час очікування; повтори спробу",
        System.Net.Sockets.SocketException => "Немає з'єднання з мережею",
        System.IO.IOException => "Не вдалося прочитати або зберегти дані; перевір сховище й мережу",
        _ => "Операцію не завершено; повтори спробу"
    };

    private static string ExplainMailError(Exception error) => error switch
    {
        System.Net.Sockets.SocketException =>
            "Не вдається підключитися до інтернету. Перевір мережу й повтори спробу.",
        System.IO.IOException =>
            "З'єднання з Gmail перервано. Перевір інтернет і повтори спробу.",
        MailKit.Security.SslHandshakeException =>
            "Не вдалося встановити захищене з'єднання Gmail. Перевір дату й мережу.",
        _ => "Не вдалося оновити Gmail. Перевір підключення й повтори спробу."
    };

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
        try { _archive.RecordSyncEvent("start", "Розпочато отримання листів"); }
        catch { /* Do not interrupt encrypted mail processing for diagnostics. */ }
        var syncWatch = System.Diagnostics.Stopwatch.StartNew();
        long metadataMillis = 0, importMillis = 0, trashMillis = 0;
        status.Text = "Перевірка службових листів...";
        try
        {
            var password = _store.GetPassword();
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Пароль не збережений.");
            _messages = await GmailConnectionService.ReadFromSenderAsync(
                _store.GetEmail(), password, sender, _exchangeKeys,
                _documentPasswords, LoadUserProfile()?.ClientId,
                validity => _archive.ListMailProcessingStates(validity, MailProcessingScope()));
            metadataMillis = syncWatch.ElapsedMilliseconds;
            try
            {
                var pendingQr = _qrPairing.LoadPending();
                var currentProfile = LoadUserProfile();
                if (pendingQr is not null && currentProfile is not null)
                {
                    if (DateTimeOffset.UtcNow >= pendingQr.ExpiresUtc)
                        _qrPairing.ClearPending();
                    else foreach (var mail in _messages)
                        if (!mail.PreviouslyArchived &&
                            QrPairingProtocol.VerifyAck(mail.Body, pendingQr,
                                currentProfile.ClientId, DateTimeOffset.UtcNow))
                        {
                            _qrPairing.MarkConfirmed(pendingQr.MainEmail);
                            _archive.MarkMailReady(mail.UidValidity, mail.MessageUid,
                                MailProcessingScope());
                            _archive.RecordSyncEvent("pairing", "QR-зв'язок підтверджено");
                            break;
                        }
                }
            }
            catch
            {
                // Pairing is optional. Never relax Gmail authentication or
                // prevent existing encrypted transfers after a bad QR.
            }
            await ProcessFileTransfersAsync(password, sender);
            importMillis = syncWatch.ElapsedMilliseconds - metadataMillis;
            var report = await ArchiveAndCleanupAsync(password, sender);
            trashMillis = syncWatch.ElapsedMilliseconds - metadataMillis - importMillis;
            try
            {
                _archive.RecordSyncEvent("complete",
                    "IMAP " + metadataMillis + " мс · вкладення " + importMillis +
                    " мс · архів/кошик " + trashMillis + " мс");
            }
            catch { /* Privacy-safe logging must not affect synchronization. */ }
            SaveInboxCacheSafe();
            var syncReport = await SendPendingReceiptsAsync(password, sender);
            try { await SendPendingFilePartsAsync(maximum: 3); }
            catch { syncReport += " Частини файлів залишені в черзі надсилання."; }
            if (_screen is "sync" or "home" or "inbox" or "assets")
                status.Text = $"Листів у вхідних: {_messages.Count}. " + report +
                    " " + syncReport +
                    $" · Час: {syncWatch.Elapsed.TotalSeconds:0.0} с " +
                    $"(IMAP {metadataMillis / 1000.0:0.0}, файли {importMillis / 1000.0:0.0}, " +
                    $"архів/Trash {trashMillis / 1000.0:0.0}).";
        }
        catch (MailKit.Security.AuthenticationException)
        {
            if (_screen is "sync" or "home" or "inbox")
                status.Text = "Google відхилив авторизацію. Перевір пароль застосунку.";
        }
        catch (System.OperationCanceledException)
        {
            if (_screen is "sync" or "home" or "inbox")
                status.Text = "Gmail не відповідає вчасно. Перевір інтернет і спробуй знову." +
                    CachedMailHint();
        }
        catch (Exception ex)
        {
            if (_screen is "sync" or "home" or "inbox")
                status.Text = ExplainMailError(ex) + CachedMailHint();
        }
        finally { _busy = false; }
    }

    private Task<string> SendPendingReceiptsAsync(string password,string sender)
    {
        foreach(var id in _ledger.ListPendingReceiptIds(limit:100))_ledger.MarkMailSent(id);
        return Task.FromResult("Підтвердження МВО зберігаються лише в локальному обліку.");
    }

    // Durable Gmail UIDs are mailbox-local. Bind processing status to BOTH
    // account and authorized sender, in addition to IMAP UIDVALIDITY; this
    // prevents accidentally skipping mail after a Gmail account change.
    private string MailProcessingScope() =>
        _store.GetEmail().Trim().ToLowerInvariant() + "|" +
        _store.GetMainMavkaEmail().Trim().ToLowerInvariant();

    private string TransferScope() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(_store.GetEmail().Trim().ToLowerInvariant() + ":" +
            _store.GetMainMavkaEmail().Trim().ToLowerInvariant() + ":" + LoadUserProfile()?.ClientId))).ToLowerInvariant();
    private PendingFileTransferStore Transfers() => new(System.IO.Path.Combine(FilesDir!.AbsolutePath,
        "pending_file_parts_v1", TransferScope()), TransferKeystoreCipher.Seal, TransferKeystoreCipher.Open);
    private OutgoingFileQueue OutgoingFiles() => new(System.IO.Path.Combine(FilesDir!.AbsolutePath,
        "outgoing_file_parts_v1", TransferScope()), TransferKeystoreCipher.Seal, TransferKeystoreCipher.Open);
    private async Task SendPendingFilePartsAsync(int maximum = 125, Action<string>? progress = null)
    {
        var queue = OutgoingFiles();
        string password = _store.GetPassword() ?? "";
        int sent = 0;
        foreach (var original in await Task.Run(() => queue.List()))
        {
            var batch = original;
            while (batch.NextIndex < batch.Metadata.Count && sent < maximum)
            {
                string encrypted = await Task.Run(() => queue.Part(batch));
                var docPassword = _documentPasswords.Load() ??
                    throw new InvalidOperationException(
                        "Налаштуй пароль документів до відправлення захищених частин.");
                encrypted = MavkaPasswordedEnvelope.Wrap(encrypted, docPassword);
                await MobileExchangeTransport.SendFilePartAsync(this, _store.GetEmail(), password,
                    _store.GetMainMavkaEmail(), batch.Metadata.FileName + "." + (batch.NextIndex + 1) + "-" + batch.Metadata.Count, encrypted);
                batch = await Task.Run(() => queue.Advance(batch)); sent++;
                progress?.Invoke($"Надіслано {batch.NextIndex} із {batch.Metadata.Count} частин · {batch.Metadata.FileName}");
            }
            if (batch.NextIndex == batch.Metadata.Count) await Task.Run(() => queue.Delete(batch.Metadata.TransferId));
            if (sent >= maximum) break;
        }
    }

    private async Task ProcessFileTransfersAsync(string password, string sender)
    {
        var pending = Transfers();
        var messages = _messages.ToList();
        foreach (var mail in messages)
        {
            if (mail.CachedPreview) continue;
            // Only an AES-GCM failure raised while authenticating a SINGLE
            // mail attachment can authorize reversible Trash. Do not delete
            // on missing/rotating keys, network loss or whole-file assembly.
            bool authenticatingPart = false;
            try
            {
                var parts = mail.Attachments.Where(a => a.SecureFilePart is not null).Select(a => a.SecureFilePart!).ToList();
                DriveFileCommand? drive = null;
                foreach (var a in mail.Attachments.Where(a => a.AuthenticatedJson))
                    if (DriveFileCommand.TryRead(a.RawJson, out drive) && drive is not null)
                    {
                        var downloaded = await Task.Run(() => drive.DownloadAsync());
                        foreach (var packet in downloaded)
                        {
                            if (MavkaPasswordedEnvelope.IsWrapped(packet))
                            {
                                var documentPassword = _documentPasswords.Load() ??
                                    throw new InvalidOperationException("Налаштуй пароль документів.");
                                parts.Add(MavkaPasswordedEnvelope.Unwrap(packet, documentPassword));
                            }
                            else parts.Add(packet); // Legacy key-encrypted bundle.
                        }
                        break;
                    }
                if (parts.Count == 0) continue;
                string? id = null;
                foreach (var part in parts)
                {
                    authenticatingPart = true;
                    var progress = await Task.Run(() => pending.Accept(part, key => _exchangeKeys.GetKey(key),
                        mail.Attachments.Count == 1 ? new PendingFileTransferStore.MailRef(mail.UidValidity, mail.MessageUid) : null, drive?.TransferId));
                    authenticatingPart = false;
                    id = progress.Id;
                    if (drive is not null && id != drive.TransferId)
                        throw new System.Security.Cryptography.CryptographicException("Drive transfer ID mismatch.");
                    if (drive is null && progress.Received < progress.Total)
                        ReplaceTransferMessage(mail, $"Отримано {progress.Received} із {progress.Total} частин · {progress.Name}");
                }
                if (id is null) continue;
                await Task.Run(() =>
                {
                    using var completed = pending.Assemble(id);
                    if (completed is not null)
                    {
                        var saved = _privateFiles.Save(completed);
                        pending.MarkSaved(id, saved.Id);
                    }
                });
            }
            catch (KeyNotFoundException)
            {
                ReplaceTransferMessage(mail,
                    "Немає ключа для розшифрування частини. Лист збережено у Gmail; " +
                    "перевір отримання поточного/майбутнього ключів.");
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                const string integrityError =
                    "Помилка криптографічної перевірки AES-GCM: " +
                    "частина пошкоджена або ключ не відповідає. Файл не прийнято.";
                bool isolatedPart = authenticatingPart && mail.Attachments.Count == 1 &&
                    mail.Attachments[0].SecureFilePart is not null &&
                    mail.MessageUid != 0 && mail.UidValidity != 0;
                if (isolatedPart)
                {
                    try
                    {
                        await GmailConnectionService.MoveToTrashAsync(
                            _store.GetEmail(), password, sender, mail);
                        _archive.MarkMailTrashed(mail.UidValidity, mail.MessageUid, MailProcessingScope());
                        _messages = _messages.Where(item =>
                            item.MessageUid != mail.MessageUid ||
                            item.UidValidity != mail.UidValidity).ToArray();
                        continue;
                    }
                    catch (Exception)
                    {
                        ReplaceTransferMessage(mail, integrityError +
                            " Перенести лист у кошик не вдалося; він залишається у Gmail.");
                    }
                }
                else ReplaceTransferMessage(mail, integrityError +
                    " Лист збережено для перевірки.");
            }
            catch (ArgumentException)
            {
                ReplaceTransferMessage(mail,
                    "Неправильний формат або розмір зашифрованої частини. " +
                    "Вкладення відхилено; лист залишено у Gmail.");
            }
            catch (System.IO.IOException)
            {
                ReplaceTransferMessage(mail,
                    "Помилка мережі або файлового сховища під час передачі. " +
                    "Отримані частини збережено; повтори синхронізацію.");
            }
            catch (Exception)
            {
                ReplaceTransferMessage(mail,
                    "Передачу не завершено. Перевір мережу, файлове сховище " +
                    "або доступ до зашифрованого файла Drive.");
            }
        }
        // Completion is recorded only AFTER whole-file verification and vault read-back.
        foreach (var transfer in await Task.Run(() => pending.List()))
        {
            if (!transfer.Saved) continue;
            var saved = await Task.Run(() => pending.Saved(transfer.Id));
            if (saved is null) continue;
            try
            {
                using var verified = await Task.Run(() => _privateFiles.Load(saved.FileId));
                if (verified.Sha256 != saved.Metadata.Sha256 || verified.FileName != saved.Metadata.FileName) continue;
            }
            catch { continue; } // Never trash mail if the user removed the saved copy.
            var refs = await Task.Run(() => pending.CleanupCandidates(transfer.Id));
            bool completeCleanup = true;
            foreach (var item in refs)
            {
                var mail = new MavkaMailItem("", "", "", Array.Empty<MavkaTextAttachment>(), item.Uid, item.Validity);
                try
                {
                    await GmailConnectionService.MoveToTrashAsync(_store.GetEmail(), password, sender, mail);
                    await Task.Run(() => pending.AcknowledgeMail(transfer.Id, item));
                    _messages = _messages.Where(m => m.MessageUid != item.Uid || m.UidValidity != item.Validity).ToArray();
                }
                catch (Exception) { completeCleanup = false; }
            }
            // Mixed letters stay in Gmail, but local parts are redundant after verified vault save.
            if (completeCleanup) await Task.Run(() => pending.Delete(transfer.Id));
        }
    }

    private void ReplaceTransferMessage(MavkaMailItem original, string status) =>
        _messages = _messages.Select(m => m.UidValidity == original.UidValidity && m.MessageUid == original.MessageUid
            ? m with { ProcessingIssue = status } : m).ToArray();

    private async Task<string> ArchiveAndCleanupAsync(string password, string sender)
    {
        var profile = LoadUserProfile();
        int moved = 0, saved = 0, kept = 0;
        var remaining = new List<MavkaMailItem>();
        foreach (var mail in _messages)
        {
            if (QrPairingProtocol.IsAck(mail.Body) &&
                _archive.IsMailReady(mail.UidValidity, mail.MessageUid,
                    MailProcessingScope()))
            {
                try
                {
                    await GmailConnectionService.MoveToTrashAsync(
                        _store.GetEmail(), password, sender, mail);
                    _archive.MarkMailTrashed(mail.UidValidity, mail.MessageUid,
                        MailProcessingScope());
                    moved++;
                }
                catch
                {
                    remaining.Add(mail);
                    kept++;
                }
                continue;
            }
            if (mail.PreviouslyArchived)
            {

                // The durable ready marker was written only after every
                // document passed read-back. Retry Trash without downloading
                // or re-importing the complete message.
                if (!_archive.IsMailReady(mail.UidValidity, mail.MessageUid, MailProcessingScope()))
                {
                    remaining.Add(mail with
                    {
                        ProcessingIssue = "Немає підтвердження архівування; лист залишено."
                    });
                    kept++;
                    continue;
                }
                try
                {
                    await GmailConnectionService.MoveToTrashAsync(
                        _store.GetEmail(), password, sender, mail);
                    _archive.MarkMailTrashed(mail.UidValidity, mail.MessageUid, MailProcessingScope());
                    moved++;
                }
                catch (Exception)
                {
                    remaining.Add(mail with
                    {
                        ProcessingIssue = "Лист уже архівований; Gmail Trash буде повторено."
                    });
                    kept++;
                }
                continue;
            }
            if (mail.Attachments.Any(a => a.SecureFilePart is not null ||
                (a.AuthenticatedJson && DriveFileCommand.TryRead(a.RawJson, out _))))
            { remaining.Add(mail); kept++; continue; }
            // Never discard mail if any attachment is unread, unsupported or corrupt.
            if (!mail.AttachmentsComplete || mail.Attachments.Count == 0)
            {
                remaining.Add(mail with
                {
                    ProcessingIssue = mail.Attachments.Any(
                            a => a.SecureFileEnvelope is not null)
                        ? "Отримано захищений PDF/Excel/Word. Відкрий лист, перевір та " +
                          "збережи вкладення. До підтвердження лист залишається у Gmail."
                        : mail.Attachments.Count == 0
                            ? "Немає придатного вкладення JSON/TXT. " +
                              "Незахищені PDF/Excel/Word автоматично не імпортуються."
                            : "Не всі вкладення підтримуються. " +
                              "Перевір типи файлів; лист залишено."
                });
                kept++;
                continue;
            }
            bool ready = true;
            string problem = "";
            foreach (var attachment in mail.Attachments)
            {
                if (attachment.InstalledKeyPackage) continue;
                // Only exact-sender, AES-GCM authenticated JSON is accepted.
                // An unsigned Gmail body or plaintext JSON can never
                // create a notice or schedule any reminder.
                if (attachment.AuthenticatedJson && profile is not null &&
                    new MobileAccessStore(this).TryNotice(attachment.RawJson,out var incomingNotice) &&
                    incomingNotice is not null)
                {
                    try
                    {
                        bool inserted = _noticeStore.Add(
                            incomingNotice, attachment.RawJson!);
                        if (!_noticeStore.ContainsSaved(incomingNotice.NoticeId, incomingNotice.Unit, attachment.RawJson!))
                            throw new InvalidOperationException("Notice readback failed.");
                        if (inserted)
                        {
                            MavkaNoticeAlerts.ShowNew(this, incomingNotice.NoticeId);
                            saved++;
                        }
                        continue;
                    }
                    catch (Exception ex)
                    {
                        ready = false;
                        problem = "Не вдалося зберегти сповіщення: " +
                            ExplainOperationError(ex) + ". Лист залишено у Gmail.";
                        break;
                    }
                }
                if(attachment.AuthenticatedJson && attachment.RawJson is string policyRaw)
                {
                    if(await Task.Run(()=>{using var changes=new MobileChangeStore(this);return changes.TryAccept(policyRaw);})){saved++;continue;}
                    using var policyDoc=System.Text.Json.JsonDocument.Parse(policyRaw);
                    if(policyDoc.RootElement.TryGetProperty("format",out var pf) && pf.GetString()=="MAVKA_ACCESS_UPDATE_V1")
                    {
                        if(policyDoc.RootElement.GetProperty("recipientClientId").GetString()!=profile?.ClientId)throw new System.Security.Cryptography.CryptographicException("Інший користувач.");
                        new MobileAccessStore(this).Accept(policyDoc.RootElement.GetProperty("access"));saved++;continue;
                    }
                }
                if (profile is null || attachment.RawJson is null ||
                    !new MobileAccessStore(this).TryDocument(attachment.RawJson, out var document) ||
                    document is null)
                {
                    ready = false;
                    problem = profile is null
                        ? "Налаштуй профіль перед імпортом."
                        : attachment.RawJson is null
                            ? attachment.Text.StartsWith("🔒", StringComparison.Ordinal)
                                ? attachment.Text
                                : "Вкладення не розшифровано або воно не є перевіреним JSON-документом."
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
                    new MobileAccessStore(this).AutoPost(document.Json);
                    saved++;
                }
                catch (Exception ex)
                {
                    ready = false;
                    problem = "Не вдалося зберегти документ: " + ExplainOperationError(ex) +
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
                _archive.MarkMailReady(mail.UidValidity, mail.MessageUid, MailProcessingScope());
                if (!_archive.IsMailReady(mail.UidValidity, mail.MessageUid, MailProcessingScope()))
                    throw new InvalidOperationException("Mail journal not persisted.");
                await GmailConnectionService.MoveToTrashAsync(
                    _store.GetEmail(), password, sender, mail);
                _archive.MarkMailTrashed(mail.UidValidity, mail.MessageUid, MailProcessingScope());
                moved++;
            }
            catch (Exception ex)
            {
                // Both archived documents and the ready marker survive disconnection.
                // Re-fetching will not post anything or duplicate the archive.
                remaining.Add(mail with
                {
                    ProcessingIssue = "Документ збережено локально, але Gmail Trash " +
                        "недоступний (" + ExplainOperationError(ex) +
                        "). Перенесення буде повторено під час оновлення."
                });
                kept++;
            }
        }
        _messages = remaining;
        return $"Архівовано/перевірено вкладень: {saved}; у кошик: {moved}; залишено: {kept}.";
    }

    private void ShowNotices()
    {
        Begin("notices", "Сповіщення та нагадування");
        var profile = LoadUserProfile();
        if (profile is null)
        {
            Text(_body, "Спочатку заповни профіль підрозділу.", 14);
            return;
        }
        var header = Card();
        Text(header, "Повідомлення ГОЛОВНОЇ МАВКИ", 18, true);
        Button(header,"Обрати підрозділ",()=>ChooseMobileUnit(ShowNotices));
        Text(header, "Надходять лише із зашифрованих вкладень від дозволеного " +
            "відправника. Нові листи перевіряються під час синхронізації Gmail. " +
            "Нагадування ставиш тільки ти; ГОЛОВНА МАВКА може запропонувати час.", 13);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
            !MavkaNoticeAlerts.Allowed(this))
            Button(header, "Увімкнути сповіщення Android", () =>
                RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 1917));
        Text(header, MavkaNoticeBackground.Enabled(this)
            ? "Автоматична перевірка пошти увімкнена (приблизно кожні 15 хвилин)."
            : "Автоматична перевірка вимкнена. Нові повідомлення з'являються після ручної синхронізації.", 12);
        Button(header, MavkaNoticeBackground.Enabled(this)
            ? "Вимкнути фонову перевірку сповіщень"
            : "Увімкнути фонову перевірку (15 хв)", () =>
        {
            try
            {
                if (!MavkaNoticeBackground.Enabled(this) &&
                    (!_store.HasCredentials || LoadUserProfile() is null))
                    throw new InvalidOperationException(
                        "Для автоматичної перевірки потрібні Gmail та профіль.");
                MavkaNoticeBackground.SetEnabled(this,
                    !MavkaNoticeBackground.Enabled(this));
                ShowNotices();
            }
            catch (Exception ex)
            {
                // JobScheduler can reject a task due to a missing OS permission,
                // a disabled component or device background restrictions.
                // Display only a safe diagnostic; never log credentials or mail.
                var hint = ex switch
                {
                    Java.Lang.SecurityException =>
                        "Android відхилив запуск фонової перевірки через системний дозвіл.",
                    InvalidOperationException => ex.Message,
                    _ => ExplainOperationError(ex)
                };
                Toast.MakeText(this, hint, ToastLength.Long)?.Show();
            }
        });

        Button(header,"Налаштування сповіщень і перевірка нагадування",ShowNoticeDiagnostics);

        var listCard = Card();
        try
        {
            var noticePolicy=new MobileAccessStore(this).Load();
            var notices=(_allMobileUnits && noticePolicy is {isMvo:false}?noticePolicy.units.SelectMany(u=>_noticeStore.List(u.unit)):_noticeStore.List(profile.Unit)).OrderByDescending(n=>n.Notice.IssuedUtc).ToList();
            Text(listCard, "Повідомлень: " + notices.Count +
                " · непрочитаних: " + notices.Count(n => !n.IsRead), 14);
            if (notices.Count == 0)
                Text(listCard, "Службових повідомлень ще немає. " +
                    "Онови пошту через ☰ → Синхронізація Gmail.", 14);
            foreach (var row in notices)
            {
                var notice = row.Notice;
                var tile = new LinearLayout(this) { Orientation = Orientation.Vertical };
                tile.SetPadding(Dp(10), Dp(10), Dp(10), Dp(10));
                tile.Background = Rounded(_theme.Card, 13, _theme.CardOutline);
                var layout = new LinearLayout.LayoutParams(-1, -2);
                layout.BottomMargin = Dp(10);
                listCard.AddView(tile, layout);
                Text(tile, (row.IsRead ? "" : "● ") + notice.Title +
                    (notice.Priority == "important" ? " · Важливе" : ""), 16, true);
                Text(tile, "Від: ГОЛОВНА МАВКА · " +
                    notice.IssuedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), 12);
                Text(tile, notice.Body, 14);
                if (!row.IsRead)
                    Button(tile, "Позначити прочитаним", () =>
                    {
                        try { _noticeStore.MarkRead(notice.NoticeId, notice.Unit); ShowNotices(); }
                        catch (Exception ex) {
                            Toast.MakeText(this, ExplainOperationError(ex),
                                ToastLength.Long)?.Show();
                        }
                    });
                if (row.RemindUtc is { } scheduled)
                    Text(tile, "Нагадати: " +
                        scheduled.ToLocalTime().ToString("dd.MM.yyyy HH:mm") +
                        (row.Reminded ? " · виконано" : " · заплановано"), 13);
                if (notice.SuggestedReminderUtc is { } suggested &&
                    suggested > DateTimeOffset.UtcNow && row.RemindUtc is null)
                    Button(tile, "Нагадати у запропонований час: " +
                        suggested.ToLocalTime().ToString("dd.MM HH:mm"),
                        () => SaveNoticeReminder(notice, notice.Unit, suggested));
                Button(tile, "Вибрати дату та час нагадування", () =>
                    ChooseNoticeReminder(notice, notice.Unit));
                if (row.RemindUtc is not null && !row.Reminded)
                    Button(tile, "Скасувати нагадування", () =>
                        SaveNoticeReminder(notice, notice.Unit, null));
                Button(tile,"Видалити повідомлення",()=>new AlertDialog.Builder(this)
                    .SetTitle("Видалити повідомлення й нагадування?")
                    .SetMessage("Повідомлення буде видалено на цьому телефоні. Облік і документи не зміняться.")
                    .SetNegativeButton("Скасувати",(_,_)=>{})
                    .SetPositiveButton("Видалити",(_,_)=>
                    {
                        try{_noticeStore.Delete(notice.NoticeId,notice.Unit);MavkaNoticeAlerts.Remove(this,notice.NoticeId);ShowNotices();}
                        catch(Exception ex){Toast.MakeText(this,ExplainOperationError(ex),ToastLength.Long)?.Show();}
                    }).Show());
            }
        }
        catch (Exception ex)
        {
            Text(listCard, "Не вдалося відкрити сповіщення: " +
                ExplainOperationError(ex), 13);
        }
    }

    private void ShowNoticeDiagnostics()
    {
        Begin("notice-diagnostics","Перевірка нагадувань");
        var diagnostic = Card();
        Text(diagnostic, "Тест нагадування без Gmail", 17, true);
        Text(diagnostic,
            "Створює тільки локальне тестове сповіщення приблизно через 2 хв. " +
            "Не додає документів, не імітує ГОЛОВНУ МАВКУ й не змінює облік.", 12);
        Button(diagnostic, "Запланувати тестове нагадування (2 хв)", () =>
        {
            try
            {
                if (!MavkaNoticeAlerts.Allowed(this))
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                        RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 1917);
                    Toast.MakeText(this,
                        "Дозволь сповіщення Android і натисни кнопку ще раз.",
                        ToastLength.Long)?.Show();
                    return;
                }
                MavkaNoticeAlerts.ScheduleLocalTest(this);
                Toast.MakeText(this,
                    "Тестове нагадування заплановано приблизно через 2 хв. " +
                    "Android може його трохи відкласти.",
                    ToastLength.Long)?.Show();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this,
                    ex is InvalidOperationException ? ex.Message : ExplainOperationError(ex),
                    ToastLength.Long)?.Show();
            }
        });

        Button(_body,"До повідомлень",ShowNotices);
    }

    private void SaveNoticeReminder(MavkaNotice notice, string unit, DateTimeOffset? when)
    {
        try
        {
            // Schedule OS alarm first. If SQLite update fails, undo the
            // changed alarm and restore the previously saved reminder.
            var previous = _noticeStore.Find(notice.NoticeId, unit)?.RemindUtc;
            MavkaNoticeAlerts.SetReminder(this, notice.NoticeId, when);
            try { _noticeStore.SetReminder(notice.NoticeId, unit, when); }
            catch
            {
                MavkaNoticeAlerts.SetReminder(this, notice.NoticeId, previous);
                throw;
            }
            if (when is not null && Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
                !MavkaNoticeAlerts.Allowed(this))
                RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 1917);
            ShowNotices();
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, "Не вдалося встановити нагадування: " +
                ExplainOperationError(ex), ToastLength.Long)?.Show();
        }
    }

    private void ChooseNoticeReminder(MavkaNotice notice, string unit)
    {
        var initial = (notice.SuggestedReminderUtc ?? DateTimeOffset.Now).ToLocalTime();
        new DatePickerDialog(this, (_, date) =>
        {
            new TimePickerDialog(this, (_, time) =>
            {
                try
                {
                    var local = new DateTime(date.Year, date.Month + 1,
                        date.DayOfMonth, time.HourOfDay, time.Minute, 0,
                        DateTimeKind.Unspecified);
                    var utc = new DateTimeOffset(local,
                        TimeZoneInfo.Local.GetUtcOffset(local));
                    SaveNoticeReminder(notice, unit, utc);
                }
                catch (Exception ex)
                {
                    Toast.MakeText(this, ExplainOperationError(ex),
                        ToastLength.Long)?.Show();
                }
            }, initial.Hour, initial.Minute, true).Show();
        }, initial.Year, initial.Month - 1, initial.Day).Show();
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
        var policyForView=new MobileAccessStore(this).Load();
        if(policyForView is not null)DrawRootDocuments(_allMobileUnits && !policyForView.isMvo?null:policyForView.units.Single(u=>u.unit==profile.Unit).locationId);
        var card = Card();
        if(!new MobileAccessStore(this).Allows(profile.Unit)){Text(card,"Доступ до документів не надано.",15);return;}
        Button(card,"Змінити підрозділ",()=>ChooseMobileUnit(ShowDocuments));
        Text(card, "Локальний архів · " + (_allMobileUnits?"усі доступні підрозділи":profile.Unit), 17, true);
        Text(card, "Документи доступні без Gmail. Збереження не означає проведення.", 13);
        Button(card, "Кошик документів (відновлення до 30 днів)", ShowArchiveTrash);
        var search = ProfileInput(card, "Пошук за номером, типом, датою", _documentQuery);
        var rows = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.AddView(rows);

        // One read on entering the screen; changing pages and searching does not
        // requery SQLite or change any stored document.
        try
        {
            var policy=new MobileAccessStore(this).Load()!;
            var documents=(_allMobileUnits && !policy.isMvo?policy.units.SelectMany(u=>_archive.ListSummaries(u.unit)):_archive.ListSummaries(profile.Unit))
                .DistinctBy(d=>d.DocumentId).OrderByDescending(d=>d.DocumentDate,StringComparer.Ordinal).ThenByDescending(d=>d.SavedUtc,StringComparer.Ordinal).ToList();

            void Render()
            {
                rows.RemoveAllViews();
                var filter = _documentQuery.Trim();
                var visible = documents.Where(d => filter.Length == 0 ||
                    d.Number.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    d.Kind.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    d.DocumentDate.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                var page = DocumentPageWindow.For(visible.Count, _documentPage);
                _documentPage = page.PageIndex;
                Text(rows, $"Документів: {visible.Count} із {documents.Count}", 13);
                Text(rows, $"Сторінка {page.PageIndex + 1} із {page.PageCount} · " +
                    (page.ItemCount == 0 ? "Записів немає" :
                    $"показано {page.StartIndex + 1}–{page.StartIndex + page.ItemCount}"), 13);

                void AddNavigation()
                {
                    if (page.PageCount <= 1) return;
                    var nav = new LinearLayout(this) { Orientation = Orientation.Horizontal };
                    nav.SetGravity(GravityFlags.CenterVertical);
                    rows.AddView(nav);

                    void AddPageButton(string label, int targetIndex, bool enabled)
                    {
                        var button = new Android.Widget.Button(this)
                        {
                            Text = label,
                            TextSize = 13,
                            Enabled = enabled
                        };
                        button.SetAllCaps(false);
                        button.SetMinHeight(Dp(48));
                        button.SetTextColor(enabled ? _theme.AccentText :
                            _theme.MutedText);
                        button.Background = Rounded(enabled ? _theme.SoftAction :
                            _theme.MutedAction, 12);
                        var layout = new LinearLayout.LayoutParams(0, -2, 1);
                        layout.SetMargins(Dp(2), Dp(3), Dp(2), Dp(3));
                        nav.AddView(button, layout);
                        button.Click += (_, _) =>
                        {
                            _documentPage = targetIndex;
                            Render();
                            _scroll.SmoothScrollTo(0, 0);
                        };
                    }

                    AddPageButton("← Попередня", page.PageIndex - 1, page.PageIndex > 0);
                    var pageLabel = new TextView(this)
                    {
                        Text = $"{page.PageIndex + 1} / {page.PageCount}",
                        TextSize = 13,
                        Gravity = GravityFlags.Center
                    };
                    pageLabel.SetTextColor(_theme.AccentText);
                    nav.AddView(pageLabel, new LinearLayout.LayoutParams(Dp(62), -2));
                    AddPageButton("Наступна →", page.PageIndex + 1,
                        page.PageIndex + 1 < page.PageCount);
                }

                if (visible.Count == 0)
                {
                    Text(rows, filter.Length == 0 ? "В архіві поки немає документів." :
                        "За заданим пошуком документів не знайдено.", 14);
                    return;
                }

                AddNavigation(); // Visible without scrolling past 50 records.
                foreach (var entry in visible.Skip(page.StartIndex).Take(page.ItemCount))
                {
                    var item = entry;
                    var tile = new LinearLayout(this) { Orientation = Orientation.Vertical };
                    rows.AddView(tile);
                    Text(tile, item.Kind + " № " + item.Number, 16, true);
                    Text(tile, item.DocumentDate + " · " + item.Unit +
                        " · " + item.Status, 13);
                    Text(tile,item.PostingStatus switch{"posted"=>"Зараховано локально","superseded"=>"Застаріла версія — використайте оновлений документ",_=>item.Kind=="Накладна"?"Очікує локального зарахування":"Збережено для перегляду"},13);
                    Button(tile, "Переглянути документ", () => ShowArchivedDocument(item.DocumentId));
                    if (ArchiveTrashPolicy.CanMoveToTrash(item.PostingStatus))
                        Button(tile, "У кошик", () =>
                            new AlertDialog.Builder(this)
                                .SetTitle("Прибрати документ з активного архіву?")
                                .SetMessage("«" + item.Number + "» залишатиметься в кошику " +
                                    "30 днів. Ключі, Gmail і облік не зміняться.")
                                .SetNegativeButton("Скасувати", (_, _) => { })
                                .SetPositiveButton("У кошик", (_, _) =>
                                {
                                    try
                                    {
                                        _archive.MoveToTrash(item.DocumentId, item.Unit);
                                        ShowDocuments();
                                    }
                                    catch (Exception ex)
                                    {
                                        Toast.MakeText(this, ExplainOperationError(ex),
                                            ToastLength.Long)?.Show();
                                    }
                                }).Show());
                    else Text(tile, "Проведений документ: видалення з архіву заблоковано.", 12);
                }
                AddNavigation(); // Also accessible at the end of the current page.
            }

            search.TextChanged += (_, _) =>
            {
                _documentQuery = search.Text ?? "";
                _documentPage = 0;
                Render();
            };
            Render();
        }
        catch (Exception)
        {
            Text(rows, "Не вдалося прочитати архів. Спробуй відкрити історію ще раз.", 14);
        }
    }

    private void ShowArchiveTrash()
    {
        Begin("archive-trash", "Кошик документів");
        var profile = LoadUserProfile();
        if (profile is null)
        {
            Text(_body, "Спочатку налаштуй профіль.", 14);
            return;
        }
        var card = Card();
        Text(card, "Видалені документи · " + profile.Unit, 18, true);
        Text(card, "Відновлення доступне протягом 30 днів. Проведені накладні " +
            "не можна видаляти через кошик. Очищення прострочених документів " +
            "не впливає на ключі чи Gmail.", 13);
        Button(card, "Повернутися до архіву", ShowDocuments);
        var list = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.AddView(list);
        void Reload()
        {
            list.RemoveAllViews();
            try
            {
                var deleted = _archive.ListTrash(profile.Unit);
                Text(list, "Документів у кошику: " + deleted.Count, 13);
                foreach (var item in deleted)
                {
                    var row = new LinearLayout(this) { Orientation = Orientation.Vertical };
                    list.AddView(row);
                    Text(row, item.Kind + " № " + item.Number + " · " + item.DocumentDate, 15, true);
                    Text(row, "Переміщено: " + item.DeletedUtc, 12);
                    if (ArchiveTrashPolicy.CanRestore(item.DeletedUtc, DateTimeOffset.UtcNow))
                        Button(row, "Відновити документ", () =>
                        {
                            try
                            {
                                _archive.RestoreFromTrash(item.DocumentId, profile.Unit);
                                Reload();
                            }
                            catch (Exception ex)
                            {
                                Toast.MakeText(this, ExplainOperationError(ex),
                                    ToastLength.Long)?.Show();
                            }
                        });
                    else Text(row, "Строк відновлення минув.", 12);
                }
                Button(list, "Очистити документи старші за 30 днів", () =>
                    new AlertDialog.Builder(this)
                        .SetTitle("Остаточно видалити прострочені документи?")
                        .SetMessage("Лише документи, що перебувають у кошику 30 днів " +
                            "або довше. Цю дію скасувати неможливо.")
                        .SetNegativeButton("Скасувати", (_, _) => { })
                        .SetPositiveButton("Видалити прострочені", (_, _) =>
                        {
                            try
                            {
                                int removed = _archive.PurgeExpiredTrash(profile.Unit);
                                Toast.MakeText(this, "Очищено документів: " + removed,
                                    ToastLength.Long)?.Show();
                                Reload();
                            }
                            catch (Exception ex)
                            {
                                Toast.MakeText(this, ExplainOperationError(ex),
                                    ToastLength.Long)?.Show();
                            }
                        }).Show());
            }
            catch (Exception ex)
            {
                Text(list, "Кошик недоступний: " + ExplainOperationError(ex), 13);
            }
        }
        Reload();
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
                                        ExplainOperationError(ex) +
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

                Text(exportCard, "Excel формується як справжня книга XLSX, " +
                    "після чого отримує парольне шифрування Microsoft Office. " +
                    "Відкритий XLSX не записується до сховища телефону.", 12);
                Button(exportCard, "Зберегти Excel із паролем", () =>
                {
                    if (_busy) return;
                    var secret = new EditText(this)
                    {
                        Hint = "Пароль Excel (12–128 символів)",
                        InputType = InputTypes.ClassText | InputTypes.TextVariationPassword
                    };
                    secret.TransformationMethod =
                        Android.Text.Method.PasswordTransformationMethod.Instance;
                    new AlertDialog.Builder(this)
                        .SetTitle("Пароль для захищеного Excel")
                        .SetMessage("Пароль не зберігається у програмі. " +
                            "Для відкриття XLSX у Excel потрібен цей пароль.")
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
                                exportStatus.Text = "Генеруємо захищений Excel...";
                                bytes = await Task.Run(() =>
                                    ProtectedXlsxBuilder.CreateInvoice(this, invoice, password));
                                var cleanNumber = new string(invoice.Number.Where(ch =>
                                    char.IsLetterOrDigit(ch) || ch is '-' or '_')
                                    .Take(55).ToArray());
                                if (string.IsNullOrWhiteSpace(cleanNumber))
                                    cleanNumber = "document";
                                var name = "Документ-" + cleanNumber + "-" +
                                    DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".xlsx";
                                var protectedBytes = bytes;
                                await Task.Run(() =>
                                    _exportedFiles.SaveProtectedXlsx(protectedBytes, name));
                                if (_screen == "message")
                                    exportStatus.Text =
                                        "Захищений Excel збережено у МаVКа файли.";
                            }
                            catch (Exception ex)
                            {
                                if (_screen == "message")
                                    exportStatus.Text = "Не вдалося створити XLSX: " +
                                        ExplainOperationError(ex) +
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
                Text(exportCard, "Можна зберегти Word окремим файлом .docx.mavkafile: " +
                    "його вміст захищений ключем обміну та паролем документів. " +
                    "Це контейнер Мавки, а не відкритий DOCX.", 12);
                Button(exportCard, "Зберегти запаролений Word (.mavkafile)", async () =>
                {
                    if (_busy) return;
                    if (!_exchangeKeys.HasKeyPair)
                    {
                        exportStatus.Text = "Спочатку встанови ключі обміну.";
                        return;
                    }
                    var documentPassword = _documentPasswords.Load();
                    if (string.IsNullOrWhiteSpace(documentPassword))
                    {
                        exportStatus.Text = "Спочатку налаштуй пароль документів.";
                        return;
                    }
                    _busy = true;
                    byte[]? plain = null, key = null;
                    try
                    {
                        exportStatus.Text = "Створюємо подвійно зашифрований Word...";
                        plain = await Task.Run(() => DocxInvoiceBuilder.Create(invoice));
                        key = _exchangeKeys.GetCurrentKey() ??
                            throw new KeyNotFoundException("Поточний ключ не знайдено.");
                        var cleanNumber = new string(invoice.Number.Where(ch =>
                            char.IsLetterOrDigit(ch) || ch is '-' or '_').Take(55).ToArray());
                        if (cleanNumber.Length == 0) cleanNumber = "document";
                        var wordFileName = "Накладна-" + cleanNumber + ".docx";
                        var inner = MavkaEncryptedFileEnvelope.Encrypt(wordFileName, plain, key);
                        var protectedFile = MavkaPasswordedEnvelope.Wrap(inner, documentPassword);
                        // Check password layer before publishing encrypted bytes.
                        if (!string.Equals(MavkaPasswordedEnvelope.Unwrap(
                            protectedFile, documentPassword), inner, StringComparison.Ordinal))
                            throw new InvalidOperationException("Парольну перевірку не пройдено.");
                        var outputName = wordFileName + ".mavkafile";
                        await Task.Run(() =>
                            _exportedFiles.SavePasswordedFile(protectedFile, outputName));
                        if (_screen == "message")
                            exportStatus.Text = "Зашифрований і запаролений Word збережено у МаVКа файли.";
                    }
                    catch (Exception ex)
                    {
                        if (_screen == "message")
                            exportStatus.Text = "Не вдалося зберегти Word: " +
                                ExplainOperationError(ex);
                    }
                    finally
                    {
                        if (plain is not null)
                            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain);
                        if (key is not null)
                            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
                        _busy = false;
                    }
                });
                Text(exportCard, "Word формується в пам’яті та надсилається " +
                    "ГОЛОВНІЙ МАВЦІ лише як зашифроване вкладення .docx.mavkafile. " +
                    "Незахищений DOCX не записується до завантажень.", 12);
                Button(exportCard, "Надіслати захищений Word (DOCX)", async () =>
                {
                    if (_busy) return;
                    if ((!_store.HasCredentials && !MobileExchangeTransport.RelayEnabled(this)) || !_exchangeKeys.HasKeyPair)
                    {
                        exportStatus.Text = "Потрібні Gmail або Relay та чинна пара ключів.";
                        return;
                    }
                    _busy = true;
                    byte[]? wordBytes = null;
                    byte[]? currentKey = null;
                    try
                    {
                        exportStatus.Text = "Формуємо й шифруємо Word...";
                        wordBytes = await Task.Run(() => DocxInvoiceBuilder.Create(invoice));
                        currentKey = _exchangeKeys.GetCurrentKey() ??
                            throw new KeyNotFoundException("Поточний ключ недоступний.");
                        var cleanNumber = new string(invoice.Number.Where(ch =>
                            char.IsLetterOrDigit(ch) || ch is '-' or '_').Take(55).ToArray());
                        if (string.IsNullOrWhiteSpace(cleanNumber)) cleanNumber = "document";
                        string fileName = "Накладна-" + cleanNumber + ".docx";
                        string ciphertext = MavkaEncryptedFileEnvelope.Encrypt(
                            fileName, wordBytes, currentKey);
                        var documentPassword = _documentPasswords.Load() ??
                            throw new InvalidOperationException("Налаштуй пароль документів.");
                        ciphertext = MavkaPasswordedEnvelope.Wrap(ciphertext, documentPassword);
                        await MobileExchangeTransport.SendProtectedFileAsync(this,
                            _store.GetEmail(), _store.GetPassword() ?? "",
                            _store.GetMainMavkaEmail(), fileName, ciphertext);
                        if (_screen == "message")
                            exportStatus.Text = "Захищений Word передано каналу обміну. " +
                                "Відкритої копії не створено.";
                    }
                    catch (Exception ex)
                    {
                        if (_screen == "message")
                            exportStatus.Text = "Не вдалося відправити Word: " +
                                ExplainOperationError(ex);
                    }
                    finally
                    {
                        if (currentKey is not null)
                            System.Security.Cryptography.CryptographicOperations.ZeroMemory(currentKey);
                        if (wordBytes is not null)
                            System.Security.Cryptography.CryptographicOperations.ZeroMemory(wordBytes);
                        _busy = false;
                    }
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
                .SetMessage(ExplainOperationError(ex))
                .SetPositiveButton("Закрити", (_, _) => { }).Show();
        }
    }

    private void ShowFiles()
    {
        Begin("files", "Бібліотека документів");
        var header = Card();
        Text(header, "Експортовані PDF та Excel", 18, true);
        Text(header, "Тільки копії з папки Download/МаVКа файли. " +
            "Видалення цих копій не видаляє накладні, залишки, " +
            "підтвердження чи історію з внутрішньої бази Мавки.", 13);

        Text(header, "Файли, завантажені в Gmail чи сторонніх програмах, " +
            "можна вибрати нижче для перегляду. Це НЕ імпорт до архіву.", 12);

        var progressCard = Card();
        Text(progressCard, "Передачі файлів", 18, true);
        Text(progressCard, "Частини зберігаються до завершення передачі навіть після перезапуску. Межа файла: 32 МБ.", 12);
        async Task RenderTransfersAsync()
        {
            try
            {
                var pending = Transfers();
                var entries = await Task.Run(() => pending.List());
                if (_screen != "files") return;
                if (entries.Count == 0) Text(progressCard, "Незавершених передач немає.", 13);
                foreach (var entry in entries)
                {
                    Text(progressCard, entry.Name + $" · {entry.Received}/{entry.Total}" + (entry.Saved ? " · файл збережено" : ""), 13);
                    Button(progressCard, "Видалити частини цієї передачі", () =>
                        new AlertDialog.Builder(this).SetTitle("Видалити незавершену передачу?")
                        .SetMessage("Видаляються лише локальні частини. Листи Gmail й уже збережені документи залишаються.")
                        .SetNegativeButton("Скасувати", (_, _) => { })
                        .SetPositiveButton("Видалити", async (_, _) =>
                        {
                            if (_busy) return; _busy = true;
                            try { await Task.Run(() => pending.Delete(entry.Id)); if (_screen == "files") ShowFiles(); }
                            catch { Toast.MakeText(this, "Не вдалося видалити передачу.", ToastLength.Long)?.Show(); }
                            finally { _busy = false; }
                        }).Show());
                }
            }
            catch { if (_screen == "files") Text(progressCard, "Не вдалося прочитати передачі.", 12); }
        }
        _ = RenderTransfersAsync();
        var outgoingStatus = Text(progressCard, "Перервані вихідні передачі зберігаються у зашифрованій черзі.", 12);
        Button(progressCard, "Продовжити надсилання частин", async () =>
        {
            if (_busy) return; _busy = true;
            try { await SendPendingFilePartsAsync(progress: text => outgoingStatus.Text = text); outgoingStatus.Text = "Чергу надсилання оброблено."; }
            catch { outgoingStatus.Text = "Надсилання призупинено. Решта частин збережена; перевір Gmail і мережу."; }
            finally { _busy = false; }
        });
        Button(progressCard, "Видалити чергу надсилання файлів", () =>
            new AlertDialog.Builder(this).SetTitle("Видалити вихідні частини?")
                .SetMessage("Видаляються лише ще не завершені локальні передачі. Листи, які вже надіслано, залишаються у Gmail.")
                .SetNegativeButton("Скасувати", (_, _) => { })
                .SetPositiveButton("Видалити", async (_, _) =>
                {
                    if (_busy) return; _busy = true;
                    try { var q = OutgoingFiles(); await Task.Run(() => { foreach (var b in q.List()) q.Delete(b.Metadata.TransferId); }); if (_screen == "files") ShowFiles(); }
                    catch { Toast.MakeText(this, "Не вдалося видалити чергу.", ToastLength.Long)?.Show(); }
                    finally { _busy = false; }
                }).Show());
        var receivedCard = Card();
        Text(receivedCard, "Отримані PDF, Excel, Word та зображення", 18, true);
        Text(receivedCard, "Окреме зашифроване сховище Android. " +
            "Ці файли не є документами облікового архіву.", 12);
        var receivedList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        receivedCard.AddView(receivedList);
        async Task ReloadReceivedAsync()
        {
            receivedList.RemoveAllViews();
            Text(receivedList, "Читаємо захищене сховище...", 12);
            try
            {
                var entries = await Task.Run(() => _privateFiles.List());
                if (_screen != "files") return;
                receivedList.RemoveAllViews();
                if (entries.Count == 0)
                    Text(receivedList, "Захищених документів і зображень поки немає.", 13);
                // Creating 200+ Android controls at once stalls older phones.
                // Render a bounded batch while retaining access to all 100 entries.
                int displayed = 0;
                var entriesPanel = new LinearLayout(this) { Orientation = Orientation.Vertical };
                receivedList.AddView(entriesPanel);
                Android.Widget.Button? moreButton = null;
                moreButton = Button(receivedList, "Показати ще 20 файлів", AppendNextFiles);
                void AppendNextFiles()
                {
                foreach (var entry in entries.Skip(displayed).Take(20))
                {
                    var label = Text(entriesPanel, entry.Name + " · " +
                        (entry.Size / 1024d).ToString("0.#") + " КБ", 13, true);
                    label.SetMaxLines(3);
                    label.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
                    if (entry.Mime.StartsWith("image/", StringComparison.Ordinal))
                        Button(entriesPanel, "Переглянути захищене зображення", async () =>
                        {
                            if (_busy) return;
                            _busy = true;
                            try
                            {
                                using var photo = await Task.Run(() => _privateFiles.Load(entry.Id));
                                ShowMailImagePreview(photo.FileName, photo.Content);
                            }
                            catch (Exception)
                            {
                                Toast.MakeText(this, "Не вдалося відкрити захищене зображення.",
                                    ToastLength.Long)?.Show();
                            }
                            finally { _busy = false; }
                        });
                    if (entry.Name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
                        entry.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                        Button(entriesPanel, "Відкрити в бібліотеці", async () =>
                        {
                            if (_busy) return;
                            _busy = true;
                            try
                            {
                                using var file = await Task.Run(() => _privateFiles.Load(entry.Id));
                                var preview = await Task.Run(() =>
                                    OfficeDocumentPreview.Read(file.FileName, file.Content));
                                new AlertDialog.Builder(this)
                                    .SetTitle(entry.Name)
                                    .SetMessage(preview)
                                    .SetPositiveButton("Закрити", (_, _) => { }).Show();
                            }
                            catch (Exception ex)
                            {
                                Toast.MakeText(this, "Не вдалося відкрити документ: " +
                                    ExplainOperationError(ex), ToastLength.Long)?.Show();
                            }
                            finally { _busy = false; }
                        });
                    Button(entriesPanel, "Зберегти розшифровану копію", () =>
                    {
                        if (_busy) return;
                        new AlertDialog.Builder(this)
                            .SetTitle("Експортувати розшифровану копію?")
                            .SetMessage("За межами Мавки цей файл уже не матиме " +
                                "захисту Android Keystore. Вибери особисту папку; " +
                                "не надсилай файл стороннім особам.")
                            .SetNegativeButton("Скасувати", (_, _) => { })
                            .SetPositiveButton("Обрати папку", (_, _) =>
                            {
                                try
                                {
                                    _pendingPrivateExportId = entry.Id;
                                    var create = new Intent(Intent.ActionCreateDocument);
                                    create.AddCategory(Intent.CategoryOpenable);
                                    create.SetType(entry.Mime);
                                    create.PutExtra(Intent.ExtraTitle, entry.Name);
                                    StartActivityForResult(create, ExportPrivateCopyRequest);
                                }
                                catch (Exception)
                                {
                                    _pendingPrivateExportId = null;
                                    Toast.MakeText(this,
                                        "Не вдалося відкрити системний вибір папки.",
                                        ToastLength.Long)?.Show();
                                }
                            }).Show();
                    });
                    Button(entriesPanel, "Видалити локальну захищену копію", () =>
                    {
                        if (_busy) return;
                        new AlertDialog.Builder(this)
                            .SetTitle("Видалити захищений файл?")
                            .SetMessage("Файл «" + entry.Name +
                                "» буде видалено лише з окремого сховища телефону. " +
                                "Це не змінює ключі, Gmail чи обліковий архів.")
                            .SetNegativeButton("Скасувати", (_, _) => { })
                            .SetPositiveButton("Видалити", async (_, _) =>
                            {
                                if (_busy) return;
                                _busy = true;
                                try
                                {
                                    await Task.Run(() => _privateFiles.Delete(entry.Id));
                                    if (_screen == "files") await ReloadReceivedAsync();
                                }
                                catch (Exception)
                                {
                                    Toast.MakeText(this,
                                        "Не вдалося видалити захищений файл.",
                                        ToastLength.Long)?.Show();
                                }
                                finally { _busy = false; }
                            }).Show();
                    });
                }
                    displayed = Math.Min(displayed + 20, entries.Count);
                    if (moreButton is not null)
                    {
                        moreButton.Visibility = displayed >= entries.Count
                            ? ViewStates.Gone : ViewStates.Visible;
                        moreButton.Text = "Показати ще 20 (показано " + displayed +
                            " із " + entries.Count + ")";
                    }
                }
                AppendNextFiles();
            }
            catch (Exception)
            {
                if (_screen == "files")
                {
                    receivedList.RemoveAllViews();
                    Text(receivedList, "Не вдалося прочитати захищені файли.", 12);
                }
            }
        }
        Button(receivedCard, "Оновити захищені файли", () => _ = ReloadReceivedAsync());
        _ = ReloadReceivedAsync();
        Button(header, "Надіслати PDF/Excel/Word із захистом ключами", () =>
        {
            if ((!_store.HasCredentials && !MobileExchangeTransport.RelayEnabled(this)) || !_exchangeKeys.HasKeyPair)
            {
                Toast.MakeText(this,
                    "Підключи Gmail або Relay та встанови ключі обміну.",
                    ToastLength.Long)?.Show();
                return;
            }
            new AlertDialog.Builder(this)
                .SetTitle("Надіслати захищений файл?")
                .SetMessage("Обери файл до 32 МБ. Мавка розділить його на частини по 256 КБ, зашифрує чинним " +
                    "ключем AES-256-GCM і надішле до налаштованої адреси Gmail. " +
                    "Відкритий файл у лист не вкладатиметься.")
                .SetNegativeButton("Скасувати", (_, _) => { })
                .SetPositiveButton("Обрати файл", (_, _) =>
                {
                    try
                    {
                        var picker = new Intent(Intent.ActionOpenDocument);
                        picker.AddCategory(Intent.CategoryOpenable);
                        picker.SetType("*/*");
                        picker.PutExtra(Intent.ExtraMimeTypes, new[]
                        {
                            "application/pdf",
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/msword"
                        });
                        StartActivityForResult(picker, PickEncryptAndSendRequest);
                    }
                    catch (Exception)
                    {
                        Toast.MakeText(this, "Системний вибір файлів недоступний.",
                            ToastLength.Long)?.Show();
                    }
                }).Show();
        });
        Button(header, "Відкрити PDF / Excel / Word з пам'яті телефону", () =>
        {
            try
            {
                var picker = new Intent(Intent.ActionOpenDocument);
                picker.AddCategory(Intent.CategoryOpenable);
                picker.SetType("*/*");
                picker.PutExtra(Intent.ExtraMimeTypes, new[]
                {
                    "application/pdf",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/msword"
                });
                StartActivityForResult(picker, PickDocumentRequest);
            }
            catch (Android.Content.ActivityNotFoundException)
            {
                Toast.MakeText(this, "Системний вибір файлів недоступний.",
                    ToastLength.Long)?.Show();
            }
        });

        if (!_exportedFiles.IsSupported)
        {
            Text(header, "Перегляд локальних файлів доступний через системний вибір. " +
                "Автоматичний список експорту потребує Android 10 або новішого.", 14);
            return;
        }

        var search = ProfileInput(header, "Пошук файлів PDF / XLSX", "");
        var output = new LinearLayout(this) { Orientation = Orientation.Vertical };
        header.AddView(output);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        bool nameSorting = false;
        IReadOnlyList<MavkaExportFile> files = Array.Empty<MavkaExportFile>();
        string? readError = null;
        bool fileListLoading = false;
        Android.Widget.Button? deleteSelectedButton = null;
        Android.Widget.Button? clearFolderButton = null;
        void UpdateFileActions()
        {
            if (deleteSelectedButton is not null)
            {
                deleteSelectedButton.Enabled = selected.Count > 0;
                deleteSelectedButton.Alpha = deleteSelectedButton.Enabled ? 1f : 0.42f;
            }
            if (clearFolderButton is not null)
            {
                clearFolderButton.Enabled = files.Count > 0;
                clearFolderButton.Alpha = clearFolderButton.Enabled ? 1f : 0.42f;
            }
        }

        // Searching/sorting never hits ContentResolver; MediaStore is queried
        // only on entering the screen or pressing Refresh.
        void RenderList()
        {
            output.RemoveAllViews();
            if (readError is not null)
            {
                Text(output, readError, 14);
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
                Text(output, "Експортованих PDF/Excel/Word немає або Android не надав " +
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
                    UpdateFileActions();
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
                        Toast.MakeText(this,
                            "Не вдалося відкрити файл. Перевір наявність PDF/Excel " +
                            "переглядача та доступ до файлу.",
                            ToastLength.Long)?.Show();
                    }
                });
                Button(tile, "Поділитися", () =>
                {
                    try { _exportedFiles.ShareFile(item); }
                    catch (Exception ex)
                    {
                        Toast.MakeText(this,
                            "Не вдалося поділитися файлом. Перевір доступ до Downloads.",
                            ToastLength.Long)?.Show();
                    }
                });
            }
            if (visible.Count > 100)
                Text(output, "Відображено перші 100 файлів. Скористайся пошуком.", 13);
            UpdateFileActions();
        }

        async Task ReloadListAsync()
        {
            if (fileListLoading) return;
            fileListLoading = true;
            readError = null;
            Text(output, "Оновлюємо список файлів...", 12);
            try
            {
                // ContentResolver queries may take time on phones with large
                // Downloads folders. Keep I/O away from the UI thread.
                var loaded = await Task.Run(() => _exportedFiles.ListFiles());
                if (_screen != "files") return;
                files = loaded;
                selected.IntersectWith(
                    files.Select(item => item.Uri.ToString()!));
            }
            catch (Exception ex)
            {
                if (_screen != "files") return;
                files = Array.Empty<MavkaExportFile>();
                readError = "Не вдалося відкрити папку експорту. " +
                    "Перевір дозволи Android та спробуй «Оновити список».";
                selected.Clear();
            }
            finally
            {
                fileListLoading = false;
                if (_screen == "files") RenderList();
            }
        }

        search.TextChanged += (_, _) => RenderList();
        var sortLabel = Text(header, "Сортування: за датою", 12);
        Button(header, "Змінити сортування", () =>
        {
            nameSorting = !nameSorting;
            sortLabel.Text = "Сортування: " + (nameSorting ? "за назвою" : "за датою");
            RenderList();
        });
        Button(header, "Оновити список", () => { _ = ReloadListAsync(); });

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
                Toast.MakeText(this,
                    "Не вдалося перевірити файли для видалення. Онови список.",
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
                    "\n\nБудуть видалені лише PDF/Excel/Word-копії з папки експорту. " +
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
                                    error = ExplainOperationError(ex);
                                    break;
                                }
                            }
                        });
                    }
                    finally { _busy = false; }
                    if (_screen == "files") ShowFiles();
                    Toast.MakeText(this, "Видалено: " + deleted +
                        (error is null ? "" : "; частина файлів залишилася. Перевір доступ."),
                        ToastLength.Long)?.Show();
                }).Show();
        }

        deleteSelectedButton = Button(header, "Видалити вибрані", () => ConfirmDeletion(false));
        clearFolderButton = Button(header, "Очистити папку", () => ConfirmDeletion(true));
        UpdateFileActions();
        _ = ReloadListAsync();
    }

    private void ShowDrawer(bool left)
    {
        _leftDrawer.Visibility = left ? ViewStates.Visible : ViewStates.Gone;
        _rightDrawer.Visibility = left ? ViewStates.Gone : ViewStates.Visible;
        _drawerOverlay.Visibility = ViewStates.Visible;
        _drawerOverlay.BringToFront();
    }

    private void CloseDrawer()
    {
        _drawerOverlay.Visibility = ViewStates.Gone;
    }

    private string? _selectedMobileUnit;
    private bool _allMobileUnits;
    private void ChooseMobileUnit(Action refresh)
    {
        var policy=new MobileAccessStore(this).Load();
        if(policy is null){Toast.MakeText(this,"Спочатку виконай нове QR-підключення.",ToastLength.Long)?.Show();return;}
        var combined=!policy.isMvo && policy.units.Length>1;
        var labels=(combined?new[]{"Усі доступні підрозділи"}:Array.Empty<string>()).Concat(policy.units.Select(u=>u.unit)).ToArray();
        new AlertDialog.Builder(this).SetTitle("Перегляд майна й документів")
            .SetItems(labels,(_,args)=>{_allMobileUnits=combined && args.Which==0;if(!_allMobileUnits)_selectedMobileUnit=policy.units[args.Which-(combined?1:0)].unit;_documentPage=0;refresh();}).Show();
    }
    private UserProfile? LoadUserProfile()
    {
        try
        {
            var profile=_profileStore.Load();var access=new MobileAccessStore(this).Load();
            if(profile is null || access is null)return profile;
            var unit=access.units.FirstOrDefault(u=>u.unit==_selectedMobileUnit)?.unit??access.units[0].unit;
            return profile with{Unit=unit,IsMvo=access.isMvo};
        }
        catch { return null; }
    }

    private void ShowProfile()
    {
        Begin("profile", "Профіль користувача");
        var current = LoadUserProfile();
        var card = Card();
        Text(card,"Профіль для реєстрації через QR з головної Мавки на ПК",15,true);

        var pairingCard = Card();
        Text(pairingCard, "Зв'язок із ГОЛОВНОЮ МАВКОЮ через QR", 18, true);
        Text(pairingCard, "ПК створює QR; телефон тільки сканує його. Спочатку збережи профіль, підключи Gmail і введи спільний пароль обміну вручну. " +
            "QR містить разовий ключ сеансу. Після захищеної заявки й дозволу ПК надійдуть поточний та майбутній ключі й дозволи. Пароль не передається. Термін — 10 хвилин.", 13);
        var qrStatus = Text(pairingCard,
            _qrPairing.ConfirmedEmail is { } pairedEmail
                ? "QR-зв'язок підтверджено: " + pairedEmail
                : "Підключення QR ще не підтверджено.", 13);
        Button(pairingCard, "Сканувати QR камерою", () =>
        {
            try
            {
                var intent = new Intent(this,typeof(LiveQrScannerActivity));
                StartActivityForResult(intent, QrCameraCaptureRequest);
            }
            catch (Exception)
            {
                qrStatus.Text = "Камера недоступна. Скористайся фото QR-коду.";
            }
        });
        Button(pairingCard, "Вибрати фото QR-коду", () =>
        {
            var intent = new Intent(Intent.ActionGetContent);
            intent.SetType("image/*");
            intent.AddCategory(Intent.CategoryOpenable);
            StartActivityForResult(Intent.CreateChooser(intent, "Фото QR-коду Мавки"),
                QrImagePickRequest);
        });
        Text(pairingCard, "Після сканування підтверди адресу і надішли запит. " +
            "Для перевірки відповіді обери ☰ → Синхронізація Gmail.", 12);

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
                    "Помилка збереження профілю: " + ExplainOperationError(ex);
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

        var sendButton = new Android.Widget.Button(this) { Text = "Надіслати оновлення профілю" };
        StylePrimaryButton(sendButton);
        sendButton.Visibility=_exchangeKeys.HasKeyPair?ViewStates.Visible:ViewStates.Gone;
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
                .SetTitle("Надіслати захищене оновлення профілю?")
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
                            sender, password, recipient, profile, _exchangeKeys, stage => RunOnUiThread(() => { if(_screen=="profile")status.Text="Реєстрація: "+new RegistrationMailFailure(stage,new Exception()).StageName+"…"; }));
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
                            status.Text = ex is RegistrationMailFailure failure ? failure.UserMessage : new RegistrationMailFailure(RegistrationMailStage.Credentials,ex).UserMessage;
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
        Text(card, "Ключі після QR-реєстрації", 18, true);
        Text(card, "Збережи профіль, підключи Gmail і введи спільний пароль обміну вручну. У профілі скануй QR з ПК. Після дозволу на ПК програма отримає захищений пакет поточного й майбутнього ключів. Пароль не надсилається.", 14);
        var status = Text(card, _exchangeKeys.HasKeyPair
            ? "Покоління: " + _exchangeKeys.Generation +
              "\nПоточний ключ: " + _exchangeKeys.CurrentKeyId +
              "\nМайбутній ключ: " + _exchangeKeys.FutureKeyId
            : "Немає автоматично встановленої пари ключів.", 15);

        Text(card, "Нові ключі імпортуються під час звичайної синхронізації: " +
            "☰ → Синхронізація Gmail → Оновити повідомлення.", 13);
        Text(card, "Усі нові повідомлення захищаються спільним паролем та ключем обміну. До отримання ключів дозволено лише захищену заявку після сканування QR з ПК.", 13);
        Button(card, "Відкрити профіль і сканувати QR з ПК", ShowProfile);
        Button(card, "Ввести спільний пароль обміну", ShowDocumentPassword);

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
                _inboxCache.Clear();
                        status.Text = "Ключі видалено. Для повторного отримання потрібна нова реєстрація.";
                    }
                    catch (Exception ex) { status.Text = "Помилка: " + ExplainOperationError(ex); }
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
            : "Введи той самий пароль, що збережений у головній Мавці на ПК.", 14);

        var input = new EditText(this)
        {
            Hint = "Пароль документів",
            Text = "",
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
                _inboxCache.Clear();
                status.Text = "Пароль збережено. Наступні захищені файли читатимуться автоматично.";
            }
            catch (ArgumentException)
            {
                status.Text = "Пароль має містити від 6 до 128 символів.";
            }
            catch (Exception ex)
            {
                status.Text = "Помилка захищеного збереження: " + ExplainOperationError(ex);
            }
        });
        Text(card, "Зміна пароля впливає на нові файли. Якщо ГОЛОВНА МАВКА " +
            "ще використовує попередній пароль, нові пакети ключів не відкриються.", 13);
    }

    private void ShowThemeChooser()
    {
        if (_busy)
        {
            Toast.MakeText(this, "Дочекайся завершення поточної операції.",
                ToastLength.Short)?.Show();
            return;
        }
        AlertDialog? chooser = null;
        chooser = new AlertDialog.Builder(this)
            .SetTitle("Колірна тема Мавки")
            .SetSingleChoiceItems(new[] { "Оливкова", "Синя" },
                _theme.IsBlue ? 1 : 0, (_, args) =>
                {
                    chooser?.Dismiss();
                    string chosen = args.Which == 1
                        ? MavkaThemePolicy.Blue : MavkaThemePolicy.Olive;
                    if (chosen == _theme.Id) return;
                    try
                    {
                        MavkaThemePalette.Save(this, chosen);
                        // Recreate applies every native color consistently,
                        // including drawers, cards, search and Android status bar.
                        // SavedInstanceState returns the user to Settings.
                        Recreate();
                    }
                    catch (Exception)
                    {
                        Toast.MakeText(this, "Не вдалося зберегти вибір теми.",
                            ToastLength.Long)?.Show();
                    }
                })
            .SetNegativeButton("Скасувати", (_, _) => { })
            .Create();
        chooser.Show();
    }

    private void PromptBackupPassword(bool importing)
    {
        if (_busy) return;
        var input = new EditText(this)
        {
            Hint = "Пароль резервної копії (12–128 символів)",
            InputType = InputTypes.ClassText | InputTypes.TextVariationPassword
        };
        input.TransformationMethod = Android.Text.Method.PasswordTransformationMethod.Instance;
        new AlertDialog.Builder(this)
            .SetTitle(importing ? "Відновити зашифровану копію?" : "Створити зашифровану копію?")
            .SetMessage(importing
                ? "Дані можна відновити тільки в порожній обліковій базі відповідного підрозділу. " +
                  "Перед відновленням переконайся, що зробив резервну копію поточних даних."
                : "Збережи пароль окремо. Без нього файл не відновити.")
            .SetView(input)
            .SetNegativeButton("Скасувати", (_, _) => { })
            .SetPositiveButton("Далі", async (_, _) =>
            {
                var password = input.Text?.ToString() ?? "";
                if (password.Length is < 12 or > 128)
                {
                    Toast.MakeText(this, "Пароль має містити 12–128 символів.",
                        ToastLength.Long)?.Show();
                    return;
                }
                if (importing)
                {
                    _pendingBackupPassword = password;
                    var pick = new Intent(Intent.ActionOpenDocument);
                    pick.AddCategory(Intent.CategoryOpenable);
                    pick.SetType("application/octet-stream");
                    try { StartActivityForResult(pick, ImportBackupRequest); }
                    catch { _pendingBackupPassword = null; }
                    return;
                }
                _busy = true;
                try
                {
                    var service = new PortableBackupService(_archive, _ledger, _profileStore);
                    _pendingEncryptedBackup = await Task.Run(() => service.Create(password));
                    var save = new Intent(Intent.ActionCreateDocument);
                    save.AddCategory(Intent.CategoryOpenable);
                    save.SetType("application/octet-stream");
                    save.PutExtra(Intent.ExtraTitle,
                        "Mavka-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmm") + ".mavkabackup");
                    StartActivityForResult(save, ExportBackupRequest);
                }
                catch (Exception ex)
                {
                    if (_pendingEncryptedBackup is not null)
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(
                            _pendingEncryptedBackup);
                    _pendingEncryptedBackup = null;
                    Toast.MakeText(this, "Копію не створено: " + ExplainOperationError(ex),
                        ToastLength.Long)?.Show();
                }
                finally { _busy = false; }
            }).Show();
    }

    private void ShowGoogleDriveUpdate()
    {
        Begin("apk-update", "Оновлення через Google Drive");
        var card = Card();
        Text(card, "Оновлення Мавки без видалення даних", 18, true);
        Text(card, "Встав посилання Google Drive на APK. Файл має бути доступний " +
            "для завантаження за посиланням (без входу в Google). " +
            "Установлення можливе тільки для ua.mavka.mobile з новішим " +
            "versionCode та тим самим Android-сертифікатом підпису.", 13);
        Text(card, "На відміну від документів, APK не розшифровується ключем " +
            "обміну. Його справжність перевіряється підписом Android. " +
            "Оновити окрему QA31 таким способом не вийде: у неї інший пакет.",
            13);
        var link = ProfileInput(card,
            "https://drive.google.com/file/d/…/view", "");
        var status = Text(card,
            "Встав посилання або продовж установлення вже завантаженого APK.", 13);
        Button(card, "Завантажити / встановити оновлення", async () =>
        {
            if (_busy) return;
            _busy = true;
            var apkPath = UpdateApkContentProvider.StagedPath(this);
            try
            {
                var typed = (link.Text ?? "").Trim();
                if (typed.Length != 0)
                {
                    status.Text = "Завантаження APK із Google Drive...";
                    var progress = new Progress<(long Bytes, long? Total)>(value =>
                    {
                        if (_screen != "apk-update") return;
                        status.Text = value.Total is long total && total > 0
                            ? "Завантаження: " + (100 * value.Bytes / total) +
                              "% · " + (value.Bytes / 1024 / 1024) + " МБ"
                            : "Завантажено: " + (value.Bytes / 1024 / 1024) + " МБ";
                    });
                    await DriveApkDownloader.DownloadAsync(typed, apkPath, progress);
                }
                else if (!File.Exists(apkPath))
                    throw new ArgumentException("Спочатку встав посилання Google Drive.");
                status.Text = "Перевірка APK, версії та сертифіката підпису...";
                var verified = await Task.Run(() =>
                    AndroidApkUpdateInstaller.Verify(this, apkPath));
                if (_screen != "apk-update") return;
                status.Text = "APK перевірено: " + (verified.ApkBytes / 1024 / 1024) +
                    " МБ · Android versionCode " + verified.NewVersion +
                    ". Інсталятор відкривається лише після підтвердження.";
                new AlertDialog.Builder(this)
                    .SetTitle("Установити оновлення Мавки?")
                    .SetMessage("Поточна версія: " + verified.CurrentVersion +
                        "\nНова версія: " + verified.NewVersion +
                        "\nSHA-256: " + verified.ApkSha256 +
                        "\n\nПакет і підпис перевірено. " +
                        "Android додатково перевірить підпис перед установленням. " +
                        "Мавка не видаляє локальний облік, Gmail і ключі.")
                    .SetNegativeButton("Не зараз", (_, _) => { })
                    .SetPositiveButton("Продовжити", (_, _) =>
                    {
                        try
                        {
                            if (!AndroidApkUpdateInstaller.HasInstallerPermission(this))
                            {
                                status.Text = "Надай Android дозвіл на оновлення " +
                                    "з цього джерела. Потім повернися й натисни ту саму кнопку.";
                                AndroidApkUpdateInstaller.RequestInstallerPermission(this);
                                return;
                            }
                            AndroidApkUpdateInstaller.LaunchSystemInstaller(this, apkPath);
                        }
                        catch (Exception ex)
                        {
                            status.Text = "Установлення неможливе: " +
                                ExplainOperationError(ex);
                        }
                    }).Show();
            }
            catch (Exception ex)
            {
                if (_screen == "apk-update")
                    status.Text = "Оновлення відхилено: " +
                        ExplainOperationError(ex);
            }
            finally { _busy = false; }
        });
        Button(card, "Видалити завантажений інсталяційний APK", () =>
            new AlertDialog.Builder(this)
                .SetTitle("Очистити завантажений APK?")
                .SetMessage("Видаляється лише тимчасовий інсталяційний файл, " +
                    "а не застосунок, документи чи база.")
                .SetNegativeButton("Скасувати", (_, _) => { })
                .SetPositiveButton("Очистити", (_, _) =>
                {
                    if (_busy) return;
                    try
                    {
                        var path = UpdateApkContentProvider.StagedPath(this);
                        if (File.Exists(path)) File.Delete(path);
                        status.Text = "Тимчасовий APK видалено. Дані Мавки збережені.";
                    }
                    catch (Exception)
                    {
                        status.Text = "Не вдалося видалити тимчасовий APK.";
                    }
                }).Show());
    }


    private void ShowRelay()
    {
        Begin("relay", "Cloudflare Relay");
        var identity = new RelayIdentity(this);
        var card = Card();
        Text(card, "Захищений додатковий канал", 20, true);
        Text(card, "Сервер: " + RelayProtocol.Origin, 12);
        Text(card, "Два канали: Gmail та Relay. Реєстрація починається скануванням QR з ПК; приватні ключі телефона не передаються.", 13);
        var status = Text(card, identity.LastStatus, 14);
        var background=new CheckBox(this){Text="Фоновий обмін із сервером",Checked=RelayBackground.IsEnabled(this)};
        card.AddView(background);
        var changingBackground=false;
        background.CheckedChange+=(_,args)=>
        {
            if(changingBackground)return;
            try{RelayBackground.SetEnabled(this,args.IsChecked);status.Text=args.IsChecked?"Фоновий обмін увімкнено.":"Фоновий обмін вимкнено. Доступна ручна синхронізація.";}
            catch(Exception ex){changingBackground=true;background.Checked=RelayBackground.IsEnabled(this);changingBackground=false;status.Text=ex is InvalidOperationException or IOException?ex.Message:"Не вдалося змінити фоновий обмін.";}
        };
        var profile = LoadUserProfile();
        if (profile is null)
        {
            Text(card, "Спочатку заповни профіль користувача.", 14);
            Button(card, "Відкрити профіль", ShowProfile); return;
        }
        Button(card, "Реєстрація: сканувати QR з ПК", ShowProfile);
        Button(card, "Перевірити ключі пристрою", () =>
        {
            try{_=identity.Request(profile.ClientId);status.Text="Локальні ключі пристрою справні. Відбиток: "+RelayProtocol.Hash(RelayProtocol.Serialize(identity.SigningKey));}
            catch(Exception ex){ShowSafeFailure("Ключі пристрою",ex);}
        });
        Button(card, "Активувати після дозволу на ПК", async () =>
        {
            if (_busy) return; _busy = true; status.Text = "Перевіряємо дозвіл пристрою...";
            try { await new RelaySync(this).Activate(); status.Text = RelayBackground.IsEnabled(this)?"Пристрій активний. Фоновий обмін увімкнено за вашим вибором.":"Пристрій активний. Фоновий обмін вимкнено; синхронізуйте вручну або ввімкніть його окремо."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { _busy = false; }
        });
        Button(card, "Синхронізувати зараз", async () =>
        {
            if (_busy) return; _busy = true; status.Text = "Синхронізація Relay...";
            try
            {
                status.Text = await new RelaySync(this).SynchronizeNow();
                try { await SendPendingFilePartsAsync(maximum: 1); }
                catch (Exception) { status.Text += " Вихідні частини файлів збережені в черзі."; }
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { _busy = false; }
        });
        Button(card, "Надіслати тестовий пакет на ПК", async () =>
        {
            if (_busy) return; _busy = true;
            try
            {
                var relay = new RelaySync(this);
                var id = Guid.NewGuid().ToString("D");
                relay.Queue("test:" + id, RelayProtocol.Serialize(new { format = "MAVKA_RELAY_TEST_V1",
                    testId = id, text = "Тестовий пакет Android → ПК", createdAtUtc = DateTimeOffset.UtcNow }));
                status.Text = await relay.SynchronizeNow();
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { _busy = false; }
        });
        Button(card, "Призупинити Relay на телефоні", () =>
        {
            identity.SetEnabled(false); RelayBackground.SetEnabled(this, false);
            background.Checked=false;
            status.Text = "Relay призупинено. Ключі та локальна черга збережені. Для відновлення натисни «Активувати».";
        });
        Text(card, "За замовчуванням фоновий обмін вимкнений. Якщо ввімкнути його вручну, перевірка виконується приблизно раз на 15 хвилин; Android може відкладати її. Для одноразового обміну натисни «Синхронізувати зараз».", 12);
        Button(card, "Документи з локального архіву", ShowDocuments);
        Button(card, "Сповіщення та нагадування", ShowNotices);
        Button(card, "Збережені файли", ShowFiles);
    }

    private void ShowSettings()
    {
        Begin("settings", "Налаштування");
        var appearance = Card();
        Text(appearance, "Оформлення", 20, true);
        Text(appearance, "Поточна тема: " + _theme.Label +
            ". Вибір зберігається після перезапуску застосунку.", 14);
        Button(appearance, "Змінити колірну тему", ShowThemeChooser);
        var updateCard = Card();
        Text(updateCard, "Оновлення Мавки", 18, true);
        Text(updateCard, "Встановлення за посиланням Google Drive. " +
            "Мавка перевіряє пакет, новішу версію та підпис APK. " +
            "Поточна база й ключі не видаляються.", 13);
        Button(updateCard, "Оновити через Google Drive", ShowGoogleDriveUpdate);
        var card = Card();
        Text(card, "Пошта Gmail", 20, true);
        Text(card, _store.HasCredentials ? _store.GetEmail() : "Не підключено", 16);
        Button(card, "Пошта Gmail →", ShowMail);
        card = Card();
        Text(card, "Адреса ГОЛОВНОЇ МАВКИ", 18, true);
        Text(card, _store.GetMainMavkaEmail(), 16);
        var backupCard = Card();
        Text(backupCard, "Зашифрована резервна копія", 18, true);
        Text(backupCard, "Копія містить профіль, архів активних документів і " +
            "облік із датами. Не містить пароль Gmail, секретні ключі обміну, " +
            "вкладення приватного сховища чи документи в кошику. " +
            "Після перенесення потрібна повторна реєстрація.", 12);
        Button(backupCard, "Створити резервну копію", () =>
            PromptBackupPassword(false));
        Button(backupCard, "Відновити з резервної копії", () =>
            PromptBackupPassword(true));

        var ledgerManagement = Card();
        Text(ledgerManagement, "Керування локальним обліком", 18, true);
        Text(ledgerManagement,
            "Можна очистити залишки та проведені рухи лише підрозділу з поточного профілю. " +
            "Архів документів, ключі шифрування, Gmail і файли не видаляються.", 13);
        var ledgerStatus = Text(ledgerManagement, "Змін поки немає.", 13);
        Button(ledgerManagement, "Очистити локальний облік підрозділу", () =>
            LocalLedgerResetDialog.Show(this, _ledger, _archive,
                LoadUserProfile()?.Unit ?? "",
                () => _busy, message => ledgerStatus.Text = message));

        var relayCard = Card();
        Text(relayCard, "Сервер Cloudflare", 18, true);
        Text(relayCard, "QR-реєстрація пристрою та додатковий захищений канал обміну.", 13);
        Button(relayCard, "Налаштувати Cloudflare Relay", ShowRelay);
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
                    status.Text = "Діагностика: " + ExplainOperationError(ex);
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

        var save = new Button(this) { Text = "Зберегти налаштування пошти" };
        StylePrimaryButton(save);
        card.AddView(save);
        save.Click += async (_, _) =>
        {
            if (_busy) return;
            var address = (email.Text ?? "").Trim();
            var typedPassword = (secret.Text ?? "").Replace(" ", "").Trim();
            var password = typedPassword;
            var senderAddress = (mainMavkaEmail.Text ?? "").Trim();
            if (!System.Net.Mail.MailAddress.TryCreate(senderAddress, out var parsedSender) ||
                !string.Equals(parsedSender?.Address, senderAddress,
                    StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "Введи правильну адресу ГОЛОВНОЇ МАВКИ.";
                return;
            }
            bool senderChanged = !string.Equals(senderAddress,
                _store.GetMainMavkaEmail(), StringComparison.OrdinalIgnoreCase);
            // One save action handles sender updates, including before the
            // Gmail app password has been configured. It never connects to
            // IMAP merely to save an unchanged credential.
            if ((address.Length == 0 && !_store.HasCredentials) ||
                (_store.HasCredentials && typedPassword.Length == 0 &&
                 string.Equals(address, _store.GetEmail(), StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    _store.SaveMainMavkaEmail(senderAddress);
                    if (senderChanged)
                    {
                        _messages = Array.Empty<MavkaMailItem>();
                        _inboxCache.Clear();
                    }
                    status.Text = _store.HasCredentials
                        ? "Адресу ГОЛОВНОЇ МАВКИ збережено. Gmail залишається підключеним."
                        : "Відправника збережено. Для отримання листів додай Gmail.";
                }
                catch (Exception)
                {
                    status.Text = "Не вдалося зберегти відправника.";
                }
                return;
            }
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
                _store.SaveMainMavkaEmail(senderAddress);
                _messages = Array.Empty<MavkaMailItem>();
                _inboxCache.Clear();
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
                status.Text = $"Помилка на етапі {stage}: {ExplainOperationError(ex)}";
            }
            finally { _busy = false; save.Enabled = true; }
        };

        Button(card, "Відключити Gmail", () =>
        {
            try
            {
                _store.Clear();
                _messages = Array.Empty<MavkaMailItem>();
                _inboxCache.Clear();
                email.Text = _store.GetEmail();
                secret.Text = "";
                secret.Hint = "16 символів";
                status.Text = "Gmail відключено, адреса ГОЛОВНОЇ МАВКИ збережена.";
            }
            catch { status.Text = "Не вдалося очистити налаштування."; }
        });
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == ExportBackupRequest)
        {
            var backup = _pendingEncryptedBackup;
            _pendingEncryptedBackup = null;
            if (backup is null) return;
            try
            {
                if (resultCode != Result.Ok || data?.Data is null) return;
                var destination = data.Data;
                using var stream = ContentResolver?.OpenOutputStream(destination, "w") ??
                    throw new IOException("Не вдалося відкрити місце збереження.");
                await stream.WriteAsync(backup);
                await stream.FlushAsync();
                Toast.MakeText(this, "Зашифровану резервну копію збережено.",
                    ToastLength.Long)?.Show();
            }
            catch (Exception)
            {
                if (data?.Data is not null)
                    try { ContentResolver?.Delete(data.Data, null, null); }
                    catch { }
                Toast.MakeText(this, "Не вдалося зберегти резервну копію.",
                    ToastLength.Long)?.Show();
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(backup);
            }
            return;
        }
        if (requestCode == ImportBackupRequest)
        {
            var password = _pendingBackupPassword;
            _pendingBackupPassword = null;
            if (resultCode != Result.Ok || data?.Data is null || password is null)
                return;
            if (_busy) return;
            _busy = true;
            byte[]? fileBytes = null;
            try
            {
                var sourceUri = data.Data;
                fileBytes = await Task.Run(() =>
                {
                    using var stream = ContentResolver?.OpenInputStream(sourceUri) ??
                        throw new IOException("Резервна копія недоступна.");
                    using var buffer = new MemoryStream();
                    byte[] part = new byte[32768];
                    int read;
                    while ((read = stream.Read(part, 0, part.Length)) > 0)
                    {
                        if (buffer.Length + read > 40_000_000)
                            throw new IOException("Завеликий файл резервної копії.");
                        buffer.Write(part, 0, read);
                    }
                    return buffer.ToArray();
                });
                var protectedBytes = fileBytes;
                var service = new PortableBackupService(_archive, _ledger, _profileStore);
                var restored = await Task.Run(() => service.Restore(protectedBytes, password));
                Toast.MakeText(this,
                    "Відновлено документів: " + restored.Documents +
                    ", облікових записів: " + restored.Receipts +
                    ". Gmail і ключі потрібно підключити окремо.",
                    ToastLength.Long)?.Show();
                ShowSettings();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Відновлення не виконано: " +
                    ExplainOperationError(ex), ToastLength.Long)?.Show();
            }
            finally
            {
                if (fileBytes is not null)
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(fileBytes);
                _busy = false;
            }
            return;
        }
        if (requestCode is QrImagePickRequest or QrCameraCaptureRequest)
        {
            if (resultCode != Result.Ok) return;
            try
            {
                string raw;
                if (requestCode == QrImagePickRequest && data?.Data is not null)
                {
                    raw = await Task.Run(() => QrPhotoDecoder.ReadQrFromUri(
                        ContentResolver ?? throw new InvalidOperationException("Немає доступу до фото."),
                        data.Data));
                }
                else if(requestCode==QrCameraCaptureRequest && data?.GetStringExtra(LiveQrScannerActivity.ResultKey) is {} scanned)raw=scanned;
                else if (data?.Extras?.Get("data") is Bitmap captured)
                {
                    using (captured)
                        raw = await Task.Run(() => QrPhotoDecoder.ReadQr(captured));
                }
                else throw new InvalidOperationException(
                    "Камера не повернула QR-фото. Обери знімок із галереї.");
                using(var qrJson=System.Text.Json.JsonDocument.Parse(raw))
                {
                    if(qrJson.RootElement.GetProperty("format").GetString()==BootstrapProtocol.Qr)
                    {
                        _=new UserProfileStore(this).Load()??throw new InvalidOperationException("Збережи профіль.");
                        var bootstrapInvite=RegistrationV2Protocol.Parse(raw,DateTimeOffset.UtcNow);
                        new AlertDialog.Builder(this).SetTitle("Реєстрація через QR з ПК")
                            .SetMessage("ПК: "+bootstrapInvite.mainEmail+"\nЗапрошення діє до "+DateTimeOffset.FromUnixTimeSeconds(bootstrapInvite.expiresAt).ToLocalTime().ToString("HH:mm:ss")+".\nЗаявка буде зашифрована разовим ключем і спільним паролем, введеним вручну. Після її отримання ПК має підтвердити користувача та надіслати ключі. Це нова реєстрація: попередній доступ не поновлюється, локальні документи зберігаються.")
                            .SetNegativeButton("Скасувати",(_,_)=>{})
                            .SetPositiveButton("Надіслати захищену заявку",async(_,_)=>
                            {
                                if(_busy)return;_busy=true;
                                try
                                {
                                    if(!_store.HasCredentials)throw new InvalidOperationException("Спочатку підключи Gmail.");
                                    var shared=_documentPasswords.Load()??throw new InvalidOperationException("Спочатку введи спільний пароль обміну вручну в меню «Пароль документів».");
                                    var pending=await Task.Run(()=>new BootstrapStore(this).SaveQr(raw,_store.GetEmail(),shared));
                                    _store.SaveMainMavkaEmail(bootstrapInvite.mainEmail);
                                    await RegistrationMailService.SendEnrollmentAsync(_store.GetEmail(),_store.GetPassword()!,bootstrapInvite.mainEmail,pending.Wire,pending.Request.requestId);
                                    Toast.MakeText(this,"Захищену заявку надіслано. На ПК натисни «Перевірити пошту», підтвердь користувача й надішли початкові ключі. Потім онови Gmail на телефоні.",ToastLength.Long)?.Show();
                                }
                                catch(Exception ex){ShowSafeFailure("Реєстрація",ex);}
                                finally{_busy=false;}
                            }).Show();return;
                    }
                }
                throw new ArgumentException("Це QR попередньої тестової версії. Створи новий QR реєстрації на ПК; телефон його тільки сканує.");
            }
            catch (Exception ex) { ShowSafeFailure("QR",ex); }
            return;
        }
        if (requestCode == PickEncryptAndSendRequest)
        {
            if (resultCode != Result.Ok || data?.Data is null) return;
            if (_busy) return;
            _busy = true;
            byte[]? fileBytes = null;
            try
            {
                var selected = data.Data;
                string? name = null;
                using (var cursor = ContentResolver?.Query(selected,
                    new[] { Android.Provider.OpenableColumns.DisplayName },
                    null, null, null))
                    if (cursor?.MoveToFirst() == true && !cursor.IsNull(0))
                        name = cursor.GetString(0);
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("У файла немає коректної назви.");
                _ = MavkaEncryptedFileEnvelope.MimeFor(name);

                // Bound source reading before loading into memory or creating the
                // encrypted envelope. No cleartext writes to public storage.
                var inputFile = name;
                var sourceUri = selected;
                var loaded = await Task.Run(() =>
                {
                    var buffer = new byte[MavkaFileParts.MaxFileBytes + 1];
                    try
                    {
                        using var source = ContentResolver?.OpenInputStream(sourceUri) ??
                            throw new IOException("Document provider is unavailable.");
                        int count = 0, read;
                        while (count < buffer.Length &&
                            (read = source.Read(buffer, count, buffer.Length - count)) > 0)
                            count += read;
                        if (count is < 8 or > MavkaFileParts.MaxFileBytes)
                            throw new ArgumentException("Файл завеликий або порожній.");
                        return (Buffer: buffer, Length: count);
                    }
                    catch
                    {
                        System.Security.Cryptography.CryptographicOperations
                            .ZeroMemory(buffer);
                        throw;
                    }
                });
                fileBytes = loaded.Buffer;
                var password = _store.GetPassword() ?? "";
                var key = _exchangeKeys.GetCurrentKey() ?? throw new KeyNotFoundException("Ключ недоступний.");
                try
                {
                    await Task.Run(() =>
                    {
                        var description = MavkaFileParts.Describe(inputFile, fileBytes.AsSpan(0, loaded.Length));
                        OutgoingFiles().Enqueue(description, i => MavkaFileParts.Encrypt(description with { Index = i },
                            fileBytes.AsSpan(i * MavkaFileParts.ChunkBytes,
                                Math.Min(MavkaFileParts.ChunkBytes, loaded.Length - i * MavkaFileParts.ChunkBytes)), key));
                    });
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(fileBytes);
                await SendPendingFilePartsAsync();
                Toast.MakeText(this,
                    "Захищені частини файла передані каналу обміну.",
                    ToastLength.Long)?.Show();
            }
            catch (KeyNotFoundException)
            {
                Toast.MakeText(this, "Потрібний ключ обміну недоступний.",
                    ToastLength.Long)?.Show();
            }
            catch (ArgumentException)
            {
                Toast.MakeText(this,
                    "Непідтримуваний файл. Потрібен справний PDF/Excel/Word до 32 МБ.",
                    ToastLength.Long)?.Show();
            }
            catch (Exception)
            {
                Toast.MakeText(this,
                    "Надсилання призупинено. Підготовлені частини збережені в черзі; перевір Gmail і мережу.",
                    ToastLength.Long)?.Show();
            }
            finally
            {
                if (fileBytes is not null)
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(fileBytes);
                _busy = false;
            }
            return;
        }
        if (requestCode == ExportPrivateCopyRequest)
        {
            var id = _pendingPrivateExportId;
            _pendingPrivateExportId = null;
            if (resultCode != Result.Ok || id is null || data?.Data is null) return;
            var location = data.Data;
            try
            {
                using var file = await Task.Run(() => _privateFiles.Load(id));
                using var destination = ContentResolver?.OpenOutputStream(location, "w")
                    ?? throw new IOException("Storage provider unavailable.");
                await destination.WriteAsync(file.Content);
                await destination.FlushAsync();
                Toast.MakeText(this,
                    "Розшифровану копію збережено у вибраному місці.",
                    ToastLength.Long)?.Show();
            }
            catch (Exception)
            {
                try { ContentResolver?.Delete(location, null, null); }
                catch (Exception) { }
                Toast.MakeText(this,
                    "Не вдалося зберегти копію; частковий файл спробували видалити.",
                    ToastLength.Long)?.Show();
            }
            return;
        }
        if (requestCode != PickDocumentRequest || resultCode != Result.Ok ||
            data?.Data is null) return;

        var uri = data.Data;
        var mime = ContentResolver?.GetType(uri);
        // Android document providers may label an XLSX as octet-stream.
        // Only accept PDF/Excel/Word extensions from the user's explicit SAF choice;
        // this action launches a read-only external viewer, not an importer.
        string? fileName = null;
        try
        {
            using var cursor = ContentResolver?.Query(uri,
                new[] { Android.Provider.OpenableColumns.DisplayName },
                null, null, null);
            if (cursor?.MoveToFirst() == true && !cursor.IsNull(0))
                fileName = cursor.GetString(0);
        }
        catch (Exception) { /* A MIME-only provider is also valid. */ }
        bool pdf = mime == "application/pdf" ||
            fileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;
        bool xlsx = mime == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" ||
            fileName?.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) == true;
        bool docx = mime == "application/vnd.openxmlformats-officedocument.wordprocessingml.document" ||
            fileName?.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) == true;
        bool doc = mime == "application/msword" || fileName?.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) == true;
        if (!pdf && !xlsx && !docx && !doc)
        {
            Toast.MakeText(this, "Підтримуються лише PDF та XLSX.",
                ToastLength.Long)?.Show();
            return;
        }
        try
        {
            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, pdf ? "application/pdf" : docx ?
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : doc ? "application/msword" :
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            StartActivity(Intent.CreateChooser(intent, "Переглянути файл"));
        }
        catch (Android.Content.ActivityNotFoundException)
        {
            Toast.MakeText(this, "Немає програми для відкриття цього формату.",
                ToastLength.Long)?.Show();
        }
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
        field.Background = Rounded(_theme.Input, 12, _theme.InputOutline);
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
        card.Background = Rounded(_theme.Card, 18, _theme.CardOutline);
        card.Elevation = Dp(2);
        var lp = new LinearLayout.LayoutParams(-1, -2);
        lp.SetMargins(Dp(2), Dp(7), Dp(2), Dp(8));
        _body.AddView(card, lp);
        return card;
    }

    private TextView Text(LinearLayout parent, string caption, float size, bool bold = false)
    {
        var view = new TextView(this) { Text = caption, TextSize = size };
        view.SetTextColor(bold ? _theme.Heading : _theme.BodyText);
        if (bold) view.SetTypeface(null, TypefaceStyle.Bold);
        view.SetPadding(0, Dp(3), 0, Dp(7));
        parent.AddView(view);
        return view;
    }

    private void StylePrimaryButton(Android.Widget.Button button)
    {
        button.SetAllCaps(false);
        button.SetTextColor(Color.White);
        button.Background = Rounded(_theme.PrimaryAction, 13);
        button.SetMinHeight(Dp(47));
        button.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
    }

    private Android.Widget.Button Button(LinearLayout parent, string text, Action callback)
    {
        var view = new Button(this) { Text = text, TextSize = 14 };
        view.SetAllCaps(false);
        var isSecondary = text.StartsWith("←") || text.StartsWith("Закрити") ||
                          text.StartsWith("Забути") || text.StartsWith("Видалити") ||
                          text.StartsWith("Перемістити лист у кошик");
        view.SetTextColor(isSecondary ? _theme.AccentText : Color.White);
        view.Background = Rounded(isSecondary ? _theme.SoftAction :
            _theme.PrimaryAction, 13);
        // Intrinsic height grows with Android's accessibility font scale.
        // Fixed 47dp buttons previously clipped long localized labels.
        view.SetMinHeight(Dp(47));
        view.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        var layout = new LinearLayout.LayoutParams(-1, -2);
        layout.SetMargins(0, Dp(4), 0, Dp(5));
        parent.AddView(view, layout);
        view.Click += (_, _) => callback();
        return view;
    }

    private void AddAssetRow(LinearLayout parent, string name, string description,
        string quantity, Action? showHistory = null)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(9), Dp(9), Dp(8), Dp(9));
        row.Background = Rounded(_theme.ListRow, 14,
            _theme.CardOutline);
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
        amount.SetTextColor(_theme.StrongText);
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

