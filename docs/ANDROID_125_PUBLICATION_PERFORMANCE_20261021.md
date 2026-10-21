# Android1.2.5 final native and PC publication evidence — 2026-10-21

Runtime23bc2425ea6b5c71d5082f38b95675527c79df52; workflow39582973900 APK + Android15 emulator SUCCESS. Same ua.mavka.mobile.registrationqa, versionCode20261026, versionName1.2.5-registration-v2-qa, retained certificate2410279934353190b0a4eb60e80c9df5d79502856a2838b85c0b7c1b72b266bd. Install over existing app, no uninstall/re-registration.

Worker-backed Home database counters and inventory/document filtering, WAL reads during import, reusable non-exportable Keystore key handle per bulk read, cancellation of obsolete reads, paged history and no unnecessary single-chunk JSON copies. Combined-view double tap shows granted-unit counts and total, preserving price/unit/category/date identity. Historical initial-invoice edits remain history, not another receipt; pending invoice revisions survive interrupted archive save.

Portable40 PASS; native33000 encrypted document rows confirm menu/Home responsiveness during loading and cancellation; actual double-tap distribution10+5=15 UI PASS. Retained native storage/migration/dedup/role/access/notice/reset and scanner/password/restart checks PASS. Synthetic emulator only; new physical phone exchange is operator-driven.

Compatible PC runtime47f53bffa1c295ce9bbc20e132bec51e55dccf61 installed locally: manual dated accounting sends and automatic changes share durable outbox/cursor/manifests. A first dated stock send carries its companion initial mirror; subsequent changes are diffed automatically. New optional sourceHash metadata does not alter the password+AES or transport contract. Foreground polling remains optional/default OFF and stops with PC window or Android Activity. No server/D1 changes.

Final APK ZIP: https://github.com/arudichik-source/Mavka-Android/actions/runs/39582973900/artifacts/12338510748
ZIP SHA256 2fbdf9d80b2232c84d0e3c2d4c9d020eaefef643522376a8503c35ac4862c8c3
APK SHA256 07E0CB77A781916C05320E0CC0B655F3FC4CEE67A315CD74E26C2898B66598C9
Evidence artifact12338792206 SHA256 eea543501723a7b514658101853c8caad6ba0df9202a92aa6ee25e07be61fad2.

Earlier24aaebc build is a tested intermediate; use the final artifact above.
