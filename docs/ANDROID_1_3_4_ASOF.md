# 1.3.4 — historical PC balances and shotgun icons

`Майно → Станом на…` calculates an inclusive end-of-day balance locally from signed daily PC movements. It is available for both observer and MVO roles and remains independent from the phone's confirmation ledger. `Останні дані ПК` returns to the latest applied PC head. Item cards and the combined unit distribution use the selected date.

The PC asset payload now includes optional `history` version 1: `from`, `complete`, exact decimal `opening`, and ordered `{date,delta}` changes. The tail is bounded to 512 nonzero daily deltas. A trimmed opening supports dates from its explicit boundary; earlier dates report unavailable. Complete timelines yield zero before the first movement. Current-zero records are retained for historical display. The full timeline must reconcile exactly to current quantity before accepting a changeset. Future dates relative to applied PC data are unavailable. Old packages work at their head date and ask for an updated PC package for past dates; local MVO holdings are never substituted.

History uses movement dates, includes posted corrections/openings, excludes cancelled document movements and carries only the recipient's permitted unit/category. Existing asset hashes make subsequent updates incremental. Date-only updates advance the authenticated head with an empty batch rather than retransmitting every record. No background polling/service was added.

Gauge matching recognizes `12/70`, spaced `/`, Latin/Cyrillic x and ×; atlas category 6 is the existing shotgun shell. Decimal 12.7 and embedded 112/70/12/700 do not match.

Validation: portable timeline 22 cases; BootstrapChecks and AssetIconChecks passed locally. Android C# compile-only passed with 0 errors (existing platform warnings remain); no local APK was generated or downloaded. Native calendar/card/current reset and MVO checks were added to the existing Android 15 full suite. Signed APK and full native CI results must pass before delivery. Code 2026102401; signing identity/package unchanged. PC source/export upgrade is required to receive historical payloads.
