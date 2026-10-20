## Android 1.2.3 verified — 20 October 2026

Compiled source0d49db04a7aa4320d6fb7cddbb9aec6e86cd4a23; workflow39438171370 APK and Android15 emulator SUCCESS. Portable35 checks and retained native storage/lifecycle/scanner/inventory/document tests PASS. New item-name parser accepts complete valid names up to1024 characters, without changing identity fields or total bounds. The current desktop bulk stock export is compatible with every granted scope in the read-only cross-check.

Package ua.mavka.mobile.registrationqa, code20261024, display1.2.3-registration-v2-qa. Existing certificate SHA2562410279934353190b0a4eb60e80c9df5d79502856a2838b85c0b7c1b72b266bd retained. APK artifact12272937907 (workflow39438171370); ZIP SHA256110c83fd036ace1b12aebb05634451514c50b0d8d17aca86ff35d2635be5dfea; APK SHA256CB05E3EF5C2D66FD279C291A08507E16D90745C3493ADA063307AF567D92E53C. Install over the existing application, preserving Gmail, password, keys and local data; QR re-enrollment is not needed. Actual new phone-to-PC transport test remains operator-driven; no server/schema changes or live-message send/delete by development tools.

## 2026-10-20 — Full catalogue names / manual stock export follow-up

Manual desktop stock sending exposed the old outgoing18k text cap and too many MIME attachments. PC now emits bounded128k UTF-8 logical content, batches at twenty attachments, and projects granted units together. The unchanged Android password/AES codec accepts a near-limit desktop fixture. Keep max262144-byte inner envelope /196608-byte plaintext reader limits and existing Gmail transport caps.

One further semantic incompatibility: names longer than200 characters caused rejection of an entire valid stock snapshot. Accept catalogue item names up to1024 in stocks and invoices, preserving exact full names and exact numeric values. Unit names, routing/recipient identifiers and other text fields keep their previous200 limit; total JSON and item-count bounds stay intact. Portable35 checks PASS, including long exact names, upper bound and invoice counterpart. Android candidate1.2.3-registration-v2-qa/code20261024 retains the existing package/signing identity; final APK/emulator gate pending. No re-registration, account/key reset or server/schema change.

## Verified Android update — 20 October 2026

Compiled source c509c8670002015645d5441f33cadc5935aacaec: workflow39427263137 APK + Android15 emulator SUCCESS. Portable32 and native SQLite/storage31 checks PASS. Verified persisted password/P256 identity after restart, embedded scanner navigation, background default-off, notice deletion/replay, initial delta ordering, MVO/manual versus observer/automatic posting, opening receipt persistence, individual/all-granted assets and complete chunked document viewing. Synthetic tests do not claim new physical-device two-channel delivery acceptance.

In-place update: package ua.mavka.mobile.registrationqa, versionCode20261023, version1.2.2-registration-v2-qa. Signing certificate SHA2562410279934353190b0a4eb60e80c9df5d79502856a2838b85c0b7c1b72b266bd unchanged. APK artifact12267190736; ZIP SHA256daa0bdee9af5d37382f08846ec8f9b0b61f6025ce5088ed4685ee736621fee57; APK SHA256C90D6711561C49DEE19017AD22A434726C94043C6AE6119C52840BFBAA8D6586. Install over existing app; do not uninstall/reset profiles, account, password or keys. Server/Worker/schema untouched. See MOBILE_SYNC_RUN_UA.md.

## 2026-10-20 — Compressed scoped exchange final candidate

Final candidate: MAVKA_CHANGESET_V1 inside bounded optional Brotli wrapper, password + user AES unchanged. Gmail twenty-attachment same-recipient bundles; Relay unchanged content parts. Source read-only audit:35592 scoped records/65 active units/327 logical parts/1,960,656 compressed content bytes, source unchanged, no network. Manual exchange cleans exactly matched SMTP-accepted Sent copies and specifically obsolete rejected enrollment replaced by an installed identity; unrelated/uncertain mail retained. Native Android QA includes SQLite migration/tombstones, staged restart/order, invoice revisions/receipt-family, opening receipt persistence and primary/combined mirror UI. Portable31 checks PASS. Final candidate APK/native CI and installation still pending. Installed source72fc803/phone1.2.1 remain baseline until verified delivery. No production mail, source data, CNG, server or D1 changes by development tools.

# Confirmed scoped change delivery

