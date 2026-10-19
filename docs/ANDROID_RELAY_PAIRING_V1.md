# Android ↔ ПК: Relay enrollment V1 (робочий контракт)

Сервер: https://mavka-relay.arudichik.workers.dev
Робоча Android-гілка: feature/mobile-relay-20261019.
Це контракт клієнтської інтеграції; на момент публікації готової APK з Relay ще немає.

## Окремий QR мобільного пристрою
Старий MAVKA_QR_PAIR_V1 для Gmail не змінюється. Android показує новий QR/JSON:
```json
{
  "format": "MAVKA_RELAY_DEVICE_REQUEST_V1",
  "server": "https://mavka-relay.arudichik.workers.dev",
  "mainDeviceId": "5110f288-ed5f-4d7f-afb5-2c2501f50076",
  "deviceId": "<новий стабільний Relay UUID цього встановлення>",
  "clientId": "<наявний ClientId профілю Мавки>",
  "publicSigningKey": {"kty":"EC","crv":"P-256","x":"<base64url>","y":"<base64url>"},
  "publicEncryptionKey": {"kty":"EC","crv":"P-256","x":"<base64url>","y":"<base64url>"}
}
```

- deviceId окремий від clientId. Відновлення профілю на іншому телефоні не переносить
  право доступу Relay. Приватні ключі не входять до QR/backup.
- ПК звіряє server, mainDeviceId, коректність обох публічних P-256 ключів і надає
  локальне підтвердження реєстрації. Не приймати QR автоматично з неперевіреного листа.
- Після підтвердження ПК підписує POST /v1/devices, передаючи ТІЛЬКИ
  deviceId, publicSigningKey, publicEncryptionKey; format/server/clientId не є полями API.
- Android підписує POST /v1/devices/activate з тілом {}. Це proof of possession;
  статус active встановлюється лише після успішної відповіді сервера.
- ПК зберігає відповідність Relay deviceId ↔ clientId профілю, обидва довірені
  публічні ключі та статус відкликання. Відкликаний deviceId не перевикористовується.

## Довіра до головної Мавки
Android закріплює вже погоджений публічний ключ головної Мавки:
```json
{"kty":"EC","crv":"P-256","x":"CnlXB4fypB0siFfCtSrgL5NoRTcMF-ZUQo6_SuINXpo","y":"0XW_nZEm7LIg51pc5N0KTl1QrQStQZ3NTEFSndG5j_o"}
```
ПК використовує наявну CNG-ідентичність із %LOCALAPPDATA%/MaVka/Relay/admin-public.json.
Не створювати новий кореневий ключ замість наявного. ECDH publicEncryptionKey
для головної Мавки брати з цієї локальної ідентичності, не вигадувати нову пару.
QR не роздає AES-ключі. Поточний/майбутній AES-пакет обміну та його ротація
залишаються наявними; ECDH не запроваджує самовільно новий KDF або AES-формат.

## Транспорт
Підпис запитів, fingerprint, proof доставки та ACK — точно за relay/README.md
і relay/src/protocol.js. Усі пакети Relay мають зовнішній MAVKA_ENCRYPTED_V1.
ACK лише після перевірки довіреного підпису, розшифрування й durable save.
ACK — збереження пакета, не затвердження накладної.

Спочатку узгодити та перевірити на ПК ↔ Android малу зашифровану накладну,
сповіщення і відповідь MAVKA_MOBILE_RECEIPT_V1. Якщо потрібний додатковий
формат транспортних частин, його контракт буде окремо дописаний до цього файлу;
не припускати, що старий великий Gmail/file envelope поміщається в 32 KiB Relay.
