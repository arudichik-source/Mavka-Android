# Registration V2 first-test build — 20 October 2026

Code commit: `216eafdb46e5672ac8b8837253905071ffdb331f`.
The later documentation commit does not change this APK.

[Download APK ZIP](https://github.com/arudichik-source/Mavka-Android/actions/runs/39379685366/artifacts/12245560159).
[Successful build and Android 15 tests](https://github.com/arudichik-source/Mavka-Android/actions/runs/39379685366).

- Package `ua.mavka.mobile.registrationqa`, launcher **Mavka Registration Test**.
- Version code `20261021`, version name `1.2.0-registration-v2-qa`.
- APK SHA-256 `53F1857416DD3E548ADAA6FDA80DE218B5E83884E833F63BBC6CB243EC425FAD`.
- ZIP SHA-256 `355d5fc2373b475f377e5ddf5d123b8bb3b1554edf69f4243c72c5e126858a17`.
- QA signing certificate SHA-256 `2410279934353190b0a4eb60e80c9df5d79502856a2838b85c0b7c1b72b266bd`.

The PC generates the only registration QR; the phone scans it. Enter the same shared
exchange password manually on both devices. The password is never transmitted,
including inside the decrypted initial-key payload. All application messages
require password protection and AES encryption in both directions. Registration
uses Gmail first; after operator approval and protected key installation the phone
activates its registered Relay identity. No second QR is needed. The existing root
PC identity and unchanged Relay server are retained.

Fixed the disposed shared-preferences JNI peer that invalidated the exchange-key
screen. Android 15 emulator checks passed for that screen before/after profile and
restart, manual password persistence, local P256 signing/verification and unchanged
identity fingerprint. Portable Relay checks51 and actual PC-to-Android V2 fixture
checks15 also passed. The emulator used synthetic data and performed no network
enrollment. Physical-phone end-to-end enrollment, role posting and Gmail/Relay
deletion still require the first user test; these are not claimed as passed.

MVO confirms locally; an observer automatically posts locally under PC unit grants.
No business RECEIVED reply goes to the main PC. Technical transport ACK follows
verified durable local saving; it does not wait for opening or MVO approval. Gmail
cleanup moves verified saved mail to Trash. Explicit new QR registration after
deletion creates fresh client/device IDs without resurrecting revoked access.
