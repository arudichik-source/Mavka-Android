# Мавка Mobile — стан розробки та продовження

Основний репозиторій: `arudichik-source/Mavka-Android`; робоча гілка `main`.
GitHub Actions: `.github/workflows/build-android.yml`; встановлюваний артефакт `Mavka-Mobile-APK`.
Збірка APK **не підтверджує** успішну роботу на фізичному Android. Потрібні реальні тести.

## Реалізовано

- Нативний Android UI у затвердженому лісовому стилі, нижня навігація,
  пошук/фільтри в Gmail, затверджений атлас іконок із 8 категорій
  та винятком `12х70`/12 калібр перед загальним `патрон`.
- Gmail через MailKit з точним зіставленням From та обмеженим завантаженням
  невеликих TXT/JSON-вкладень; TLS fallback тільки для невідомого статусу
  відкликання, без вимкнення довіри CA/перевірки імені.
- `MAVKA_DOCUMENT_V1` — перегляд накладних та ручне підтвердження МВО;
  транзакційний SQLite-облік з унікальним documentId та окремими
  партіями за назвою, NSN, одиницею і ціною.
- `MAVKA_UNIT_ASSETS_V1` — початковий знімок; ручне одноразове
  зарахування до початку інших рухів; за наявності документів імпорт
  блокується для уникнення подвійного обліку.
- `MAVKA_KEY_PACKAGE_V1` — парольний JSON-пакет з поточним і наступним
  ключами; одноразовий пароль документа у налаштуваннях Android;
  обидва ключі зберігаються за допомогою Android Keystore,
  ротація контролюється поколінням і відповідністю майбутнього/поточного.
- `MAVKA_ENCRYPTED_V1` — AES-256-GCM, автентифіковане шифрування
  JSON-вкладень, а також вихідних службових листів після встановлення ключів.
- `MAVKA_UNIT_MOVEMENTS_V1` — довідкова історія надходжень,
  актів списання, переміщень та коригувань за JSON із Gmail.
  Не змінює локальний залишок, але доступна через натискання майна.
- Службові листи переміщуються в кошик Gmail після ручного підтвердження.

## Тести

- `dotnet run --project tools/AssetIconChecks/AssetIconChecks.csproj`
  перевіряє правила відповідності іконок та читання історії.
- `dotnet run --project tools/MavkaEncrypt -- selftest`
  перевіряє захищений паролем пакет ключів і відхилення неправильного пароля.
- `dotnet publish src/Mavka.Mobile/Mavka.Mobile.csproj -f net10.0-android -c Release`
  компілюється в GitHub Actions.
- Android-перевірки: збереження пароля, отримання 2 ключів, повторне
  читання ключового листа без дублювання, ротація поколінь, надсилання
  зашифрованого підтвердження, зарахування початкового залишку,
  отримання накладної та повторний імпорт, пошук майна, вікно історії
  зі справжніми номерами документів, поворот телефону без заставки.

## Ще не реалізовано / ризики

1. Інтеграція з кодом настільної ГОЛОВНОЇ МАВКИ: автоматичне створення
   парольних пакетів, інвентаризацій, історії, автентифікація й обробка
   вхідних зашифрованих листів підтвердження.
2. Нативний пароль на PDF та Excel, захищені за допомогою відповідних
   стандартів форматів; звичайні TXT/JSON не можуть мати стандартний пароль,
   для них потрібен окремий зашифрований контейнер.
3. Прямі криптографічні гарантії джерела першого пакета ключів. Перевірка
   Gmail From не замінює криптографічний підпис або довірене підтвердження.
   Значення 090699 — демонстраційний слабкий пароль, не придатний для
   справжніх службових документів.
4. Тести на фізичному Android та сумісність зі старою базою користувача,
   зокрема міграція SQLite v1->v2, ще не підтверджені.
5. Повноцінне проведення актів списання в мобільній базі НЕ реалізовано.
   Історія з ГОЛОВНОЇ МАВКИ поки лише довідкова.

Продовжувати роботу поверх останньої зеленої `main`, не відкотуючи
попередні Gmail/TLS/UI/облікові функції. Не записувати реальні паролі,
ключі, персональні дані чи службові документи в репозиторій.

## Робоча гілка від 11.10.2026 — архів і профіль (НЕ ЗЛИВАТИ БЕЗ CI)

