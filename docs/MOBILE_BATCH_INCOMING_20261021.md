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

Local checks: bootstrap/crypto/parser 35 PASS; ledger/date policy 20 PASS.
Native Android QA adds a 65-unit import/replay, latest snapshot, zero baseline,
forbidden/revoked scopes, MVO manual action, observer role-change recovery,
partial conflict reporting and bad-row rollback. Release APK and Android 15
emulator verification are required before delivery; no physical-phone exchange
is claimed by these synthetic checks.
