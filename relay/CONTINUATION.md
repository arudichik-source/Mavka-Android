# Relay: update 23.10.2026 — private R2 attachment integration (draft)

- Cloudflare Dashboard screenshot confirms production Worker binding `FILES` → private bucket `mavka-private-files`; existing `DB` → `mavka-messages` preserved.
- R2 bucket Standard, public access disabled; `cleanup-transfers` lifecycle `transfers/` 7 days enabled.
- PR #37 merged to `main` as `5c90b64328c4ec58ccbe7ce9af2fca028933af76`; generated Wrangler config now preserves R2 binding on deployment.
- Draft PR #38 (`feature/relay-r2-encrypted-parts-v1`) adds signed encrypted-parts R2 API, recipient-only reads, durable D1 ACK tombstones, R2 deletion, additive migration and Miniflare tests.
- **PR #38 is NOT merged or deployed.** Before rollout, take D1 backup/Time Travel checkpoint, apply additive `relay/migrations/0002_r2_file_parts.sql` manually to production, verify schema, then deploy. Do not deploy code before migration: scheduled cleanup refers to the new table.
- `relay/docs/R2_ENCRYPTED_PARTS_V1.md` records API, limits, trust boundary and rollout. Android/Windows file clients are not yet integrated and no real encrypted file transfer has been done.
- `/health` was user-tested 23.10.2026 with version 1.0.0, and unsigned `/v1/status` returned `unauthorized`. D1 screenshot: one active main, one active mobile, four revoked mobiles.
- Current CI: Relay check passing on draft branch; any later changes require fresh checks.

---

# Relay: стан на 19.10.2026

## Підтверджений стан

- Worker: `mavka-relay.arudichik.workers.dev`.
- Існуюча база: `mavka-messages`, binding `DB`; UUID отримано від користувача
  і звірено зі скриншотом Overview. Передати його через `MAVKA_D1_DATABASE_ID`.
- Колонки, типи, NULL/default і первинні ключі `devices` та `messages` звірено
  зі скриншотами `PRAGMA table_info` 18.10.2026. `public_encryption_key` допускає
  NULL, `status` має DEFAULT 'pending'; локальні тести відтворюють цю схему.
  19.10.2026 додатково звірено `sqlite_master`: CHECK для типів/статусів,
  CHECK `expires_at > created_at`, обидва FK на `devices`, індекси recipient/expiry
  сумісні з міграцією. Користувач підтвердив: devices = 0, main = 0, messages = 0.
- 19.10.2026 користувач погодив міграцію та виконав її додаткову частину через
  D1 Console. Результат sqlite_master підтвердив усі 3 службові таблиці
  (relay_nonces, relay_rate_windows, relay_message_ids) та 4 індекси idx_relay_*.
  Повторна міграція для цього кроку не потрібна; d1_migrations через Console не вели.
- Cloudflare Builds підключено до `arudichik-source/Mavka-Android`, branch `main`.
- 19.10.2026 користувач окремо погодив злиття PR #34 та розгортання Worker.
  PR #34 злито у main: `176f6519c4f2c6b838a41c1a271a42f9c32165f7`.
  Повторного погодження цього випуску не потрібно.
- Реальну ідентичність створено скриптом на Windows користувача 19.10.2026.
  Три публічні параметри перевірено через `configuration()` і Web Crypto importKey;
  приватний ключ не передавався. Повторна генерація ідентичності не потрібна.
- Користувач підтвердив збереження production-налаштувань 19.10.2026:
  root `relay`, Build command `npm run configure`, Deploy command
  `npx wrangler deploy`, Disable builds OFF, Preview branches OFF,
  три публічні Build variables. Перехід від versions upload до deploy погоджений
  дозволом на активацію Worker.
- Невдала збірка `4a0f0b78-32ba-448e-bdb0-345c2903bbe8` була Preview для
  feature-гілки: configure успішний, збій на етапі `wrangler preview`.
  Її не слід плутати з погодженим production-випуском main.
- Цей коміт оновлює стан випуску й має запустити production Build з main.
  Успішне завершення deployment і bootstrap ще потребують перевірки.