Branch: `feature/mobile-archive-and-profile-20261011`. Baseline `main`:
`af0fb026f9816c27acfb369508b9f7ad972df6dd`, Actions #67 SUCCESS.

### Реалізовані зміни на робочій гілці
- `PhoneNumberNormalizer.cs`: валідація, нормалізація +380 та міжнародних номерів.
- `UserProfileStore.cs`: необов'язкове поле `PhoneNumber` в наявному Keystore
  AES-GCM профілі, без зміни `ClientId`. Старі JSON-профілі без поля
  мають завантажуватися зі значенням за замовчуванням.
- `RegistrationMailService.cs`: додаткове поле телефону в реєстраційному
  форматі (desktop compatibility НЕ ПЕРЕВІРЕНА).
- `DocumentArchiveStore.cs`: нова приватна SQLite-база архіву (не видаляє
  `mavka_property_ledger_v1.db`), AES-GCM-захист тексту через окремий
  Android Keystore alias, SHA-256-перевірка, документний ID та журнал Gmail.
- `ArchiveDocumentClassifier.cs`: суворий допуск перевірених накладних,
  початкових знімків та довідкових журналів лише для поточного підрозділу.
- `GmailConnectionService.cs`: ознака повного опрацювання вкладень,
  перевірка читання збереженої пари ключів; непрочитані та інші формати
  блокують автоматичне очищення.
- `MainActivity.cs`: список локальних документів, відкриття накладної без
  Gmail, автомаршрут збереження -> read-back -> mail_processing -> Gmail Trash.
  Ручний обхід гарантій очищення прибраний.
- `tools/ArchiveChecks`: тести валідації номера, прив'язки до підрозділу,
  типів документів і детермінованих ID. Додані до GitHub Actions.

### Обмеження/незавершені етапи (важливо)
- Робоча гілка ще потребує успішної Android-компіляції та тесту на пристрої.
- На поточному проході архівуються тільки відомі JSON-типи та пакети ключів.
  PDF, XLSX, TXT, невідомі, пошкоджені й неповністю прочитані вкладення
  залишаються у Gmail Inbox. Це захист від втрати даних, не завершена підтримка типів.
- Отриманий документ не проводиться автоматично. Зберігання та підтвердження
  відокремлені. Повноцінна offline outgoing queue ще не реалізована.
- Облікові рухи, історичні залишки, файлова папка, PDF/XLSX і
  двостороння desktop-синхронізація — наступні окремі етапи.
- Автоматичний кошик не можна тестувати з реальними службовими документами
  до перевірки на ізольованому Gmail із демонстраційними даними.
- Не змінено `MAVKA_KEY_PACKAGE_V1`, `MAVKA_ENCRYPTED_V1`, AES-GCM,
  PBKDF2, алгоритм/сховище двох ключів і ротацію.

### Наступні обов'язкові дії
1. Перевірити CI для PR, виправити компіляційні та тестові регресії.
2. Протестувати встановлення поверх тестової бази, збереження після рестарту.
3. Розширити стани архіву, транзакційний журнал рухів, залишки на дату,
   офлайн-чергу, експорти та інтеграцію з десктопом згідно з ТЗ 11.10.2026.
4. Після кожного етапу оновлювати цей документ і не зливати неперевірене в main.

---

## Етапи 4–5: події та історичні залишки (робоча гілка)

Branch: `feature/mobile-ledger-dates-20261011`, заснована на архівній гілці.
- `PropertyLedgerStore.cs`: контрольована міграція SQLite v1/v2→v3 без DROP,
  таблиця `ledger_events`, унікальні ID подій, backfill попередніх проведених
  накладних і початкових залишків; нові накладні створюють події в тій самій
  транзакції, що й рядки документа.
- `LedgerBalanceCalculator.cs`: точне decimal-обчислення за обліковою
  датою й підрозділом, заборона повторних eventId, відокремлення NSN/цін,
  невідома ціна залишається невідомою.
- `MainActivity.cs`: календар і повернення до «Сьогодні» на екрані майна,
  перегляд архівованих знімків та довідкових рухів навіть після Gmail Trash.
- `tools/LedgerChecks`: перевірки відкриття на початок дня, рухів тієї
  самої дати, видатків, розділення підрозділів/NSN, null-ціни; інтеграція в CI.
- Зауваження: поки автоматично проводяться тільки раніше підтримані
  підтверджені накладні та початковий знімок. Списання/коригування з довідкових
  журналів **не проводяться**. Сторно й узгодження з десктопом не реалізовані.
