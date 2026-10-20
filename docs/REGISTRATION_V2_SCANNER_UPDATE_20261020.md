# Оновлення Мавки на телефоні

[Завантажити APK у ZIP](https://github.com/arudichik-source/Mavka-Android/actions/runs/39387805432/artifacts/12248047602).

Версія **1.2.1-registration-v2-qa**, назва **Mavka Registration Test**.
Це оновлення попереднього Registration Test: пакет і підпис збігаються.
Розпакуйте ZIP, відкрийте APK та оберіть **Оновити**. Старий додаток не видаляйте.
Профіль, Gmail, пароль і локальні ключі зберігаються при оновленні.

ПК уже оновлено — запускайте звичайний ярлик «MaVka — оновлена».
Після оновлення телефона створіть на ПК новий QR, скануйте його у профілі
телефона. Дозвольте доступ до камери: сканер сам читає QR, фотографувати не треба.
Після надсилання заявки одразу натисніть на ПК **Лист надіслано — перевірити пошту**.
Далі виберіть заявку в Користувачах, задайте підрозділи й дозволи, зареєструйте
користувача та надішліть початкові ключі Gmail. Другого QR не потрібно.

Кожен новий QR має окремий разовий ключ і діє 10 хвилин. Створення нового коду
не продовжує старої заявки. Спільний пароль вводиться вручну на ПК й телефоні
та не передається. Фоновий Relay на ПК й телефоні вимкнений за замовчуванням;
ручна синхронізація не вмикає фонового обміну.

Android15: збірка, пароль/ключі після перезапуску, вимкнений фон, вбудований сканер
з живим переглядом віртуальної камери й повернення з нього — перевірено.
Сканування камерою вашого телефона та повну реєстрацію потрібно перевірити зараз.

## 2026-10-20 — DELIVERED PC72fc803 and Android24cc004 in-place scanner update

Supersedes the preceding pending Android scanner record. PC72fc803 remains installed at C:/Нова папка/MaVka,559 verified program files; database, source selection, Office password/profile state, Gmail credentials and root CNG preserved. QR shows expiry/countdown and close-and-check action, expired intake is explicitly explained, rejected protected mail has safe diagnostics. Background Relay defaults OFF; QR creation does not enable it. Manual Check Office performs only one explicit Gmail/Relay cycle. Focused bootstrap30/recipient16/Relay/automatic tests PASS; all8 publishes and7 module usage checks PASS. See docs/checkpoints/REGISTRATION_EXPIRY_SCANNER_20261020.md and private installation proof. No accounting data or real registration changed by tools.

Final Android code24cc0041e32a34d1e1d5208890976e331a849c49, CI39387805432 BOTH build and emulator SUCCESS. Relay51 and actual PC-to-Android V2 codec15 PASS. Android15 tests cover key screen before/after profile/restart, password persistence, P256 proof/fingerprint stability, background checkbox OFF across restart, internal scanner navigation, actual emulated Camera2 preview and cancel/return. AndroidRuntime error log empty. Scanner screenshot reviewed: title/buttons clear of system bars and live synthetic camera image visible. Earlier d368 emulator failure was uppercase-transformed cancel text; the product button now preserves case and system insets are applied. Test now enables emulated back camera and requires preview-running status, retaining all earlier checks. Physical-phone QR decoding and live enrollment/ACK deletion remain pending user acceptance, NOT claimed passed.

APK update: ua.mavka.mobile.registrationqa, Mavka Registration Test, versionCode20261022/versionName1.2.1-registration-v2-qa. SAME certificate as previous216eafd version20261021: SHA2562410279934353190b0a4eb60e80c9df5d79502856a2838b85c0b7c1b72b266bd. Intended in-place Android UPDATE, not another package; do not uninstall the previous Registration Test or erase its Gmail/profile/password/keys. Different obsolete QR Test/Relay QA/original package IDs remain separate. No private signing key is requested from user.

Artifact https://github.com/arudichik-source/Mavka-Android/actions/runs/39387805432/artifacts/12248047602 ; ZIP22196355 bytes/SHA367ff04779c11fa3fc64ed4f3e7cc77c58b221c81bd8c62aba3068ca1dabab1d. APK22344544 bytes/SHAD8FCDFAAE0595375D929F6F2E7D6ECA162C014267BB82FB952DA863D8A726E29, locally x20/builds/Mavka-Registration-Update-24cc004/ua.mavka.mobile.registrationqa-Signed.apk. Emulator evidence12248915509 retained locally x20/relay-20261019/emulator-24cc. Workflow artifact expires2027-01-18; local copy retained.

Operator next: update phone first, retain configuration, then create a fresh PC QR. Phone scans inside MaVka without taking a photo; original photo picker remains optional. Confirm protected request and immediately click PC QR's check-mail button before expiry, then operator selects user/grants and sends initial keys. Every QR click generates a new one-time secret; an already displayed QR does not auto-refresh and creating another does not extend the previous request. Never bypass expiry or restore revoked trust. All application data still uses password plus AES; the manually entered shared password is never transmitted. User may launch usual PC shortcut; background remains off unless explicitly enabled. Desktop commits remain local only; Mobile branch publication authorized. No Worker/D1 change or real mail/ACK sent by development tools.