- Прямого авторизованого доступу до кабінету Cloudflare в агентському браузері немає.
- Google OAuth відкладено. Розгортання цього Worker вже погоджене користувачем.

## Підготовлено

- Окремий модуль `relay/`; Android-код і попередні інтеграційні гілки не змінені.
- ECDSA P-256 для запитів, закріплений кореневий публічний ключ головної Мавки,
  запрошення пристрою головною Мавкою і підтвердження володіння приватним ключем.
- Геофільтр UA за `request.cf`, nonce в D1, вікно часу ±300 секунд, ліміти запитів і черги.
- Маршрути головна ↔ мобільна Мавка; шифровані AES-GCM пакети з підписом відправника.
- Атомарний ACK і DELETE, безпечні повторні запити, очищення за TTL, службові
  записи доставки без шифротексту для захисту від повторної появи доставлених пакетів.
- Додаткова SQL-міграція, конфігурація з перевіркою публічних параметрів,
  локальний синтетичний клієнт та CI тільки для перевірок, без deployment.
- Windows PowerShell 5.1/CNG: локальне створення неекспортованих ключів головної
  Мавки, стабільна повторна ініціалізація, виведення публічних Build variables.
  Режими `-Bootstrap`/`-Status` призначені для явного запуску після deployment;
  під час підготовки до production Relay запитів не виконували.

## Перевірено локально та в CI

- `npm run check`: 10 сценаріїв успішно на workerd + локальній D1 (Miniflare).
- Справжнє AES-GCM шифрування/розшифрування синтетичного пакета та перевірка
  збереженого підпису; чужий ключ і змінений вміст відхиляються.
- Чужий/неправильний ACK не видаляє пакет; імітована помилка DELETE відкочує ACK.
- Повторний fetch зберігає пакет; ACK видаляє його з активної черги;
  повторний send після ACK не відновлює шифротекст.
- Одночасний повтор nonce, підроблені заголовки країни, зміна тіла/URL,
  відкликані пристрої, plaintext, некоректні ключі, SQL-ін'єкція та oversize відхиляються.
- Nonce зберігається після перезапуску Worker, включно з майбутнім допустимим часом підпису.
- `npm run check-bundle`: Wrangler 4.151.1 dry-run, приблизно 22.6 KiB / gzip 6.3 KiB.
- GitHub CI на `c799288a6e9cbb1b672cb168db16f16e89263b08` успішний:
  https://github.com/arudichik-source/Mavka-Android/actions/runs/39152597714 .
  Linux: протокол/D1 та dry-run; Windows: PowerShell 5.1/CNG, заборона експорту,
  повторне відкриття, прийняття підписаного bootstrap локальним workerd.
- Тести й dry-run використовували синтетичні ключі та UUID; remote-команд не виконували.

## Що залишилось до завершення випуску

1. Дочекатися production Build цього main-коміту. Перевірити успішний deploy,
   binding DB на існуючу mavka-messages, закріплений публічний ключ та Cron */5.
   Не повторювати вже застосовану SQL-міграцію вручну.
2. На тому самому Windows-акаунті виконати bootstrap існуючою CNG-ідентичністю
   (скрипт Initialize-RelayAdmin.ps1 -Bootstrap), потім підписаний -Status.
   Справжні приватні ключі залишаються лише на ПК користувача.
3. Перевірити delivery/ACK та Cron з України на синтетичних даних.
   GET /health перевіряє виконання Worker, але не підтверджує готовність D1.
4. Підключити CNG-ідентичність до інтерфейсу ГОЛОВНОЇ МАВКИ;
   реалізувати Android Keystore, довірене спарювання та API transport.
   Наявний QR HMAC не є автоматично дозволом Relay. Готового APK з Relay поки немає.

Після ACK пакет відсутній в активній базі. D1 Time Travel може зберігати історичний
шифротекст протягом строку провайдера; негайне фізичне стирання всіх резервних копій
не гарантується. Не включати реальні документи, приватні ключі чи API token у Git.

Повний протокол та порядок випуску: [README.md](README.md).