- Обов'язкові перевірки: Android CI, міграція фізичної копії старої бази,
  відсутність подвійних подій при оновленні, фактична дата знімків.
- Статус: тестовий код, не заявляти як production-ready до CI і пристрою.

---

## Етап 7, частина 1: черга вихідних підтверджень

Branch: `feature/mobile-offline-outbox-20261011`.
- Повторно використано надійний запис `receipts.mail_status=pending` як чергу
  збережених вихідних підтверджень; не створюються тимчасові in-memory jobs.
- `ListPendingReceiptIds` повертає максимум 25 підтверджень за одну спробу.
- Після успішного Gmail refresh, `SendPendingReceiptsAsync` шукає накладну
  у локальному захищеному архіві, перевіряє підрозділ і надсилає її старим
  затвердженим `MAVKA_MOBILE_RECEIPT_V1` через існуючий AES обмін.
- При помилці SMTP статус залишається `pending`; спробу повторять після
  наступного ручного оновлення Gmail. Підтвердження без архівного JSON
  зберігаються як pending і не видаляються.
- Користувацький екран показує число записів черги; не плутати `sent`
  із серверним підтвердженням або `synced`.
- Наступне: повноцінна черга для всіх типів повідомлень, connectivity hooks,
  outbox retry/backoff, idempotent desktop ACK, reconcile-знімки, і фізичні тести.
- Реальні ключі/паролі/документи в GitHub не додавати. Не зливати без CI.
---

## Етап 6, частина 1: XLSX-генератор (ще не доступний в UI)

Branch `feature/mobile-export-core-20261011`. `XlsxInvoiceBuilder.cs` створює
справжній пакет Office Open XML у пам'яті (без залежностей від комерційних
бібліотек): лист `Накладна`, українські заголовки та рядки, числові
кількості/ціни/вартості, Excel-дату, autofilter, 200+ позицій, підсумок
лише для рядків з відомою ціною. `tools/XlsxChecks` перевіряє ZIP/OpenXML
склад пакета, 200 рядків, типи клітинок і кирилицю; включено в CI.

**Безпека:** цей генератор НЕ шифрує XLSX і НЕ експортує його у публічні
Downloads. Не підключати його до UI для конфіденційних документів до
реалізації нативного парольного захисту XLSX та перевірки Android MediaStore.
PDF і менеджер `МаVКа файли` ще не виконані. Робоча гілка залежить
від попередніх PR #16–18. Тести пристрою ще не проводилися.
---

## Етап 8: поліпшення навігації/компонування (робоча гілка)

Branch `feature/mobile-ui-refinement-20261011`:
- Задано контрастні кольори системного status/navigation bars (Android).
- Бічне меню поміщено у ScrollView: дії доступні на коротких екранах.
- Галерею демо-іконок прибрано з основної вкладки «Майно» до
  секції «Діагностика» у налаштуваннях.
- З головної сторінки прибрано тестове технічне пояснення Gmail.
- Довгу історію майна перенесено з обмеженого AlertDialog до окремого
  прокручуваного екрана з доступом до архівованих накладних.
- Загальний фірмовий стиль, графіка, іконки та splash залишені без змін.
- Ресайз/повороти, клавіатура, кілька розмірів екранів потребують
  фізичних Android-перевірок. Не називати UI завершеним до цих тестів.
- Ця гілка залежить від усіх попередніх draft PR і не зливається до
  підтвердження CI/пристрою.
---

## Етап 2/7: аудит статусів архіву — SQLite v2

Branch `feature/mobile-document-status-20261011`:
- `DocumentArchiveStore`: контрольована міграція v1→v2 без DROP/DELETE;
  `posting_status`, `delivery_status`, `sync_status` є окремими
  стовпцями, нова таблиця `document_status_history` реєструє зміни.
- Старі локально збережені архівні документи залишаються на місці; backfill
  початкової події `archive=saved`. Перевірка міграції на фізичній копії
  старої бази ще потрібна.
- Після проведення накладної оновлюється статус `posted`, для SMTP
  використовується `pending`/ `sent`; UI показує стани окремо й історію.
- `sync_status=not_synced` поки означає тільки відсутність ACK.
  Не маркувати `synced` без підтвердження ГОЛОВНОЇ МАВКИ.
