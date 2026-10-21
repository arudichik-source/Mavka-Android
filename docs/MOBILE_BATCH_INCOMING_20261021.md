# Android 1.2.4: incoming inventory across granted units

The PC remains on the installed `2451a57` hotfix. This Android-only change retains
the registrationqa package/signing identity, saved account, common password and
exchange keys. No enrollment or transport protocol changes are needed.

- Observers automatically post opening snapshots as well as new invoices within
  their explicit unit grants. Startup and manual Gmail/Relay refresh recover
  already archived pending documents, including after a role/access change.
- Assets and document history offer one local **Зарахувати всі отримані надходження**
  action across every currently granted unit, independent of the screen filter.
  MVO still requires this explicit confirmation and remains limited to one unit.
- Pick the newest accounting-date snapshot per unit, never add dated snapshots
  together. A durable opening baseline is never repeated or added over existing
  receipts. A zero-position snapshot can establish a zero baseline.
- Preserve invoice family/current-version checks, exact recipient grants, source
  read-back and per-document SQLite transactions. Replay is idempotent; conflicting
  scopes are listed while other units can succeed. Storage failures remain errors.
- Posting sends no MVO acknowledgement to the PC and does not enable background
  synchronization. Authenticated durable transport ACK/cleanup remain separate.
- Remove the settings **Діагностика / Галерея іконок (демо)** card.
- Keep the same case-insensitive name/NSN search when switching between a unit
  and all units, for both the root mirror and legacy snapshots. Legacy all-unit
  snapshots load off the UI thread and render at most 50 filtered rows per page;
  retain distinct unit/date/price rows and current grants.
- Authenticated notices from the PC persist their reminder time in the same SQLite
  insert and schedule the local Android alarm without an extra phone confirmation.
  Both transports use the same receive path. Delayed/overdue notices are still
  imported and reminded once; replay preserves user snooze/cancel/delete actions.
- Add an explicit whole-account local content reset, confirmed by typing
  **ОБНУЛИТИ**. Clear every unit's ledger, mirror, archive/history/trash, received
  attachment vault and pending document/file queues. Keep the user/client/device,
  Gmail credentials, manual password, current/future exchange keys, Keystore and
  exact access grants. Do not change the PC database or remote mail/server data.
  Retain processed-mail deduplication and authenticated transport cleanup evidence.
  A durable reset marker resumes an interrupted multi-store clear before recovery;
  serialize with Relay/recovery/batch work and require a fresh root stock delivery.

Local checks: bootstrap/crypto/parser 35 PASS; ledger/date policy 20 PASS.
Native Android QA adds a 65-unit import/replay, latest snapshot, zero baseline,
forbidden/revoked scopes, MVO manual action, observer role-change recovery,
partial conflict reporting and bad-row rollback. Release APK and Android 15
emulator verification are required before delivery; no physical-phone exchange
is claimed by these synthetic checks.

Additional native reset QA checks the complete clear, trash removal, restart
without old snapshots returning, exact grant/profile/Gmail/manual-password and
both AES keys remaining usable, existing Keystore ciphertext decryption,
background-off remaining off, replay and interrupted-reset recovery. It runs only
with the existing guarded synthetic offline emulator profile, never on a real account.
