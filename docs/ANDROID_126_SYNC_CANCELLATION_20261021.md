# Android 1.2.6 — synchronization cancellation and reset

Runtime source `dd121252bca25f240d1c6fd1f4ef8d7c26d17cb5`, branch `feature/pc-mobile-bootstrap-access-20261019`. Version `1.2.6-registration-v2-qa`, code `20261027`, package `ua.mavka.mobile.registrationqa`. Install over the existing Registration Test application; do not uninstall or repeat QR enrollment.

Gmail returns at most three payload letters per refresh, with key provisioning prioritized. Remaining UIDs remain eligible; a completed portion can be durably imported and moved to Trash before downloading more. Previous intake could decrypt hundreds of attachments and exhaust its timeout before returning any data. UI explicitly indicates when another portion may remain.

Manual Gmail, manual Relay on Home AND settings, foreground refresh, startup recovery and batch posting use a cancellable operation. Reset requires typed confirmation, cancels the exchange and obsolete reads, awaits operation completion and the serial gate, then clears local content. It preserves the profile, Gmail credentials, shared password, AES/Keystore identities and exact access policy. Durable staging/transaction rollback permit safe cancellation and recovery; reset clears both applied and staged data. No server/Worker/D1 or wire-format changes.

Network decryption, legacy archive import and posting execute off the Activity UI thread. Cancellation reaches posting loops, Drive and outgoing file parts. Incomplete change batches are counted before decrypting their bodies. Bulk local record encryption uses a fresh random AES-256 data key wrapped once by the existing non-exportable Keystore master; records each receive a fresh GCM nonce/tag. The plaintext data key is never stored and is zeroed on disposal. Old encrypted records remain readable.

## Verified delivery

Workflow [39599179047](https://github.com/arudichik-source/Mavka-Android/actions/runs/39599179047): APK and Android 15 emulator jobs SUCCESS. Portable142 checks PASS, retained cross-language protocol/crypto checks PASS. Native actual multipart import cancels after50 encrypted writes, rolls back without advancing sequence, resumes500 records exactly and resets without resurrection. Bulk100 fresh-IV round trips PASS. Actual Activity settings reset cancels a synthetic offline exchange holding the gate and completes within the20-second UI test bound. Existing33,000-row menu/Home navigation, per-unit distribution, search, role/grant, scanner/restart/password and interrupted-reset preservation tests PASS. No MaVka ANR/fatal in the evidence. Physical operator-phone performance and real transport exchange still require operator testing.

- [Phone-downloadable APK ZIP](https://github.com/arudichik-source/Mavka-Android/actions/runs/39599179047/artifacts/12345876657), artifact12345876657.
- ZIP SHA256 `a92d32fe907f3f5987e4436bc6d8f0da6c0846c0a0beb730f48ebe0accdd4d2f`.
- APK SHA256 `115E081FE747185FA0F8419D1F2A58B07B2B1FD1E4226F75FAB596CAC3258A2A`.
- Signing certificate SHA256 `2410279934353190b0a4eb60e80c9df5d79502856a2838b85c0b7c1b72b266bd` matches previous Registration Test updates.
- Emulator evidence artifact12345837808; SHA256 `a096997deed2201cd5ce45057d47e690e527a5d35a68a19188b1dc1ad9956689`.

Desktop companion was installed program-only with current accounting, account and key state preserved. No real Gmail/Relay messages or operator-account reset were performed by development tools. Automatic polling remains OFF by default and Activity/window lifetime only.