- Недоліки: між базою проведень та архівом немає спільної транзакції
  (окремі файли), тому потрібна подальша автоматична звірка статусів;
  повноцінний desktop ACK ще не реалізовано.
- Документне шифрування і схему ключів не змінено.
---

## Етап 7, частина 2: читання та звірка контрольних знімків

Branch: `feature/mobile-snapshot-reconciliation-20261011`.
- Новий `LedgerReconciliation.cs` виконує лише читання і порівняння
  локальних залишків на дату з контрольним `MAVKA_UNIT_ASSETS_V1`.
- Розмежовуються точні назви, одиниці, номенклатурні ідентифікатори
  та облікові ціни, невідому вартість не підміняє нулем.
- У `MainActivity` додано екран розбіжностей (локально мінус контроль)
  і попередження про різницю «стан на початок дня» / «кінець дня».
- Порівняння **не змінює** облік, не застосовує автоматичних коригувань,
  не підтверджує серверної синхронізації. Для ручної звірки.
- `tools/ReconciliationChecks`: незалежні тести на точність, нульову
  кількість відсутніх позицій і розділення облікових партій у CI.
- Потрібні CI + тест на реальному Android та десктопне підтвердження
  семантики `asOf`; відсутній повноцінний ACK-цикл ГОЛОВНОЇ МАВКИ.
---


## 2026-10-12 — Reliability: legacy outbox + 200-position intake

Working branch: `feature/mobile-reliability-20261012`, based on green #79
commit `0348fdb35ce8b4edb60ef8fb76f54109acb98ba9`.
Do not merge until the new PR CI and real Android checks pass.

Changes:
- `PropertyLedgerStore.LoadConfirmedInvoice(documentId)` reconstructs exact
  stored rows and prices from original SQLite receipts. Pending confirmations
  created before `DocumentArchiveStore` existed can now be resent without
  a Gmail letter and **without posting inventory a second time**.
- `MainActivity.SendPendingReceiptsAsync` uses the authoritative receipt
  ledger, validates MVO/unit, sends in bounded batches (3 per attempt), and
  exposes a manual retry on Home. SMTP status transitions must be durable.
- `PropertyLedgerStore.MarkMailSent` rejects an update that affects no row.
- `MainActivity.ShowArchivedDocument` reconciles posting/delivery flags
  from the authoritative receipt ledger after a partial crash across the
  two independent SQLite stores. This affects only displayed statuses.
- Confirmation UI distinguishes SMTP accepted from a failed local status
  update, so a sent-but-unrecorded confirmation is not called "Gmail offline".
  Server idempotency on documentId is still required for ambiguous retries.
- Increase bounded document sizes (not the encryption/key formats):
  IMAP TXT/JSON attachment 32 KiB → 256 KiB; encrypted envelope JSON
  up to 256K chars with ciphertext up to 192 KiB; invoice/snapshot/history
  JSON parse up to 192K chars. Still at most 3 recognized attachments per mail
  for automatic Trash; unsupported or incomplete mail remains in Inbox.
- 200-row invoice UI now renders up to 25 positions per tap, rather than
  creating 200+ rows immediately; raw JSON dialogs limited to 8,000 chars.
- `tools/ArchiveChecks` regression covers a >65KB 200-position invoice.

Do not change `MAVKA_KEY_PACKAGE_V1`, `MAVKA_ENCRYPTED_V1`,
AES-256-GCM, PBKDF2, or the 2-key rotation rules. No historical keys retained.

Pending: Android CI, compatibility tests with Gmail key rotation, restoring a
pre-archive SQLite fixture on a physical phone, real offline/reconnect,
full PDFs/password-protected XLSX export, desktop ACK/confirmation parser.
Known limitation: SMTP has an inherent ambiguous success window before local
sent-status commits; desktop receipt handling must dedupe by ClientId and
documentId to guarantee exactly-once accounting.
---


## 2026-10-12 — Scoped file manager (Android MediaStore)

