# PC-generated QR registration V2

This supersedes the two-QR bootstrap test. The phone only scans the PC invitation;
it does not display or copy its public device QR for registration. Gmail is used
for the first encrypted request because the unchanged Relay server rejects
unregistered signing identities. No Worker, API schema or D1 change is required.

The same shared exchange password is manually entered in PC Office settings and
Android password settings. It is NEVER a field in a QR, request or key payload,
including the decrypted key payload. There is no built-in/default password.

1. PC creates `MAVKA_REGISTRATION_QR_V2`: `format`, `server`, `mainEmail`,
   `mainDeviceId`, `publicSigningKey`, random `sessionId`, Unix `expiresAt` (10 min),
   random 32-byte standard Base64 `bootstrapKey`. Fixed existing root identity.
2. Derive 32-byte direction keys with HKDF-SHA256: input = decoded bootstrapKey,
   salt = UTF8(sessionId), info = UTF8(`MAVKA_REGISTRATION_V2:request`) or
   UTF8(`MAVKA_REGISTRATION_V2:keys`). Nonces are independently random 12 bytes.
3. Phone saves an immutable `MAVKA_REGISTRATION_REQUEST_V2` with `sessionId`,
   `requestId`, `clientId`, `email`, `fullName`, `rank`, `position`, `unit`, `isMvo`,
   `phoneNumber`, `deviceId`, `publicSigningKey`, `publicEncryptionKey`.
   A local private-key signature proof runs before sending; private keys never leave
   Keystore. Request wire = AES(request-direction key, PASSWORD_WRAP(AES(request,
   request-direction key), manually entered password)). Existing AES envelope AAD
   is UTF8(`MAVKA_ENCRYPTED_V1:` + SHA256(key)[0:16] lower hex). PASSWORD_WRAP uses
   existing `MAVKA_PASSWORD_WRAPPED_V1`, PBKDF2-SHA256 350000, random salt/AES-GCM.
4. SMTP subject = request format; JSON attachment; stable Message-ID from requestId.
   Retrying the same scanned invitation reuses its exact request ID/ciphertext.
   PC manual Check Office selects only live enrollment subjects plus known service
   senders, validates sender, password, invitation and key points, and pins a pending
   device/profile. Scanning alone never grants access. Operator approves user and
   grants (MVO exactly one unit/manual local posting; observer one or more units/
   automatic local invoice posting). PC registers the pinned device with root CNG.
5. PC sends `MAVKA_BOOTSTRAP_KEYS_V2`: `format`, `sessionId`, `requestId`,
   `messageId`, `clientId`, `recipientDeviceId`, `mainDeviceId`, `expiresAt`,
   `generation` (1), `currentKey`, `futureKey`, `access`. EXACTLY these fields;
   no password. Response uses the same three layers with the keys-direction key.
   It travels by Gmail or the existing Relay AES envelope. No second QR.
6. Android verifies binding/expiry/password/AES, atomically saves key pair,
   permissions and receipt, clears the one-time secret, and verifies readback.
   `MAVKA_BOOTSTRAP_KEYS_ACK_V2` confirms technical installation to PC, not receipt
   or approval of an accounting document. PC clears the invitation secret after ACK.

New logical messages in either direction use exchange-key AES + the password
wrapper. Relay carries password-wrapped bytes as the agreed 12000-byte content
parts, each in its own signed/checked AES transport packet. Fresh nonce on HTTP
retry, same message IDs/ciphertext; durable local queues and cross-channel dedup.
New V2 profiles reject service content without the password layer.

Deletion revokes the old device ID and removes active Office access. Explicitly
scanning/confirming a NEW invitation starts a fresh client/device registration;
revoked IDs are never reused and old mail cannot resurrect trust. Previous local
documents and wrapped historical AES keys remain. The root PC CNG identity is
never recreated. Repeated scans of the SAME invitation keep the same request.

MVO confirms locally; observers auto-post to local inventory under their grants.
No business RECEIVED confirmation is sent to the main PC. Transport ACK/deletion
occurs after signature/password/AES checks and durable local storage, without
waiting for opening or MVO approval. Gmail cleanup moves only verified saved mail
to Trash; Relay ACK uses the recipient signature and deletes its server packet.
Each recipient has separate encrypted messages/IDs, so one recipient cannot delete
another recipient's packet.

QA package: `ua.mavka.mobile.registrationqa`, Mavka Registration Test, distinct
from the obsolete bootstrap QA. CI caches a QA-only signing identity; production
signing workflow/secrets are untouched. Portable fixture checks and Android 15
settings/Keystore/restart smoke are gates. Real PC ↔ phone acceptance is still
required; the screenshot-only key-screen failure has no proven root cause yet.
