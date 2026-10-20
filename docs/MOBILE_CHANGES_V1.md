## 2026-10-20 — Scoped change batches and phone usability IN DEVELOPMENT

Latest operator scope: three access tiers: all current/future units for an administrator/viewer, an explicit subset for other viewers, and exactly one unit for MVO with manual invoice posting. All-unit policy carries an explicit current unit whitelist; only PC expands it and increments revision. Phone offers individual/granted combined views, without access to ungranted units. Observer invoices auto-post locally; initial full mirror never replays historic receipts. New root mirror is separate from the confirmed independent MVO ledger.

User requests automatic local detection of invoices/acts, card/unit/price/quantity edits and deletions, followed by one review/channel choice. Local detection does NO network work. New durable approved batches use MAVKA_CHANGESET_V1; immutable IDs/ciphertext, per-user hashes, optimistic source/grant/key/baseline checks, chunk manifest, ordered/restart-safe staging and explicit tombstones. A permission revision carries full authorized state and rebases phone mirror; stale grants cannot restore access. Switching the registered accounting source is blocked rather than merging unrelated bases.

Main Gmail read-only audit found SUBJECT MAVKA_ returns zero while exact protocol subjects find mail; replaced prefix queries with complete subjects. Sent cleanup targets only locally SMTP-Accepted exact Message-ID/sender/recipient/service subject at next explicit update; uncertain/personal mail stays. MIME split keeps parent ID or deterministic child IDs. Phone notice deletion migrates SQLite v1→v2 with tombstones, clears body, cancels alarms/notifications, prevents redelivery resurrection, and preserves durable-save ACK semantics.

Real test: user confirms initial balances arrived and phone service letters were moved to Trash; Relay sender record later delivered/body-empty and server queue0. Main Gmail had six known accepted sent copies and an older rejected expired request; no development tool deleted real mail. User screenshot confirms opening baseline already posted but UI statically said not posted. New UI queries actual persisted opening receipt, immediately redraws after posting and collapses processed reference snapshot; retains source for read-only reconciliation. Main screen gets direct manual Gmail/Relay update buttons. Background Relay defaults OFF and no approval/enrollment path enables it implicitly.

Status: code WIP, local protocol checks25 PASS; final PC/native/Android build, storage/migration/regression gates and new APK/package delivery PENDING. Installed PC remains72fc803 and delivered phone1.2.1; do not claim these new changes installed. Future phone tests use operator's existing phone/account. Reuse registrationqa package/certificate; in-place update, no uninstall/reset. Desktop Git remains LOCAL ONLY; Mobile feature branch publication authorized. Worker/D1/CNG identity and live accounting DB/credentials untouched.

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
operation list. Each bounded logical part is password-wrapped AES like all other
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

Status: implementation and tests PENDING. Desktop remains local-only. Authorized
Mobile working branch may be updated; existing registrationqa signing identity and
package must be reused for an in-place APK update. Worker/D1 and core accounting
semantics are outside this change. Never alter the operator's live base or delete
real mail during development tests; exercise cleanup through the installed UI
only after final build and operator review.