Latest operator contract (20 October 2026) supersedes silent invoice-only auto-send:
local changes produce a prepared, scoped batch. The operator approves recipients
already derived from grants and chooses Gmail or Relay once. No network work is
performed by change detection; server background exchange defaults off. Explicit
approval/synchronization executes a bounded channel cycle. Never silently publish
new invoices, acts, catalogue edits, quantity/price/unit corrections or deletions.

Project the current accounting database READ ONLY, through its existing balances
projection. Per authorized unit, track stable asset/category, catalogue-card and
document records. Compare canonical record hashes to each user's last submitted
baseline; unrelated units must not produce a change for that user. Initial sync
is an explicitly reviewed read-only mirror, not historical receipt replay. New
incoming documents may enter MVO's pending local receipt workflow; observers use
existing automatic local receipt posting. Root mirror changes never silently
rewrite an MVO's confirmed independent local ledger. Root documents, cards and
current balances remain available as an authoritative updated view on the phone.

`MAVKA_CHANGESET_V1`: recipientClientId, sourceId, batchId, sequence, baseSequence,
initial, asOf, access, index/count, operations. Operations contain kind, key,
locationId, remove, JSON payload. Canonical manifest hash binds the complete ordered
operation list. Optional full=true explicitly marks a complete permission rebase; a delta arriving before the initial/rebased full batch must wait, even if it has a newer grant revision. Each bounded logical part is password-wrapped AES like all other
service content; Relay retains agreed content-part transport. Persist immutable
IDs/ciphertext before submission; retries change HTTP nonces, never logical IDs.
Recheck current permissions, profile/key generation and source before approval.
Once approved, queue parts durably, then advance sender baseline. Phone stages
encrypted parts durably and verifies scope, manifest, sequence, duplicates and
read-back before applying a complete batch transaction. Out-of-order batches wait;
older batches never roll back a newer view. Deletions are explicit tombstones.

Saved application content/parts are ACKed and removed from Relay/Gmail without
waiting for opening or MVO approval. The user explicitly permits SMTP-accepted
Sent copies to move to Gmail Trash at the next manual mail update. Match ONLY
accepted local outbound Message-ID + exact sender + exact recipient; uncertain
sends and unrelated mail are retained. Preserve Message-IDs when MIME is split.
Use exact known protocol subjects: observed Gmail IMAP SUBJECT MAVKA_ yields0
while complete protocol subjects find the same service letters. Obsolete incoming
registration handling must remain separate from protected document acceptance.

Phone notice deletion is local and cancels pending alarms/notifications. Keep a
small tombstone/hash so cross-channel redelivery cannot resurrect a deleted notice.
Migrate SQLite without resetting existing profiles, mail, exchange keys or data.

Transport extension: logical parts target 80000 UTF-8 bytes, one operation <=100000, at most 2000 parts. A large read-only document is split into header-identical movement chunks and reconstructed only after the complete batch is verified; it must never create a partial local receipt. Compress inside existing password/AES protection using MAVKA_CHANGESET_BROTLI_V1: format, recipientClientId, size (original UTF-8 bytes <=150000), sha256 (lower-hex), data (Base64 Brotli). Bounded streaming decompression checks exact size/hash/recipient and inner changeset. Uncompressed changesets remain accepted. Gmail groups up to twenty separately protected logical parts for the SAME client/key/revision into one MIME message with a stable journal Message-ID. Relay still uses the agreed MAVKA_RELAY_CONTENT_PART_V1 fragmentation, without Worker changes. Manual Relay cycles can recheck an acknowledged queue immediately; background backoff/default-off stay intact.

Invoice revisions use canonical content-version identities and a common receipt family: superseded/cancelled pending versions cannot be posted, and another version of an already posted receipt cannot add stock again. The initial mirror and access rebase do not replay historical receipts. Only a currently allowed, authenticated invoice enters local receipt posting.

Status: portable protocol checks31 PASS, PC changes suite pending final rerun, prior full compatibility869/Core46/native render-interaction-modules PASS. New Android CI/storage/UI and program installation pending. Desktop remains local-only. Authorized
Mobile working branch may be updated; existing registrationqa signing identity and
package must be reused for an in-place APK update. Worker/D1 and core accounting
semantics are outside this change. Never alter the operator's live base or delete
real mail during development tests; exercise cleanup through the installed UI
only after final build and operator review.
