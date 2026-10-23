# Android 1.3.3 verified — 2026-10-23

Final source b94e42514ef1be5e7a237e544d777ed7f9dd8b57 passed workflow39962246822: APK/signature/dependency audit and Android15 emulator SUCCESS. Artifact12505666370,23260140 bytes,digest88ac2b2d6ac5976136f80277790998734108999c7f41f097364d01be8d81d188. Download https://github.com/arudichik-source/Mavka-Android/actions/runs/39962246822/artifacts/12505666370 . Same package and certificate; in-place update over1.3.2. No APK or APK ZIP downloaded to PC.

Native observer/MVO/document scope/filter/empty mirror, actual RemoteViews in both palettes and two one-shot buttons without Activity launch,33k encrypted documents/responsive navigation, retained progress/cancellation/busy reset/protected files all PASS. AndroidRuntime error log empty. Widget screenshot reviewed: navigation and both exchange actions readable in green/blue. Emulator is synthetic/offline without credentials; physical-phone/live-account acceptance remains user-driven.

User scope: current PC balances as the primary inventory; observer UI without manual MVO posting/date/ledger controls; separate current documents/local archive; only authorized units with an allowed-unit selector; compact type/status/inclusive-period filters on both document tabs. PC column filters remain unchanged by the separate desktop addition.

Applied heads distinguish a valid empty mirror from an unapplied full package. Display the PC as-of date, never substitute the local ledger or phone today. MVO manual confirmation and local history remain available. This is a presentation change, not replacement or deletion of the independent ledger.

Document JSON is decrypted/projected once per screen, sorted once, filtered off the UI thread and rendered in pages of 50. Archive metadata is loaded off the UI thread with obsolete-screen guards and debounced search. Authorization is rechecked on render and open.

Foreground auto-refresh defaults off; supports 1/2/5/10/15/30/60/120/240 minutes. New default interval is one hour; existing saved choices remain. A cancellable timer sleeps until due instead of waking every five seconds. OnPause stops foreground polling and in-flight foreground I/O; no periodic job is added.

Launcher widget keeps navigation and adds two immutable private broadcast actions: update the configured channel(s), or receive Gmail. Each tap schedules one nonperiodic, nonpersistent network-constrained JobService, limited to four minutes and the existing 128-pass mail budget. No automatic retry or boot scheduling. OS may defer a job until network/quota permits it. Repeated taps do not duplicate a job. The Activity and widget exclude overlapping exchange; account reset cancels/awaits the widget before clearing data. Generic progress/result in the widget contains no names, message bodies or inventory.

Widget Gmail accepts only the existing sender/password/AES authenticated client/unit-bound JSON, current mirror protocol, notices and archive documents. Readback precedes ready markers; MVO invoices remain manual. It never deletes mail. Unsupported PDF/Excel/file-transfer attachments remain for the existing foreground viewer. A time-limited run preserves authenticated staged parts for the next explicit run. Unsupported content or posting issues are reported as incomplete, never successful application of the entire database.

Local validation: compile-only (no APK output/download), portable ArchiveChecks including filter boundaries, BootstrapChecks including hourly/backoff/paused/busy contracts. Native CI adds observer/MVO role UI, document selector/filter dialogs, valid empty-mirror behavior, actual RemoteViews button actions without Activity launch, and exchange progress across navigation. Retains 33k encrypted document responsiveness, reset, security, multipart repair and protected-file gates. Final CI passed as recorded above; physical-phone/live-account acceptance remains pending.

Package remains ua.mavka.mobile.registrationqa, versionCode 2026102304 / 1.3.3-documents-widget-qa with retained CI certificate. Deliver through GitHub only. Do not download/store ready APK or APK ZIP on PC.

Android contracts checked against primary documentation: https://developer.android.com/develop/ui/views/appwidgets/advanced and https://developer.android.com/reference/android/app/job/JobService .

Native candidate4b5c167 run39959404093 passed current PC inventory, search/distribution, observer/MVO controls, document/filter scope and empty mirror UI. It then exposed an older widget XML defect: generic android.view.View dividers cannot be inflated by RemoteViews. Replaced the divider with supported TextView in both palettes. Added a portable whitelist regression and actual two-palette RemoteViews inflation; no test/gate removed. Corrected source b94e425 passed the final full CI; the failed intermediate candidate is not the delivered APK.

Persistent-background audit also found the historical opt-in 15-minute notice job. It is now an inert compatibility service; startup cancels it and migrates its flag to false. The obsolete toggle is replaced with foreground/widget guidance. Local dated reminder alarms/boot rescheduling are retained. Native widget QA simulates an old true flag and verifies it cannot enable mail polling.