Working branch: `feature/mobile-file-manager-20261012`, stacked above
`feature/mobile-reliability-20261012` (PR #23).

- `MavkaExportPolicy.cs`: exactly the approved label `МаVКа файли`,
  Android media relative path `Download/МаVКа файли/`, PDF and XLSX
  allow-list. Paths outside this folder and internal databases are excluded.
- `MavkaExportFileStore.cs`: Android 10+ MediaStore API (no raw filesystem
  access, no `MANAGE_EXTERNAL_STORAGE`) to list PDF/XLSX export copies,
  inspect name/date/size, open via system viewer, share with URI grants,
  delete only revalidated copies from the dedicated export folder.
- `MainActivity.cs`: an accessible `МаVКа файли` screen from Home/drawer,
  search, sort, selection, confirmation before deleting selected copies or
  clearing the folder. A deletion does **not** touch document archive,
  property ledger, keys or Gmail.
- `tools/FilePolicyChecks`: 10 safety assertions added to GitHub Actions.
- Android 8/9: MediaStore scoped Downloads selection is not available.
  A separate SAF picker is still needed; the UI explicitly says so.
- This stage only **manages existing exported copies**. It does NOT yet
  generate or publish plain PDF/XLSX. For real confidential exports, implement
  and validate format-native PDF and XLSX password encryption first.
- Physical device verification of listing, external viewers, scoped deletion,
  multi-select and SAF remains pending. Do not merge until CI passes and
  on-device permission behavior is verified.
---


## 2026-10-12 — format-native AES-256 encrypted PDF export (draft)

Branch: `feature/mobile-protected-pdf-20261012`, based on green #86
commit `5b9dd669875dc16ea0a693ce53325bccfa2a24ab`.

- Added MIT-licensed PDFsharp 6.2.4 (Core) as a dependency.
- `ProtectedPdfBuilder.cs`: generates native Android paginated printable
  PDF from an already saved local invoice using Android Canvas/OS typeface
  (Unicode Cyrillic), wraps long names, prints quantity, unit price,
  line totals and known-price summary. Unencrypted source lives in RAM only.
- The generated PDF is reopened in memory by PDFsharp, assigned a user
  password (12-128 chars) and random independent owner password, and saved
  with PDF-native AES-256 V5 encryption. The source PDF byte buffer is
  zeroed after encryption; no unprotected temp files are created.
- `MavkaExportFileStore.SaveProtectedPdf`: only publishes encrypted PDF
  to Android MediaStore `Download/МаVКа файли/`; IS_PENDING=1 until the
  write succeeds, then finalizes; attempts to remove interrupted exports.
- Local archived invoices now expose `Зберегти PDF із паролем` with a
  non-persistent password prompt. Generation/write occur off the main UI
  thread, with a human-readable error on failure. No export from Gmail alone.
- `tools/ProtectedPdfChecks`: creates AES-256 PDF and verifies a wrong
  password is rejected and a valid password opens the document.
- **Important:** this is an initial implementation and requires CI,
  physical-Android PDFsharp/MediaStore testing, visual Cyrillic/font layout
  verification, and opening the result in standard Android/PC viewers.
  Do not label the PDF mechanism production-ready until tested.
- Native-password Excel `.xlsx`, MediaStore write on Android 8/9 (SAF),
  complete cross-device sync and signed APK updates remain pending.
- The approved MAVKA key package/envelope/rotation scheme was not touched.
---


## 2026-10-12 — encrypted XLSX export (generic document-security stage)

Branch `feature/mobile-xlsx-protected-20261012`, stacked above PR #25
(verified GitHub Actions #87 SUCCESS).

- Added Apache-2.0 `ExcelEncryptor` 2.0.5.1 for format-native Microsoft
  Office Agile Encryption. Package defaults are AES-128/SHA-1; do not claim
  this is AES-256. The existing offline OOXML builder remains unchanged.
- `ProtectedXlsxBuilder.cs` works with unencrypted XLSX bytes in RAM only,
  writes only an encrypted Office compound package to private Android cache,
  verifies password-based decryption and byte-for-byte round-trip, and
  zeroes in-memory plaintext before returning. Deletes the encrypted cache
  file after use; on abrupt app termination a cache copy can remain but is
  encrypted, not unprotected plaintext.
- `MavkaExportFileStore.SaveProtectedXlsx` requires native OLE encrypted
  package magic and writes with MediaStore `IS_PENDING` to the approved
  `Download/МаVКа файли/` folder, rolling back partial copies on failure.
- Offline document preview now offers separate PDF and Excel password
  prompts. Passwords are not stored; file creation runs off the UI thread.
- `tools/ProtectedXlsxChecks` tests encrypted Office container bytes,
  incorrect-password rejection, and exact decrypted OOXML round-trip.
  The test runs in GitHub Actions before APK publication.
- Important: this covers generic Office encryption and protected file output,
  not full end-to-end synchronization. Android device and desktop-Excel
  verification remain mandatory before claiming ready for release.

Still pending independent safety/quality gates: Android emulator/device
file sharing, password interoperability in Excel and compatible viewers,
offline reconnection, profile/Keystore resilience, signed install update,
and any missing desktop counterpart. No modifications to the approved
MAVKA exchange envelope or two-key provisioning scheme.
---


## 2026-10-12 — PDF pagination and MediaStore file-manager hardening

Branch: `feature/mobile-export-quality-20261012`, stacked on PR #26,
based on green GitHub Actions #89 commit
`c21d01bd7073c5792d47a1d18f66f48dfcc9cb65`.

Safe, generic quality improvements:
- `PdfPageFlow.cs` is a pure page cursor. Lines are individually paginated:
  a paragraph containing more lines than an entire page can no longer
  overflow the printable area. Header metadata is paginated once at the
  start rather than copied/truncated on every page.
- `ProtectedPdfBuilder` now wraps supplementary Unicode characters as
  whole runes, not disconnected UTF-16 halves. It also reopens the
  in-memory AES-256 PDF using the entered password to validate that the
  exported document is readable before publishing it.
- `tools/PdfFlowChecks` asserts that 3,000 layout lines spanning many
  pages remain inside the printable margins; integrated into CI.
- `MavkaExportFileStore` queries only
  `Download/МаVКа файли/` through MediaStore with an exact relative-path
  selection. Every file-open, share and delete action revalidates the
  content URI, name and MIME against the same scoped list.
- `MainActivity.ShowFiles` now caches a single MediaStore directory query
  until explicit Refresh and filters/sorts the in-memory list, avoiding
  ContentResolver calls on every search keystroke; sorting label updates.
- No SQL migrations, receipt logic, network protocols, or encryption key
  lifecycle was changed.
- Physical-device validation of multi-page Cyrillic PDF layout,
  MediaStore visibility/open/share/delete and Android 10+ provider semantics
  remains necessary. Stable main stays unchanged.
---


## 2026-10-12 — APK identity gate and preview-only device test protocol

- `tools/Inspect-Android-Apk.ps1` is a cross-platform PowerShell 7
  inspector for SDK `aapt` package identity and `apksigner` signature.
  It never installs unless explicitly called with `-InstallPreview` and
  the APK package ID exactly equals `ua.mavka.mobile.preview`.
  Uses `adb install -r -t` only, never uninstall/clear-data.
- The build workflow now checks the manifest application IDs of both
  production and independent preview APKs before uploading artifacts.
  These checks do not change how CI signing keys are generated.
- `docs/ANDROID_DEVICE_ACCEPTANCE.md` contains a repeatable device
  test plan using fabricated document data, encryption viewers, multi-page
  PDF, file management, offline UI, and compatibility.
- Automatic APK update and SMS remain intentionally unimplemented by the
  original scope. A consistent production signing key and migration/device
  acceptance must precede any in-place upgrade promise.
- The latest proposed changes remain in stacked draft PR #27; main is
  unchanged. Await final GitHub Actions success for the branch head.
---


### Final pre-device quality follow-up (same PR)

- MediaStore listing now uses `Task.Run` and refreshes the view after
  asynchronous completion. Filter/sort stay in-memory on the UI thread.
- Encrypted PDF trailer inspection searches the byte span directly instead
  of allocating an additional string as large as the entire PDF.
- PR build workflow now has per-PR concurrency: newer commits cancel
  obsolete runs from the same PR only; unrelated PRs and `main` are
  unaffected. Only the latest branch-head CI is authoritative.
- Check `docs/ANDROID_DEVICE_ACCEPTANCE.md` before any on-device install.
  No production upgrade key exists in GitHub Actions at this stage.


## 2026-10-12 — multipart/Drive implementation follow-up (PR #31)

See `docs/LARGE_FILE_TRANSFERS.md` for the exact protocol, counterpart test
producer, implementation boundaries and device checklist. Added multipart GCM,
persistent incoming/outgoing checkpoints, automatic authenticated Drive download
for accessible encrypted blobs, DOC/DOCX support and confirmed transfer deletion.
Private Drive OAuth and full desktop integration remain pending. Do not infer
device acceptance from CI or uninstall an existing app to change its signing key.
