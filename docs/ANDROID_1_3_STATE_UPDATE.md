## Verified release candidate — 23 October 2026

Compiled source8b953f5525084a382e6542d3fb0d9a1571ca9d28. Workflow39922737956 APK and Android15 emulator SUCCESS. Existing package/signing certificate retained; native state-request suite15 PASS, foreground keep-awake/pause release, automatic refresh, all-unit search/distribution, documents,33000-row responsive navigation and busy-reset/credentials preservation PASS. Runtime errors log empty. No real accounts/mail operated.

APK artifact12487194914, 23057248bytes; APK SHA2568ECC4B4FADCC83F1F6F9D8FAFBAEDF53094474CB7571A90DA375E589FECB6B1B. ZIP SHA256da1f216efba8c332266fbbca750c39ee5a8b50afdc184a90ae5d5f332b5cb644. Evidence artifact12488542723. Phone installation and live-channel acceptance are operator-driven. Matching desktop source0ac7cdf installed locally with586 program files verified and297 protected paths unchanged. Retained NuGet dependency advisories remain a separate maintenance item. Later docs-only commits do not change the tested APK source.

# Android 1.3.0 — state request and queue maintenance

Candidate dated 23 October 2026. Package `ua.mavka.mobile.registrationqa`,
versionCode `2026102301`. Keep the installed QA signing certificate
`2410279934353190b0a4eb60e80c9df5d79502856a2838b85c0b7c1b72b266bd`.
Never uninstall/reset the user's app to install this update. The workflow refuses
to generate a replacement key if the retained signing cache is unavailable.

## Included

- Foreground window stays awake; OnPause clears KEEP_SCREEN_ON. No wake lock or
  background service is introduced. Existing opt-in foreground polling remains
  1/2/5/10 minutes and stops on pause.
- Home/right drawer → «Оновлення бази та черга»: request an update from the main
  MaVka using encrypted hashes of the actually applied mirror, source and sequence.
  Gmail/password/AES and durable Relay transport reuse existing identities.
- An unchanged report retry reuses its identity. Ordinary deltas wait while a
  requested repair is pending; the user can cancel waiting. A response binds to
  the exact request/head/hash and validates the resulting complete scoped state.
  Apply is atomic, complements existing records and does not replay historical
  receipts into the independent local ledger.
- Obsolete received queue parts are automatically removed only after verified
  coverage by an applied head, after a flushed/read-back-verified encrypted backup.
  Manual «Очистити чергу (з резервною копією)» can clear staged received parts,
  preserving applied stock/documents, profile, credentials, keys and access.
  Three automatic backups are retained; manually requested backups are separate.
- «Перевірити копію черги» checks integrity and compares applied phone state at
  cleanup time. It does not assert that the PC has no newer changes.
- SQLite schema 1/2 → 3 preserves records/heads/receipts and adds request history
  and an authenticated sequence index for parts.

## Compatibility and remaining acceptance

State-report requests require the matching desktop Mail update. An older desktop
cannot prepare the requested repair. Normal existing mail/Relay changes remain
compatible. PC preparation is not phone application: only a new phone report
proves applied state. This update does not modify Worker/D1 or core accounting.

Prior stock search across units, double-tap distribution, document history,
bulk receipt confirmation, reset and large-catalogue cancellation are retained
and exercised by the existing native emulator suite. Physical-phone delivery
through the user's real Gmail/Relay remains operator acceptance; development
checks never send/delete real mail or alter the user's accounting base.

Local candidate: Android Debug compilation succeeded (existing dependency
advisory/compiler warnings remain); portable bootstrap 142 retained checks plus
7 request guards passed; Relay portable 50 passed. Release signing and native
Android lifecycle/SQLite/UI gates must finish before delivering this candidate.
