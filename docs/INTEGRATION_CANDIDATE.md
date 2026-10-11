# Mavka Mobile — Unified Integration Candidate

**Date:** 2026-10-12
**Candidate branch:** `integration/mobile-unified-rc-20261012`
**Base main commit:** `af0fb026f9816c27acfb369508b9f7ad972df6dd`
**Consolidated source head:** `1b9c07df489bb5c18616f48d67ea1178f0fdbade`
**Last pre-consolidation successful GitHub run:** #98 (`38185743521`)
**Release status:** **CANDIDATE — NOT APPROVED / NOT PRODUCTION SIGNED**

## Scope and proof of inclusion

All commits from the following twelve *stacked* PR heads are present in
the candidate commit history. `tools/verify-stacked-integration.sh`
verifies each with `git merge-base --is-ancestor`; the check runs
before any APK build.

| PR | Head at consolidation | High-level software workstream |
|---|---|---|
| #16 | `6da443520853c527f2f6e4601bc6db46bbcd4a04` | Encrypted local archive, profile validation |
| #17 | `16a1c0af3fbd32b75c82e629bdf079b5d8ad2a2` | Database history and date handling |
| #18 | `f907775ca656fa8215c33744d296e5e6e973f163` | Offline delivery resilience |
| #19 | `a98abc3c4eed3f6b0ba5c87cef7340416f5b6ce1` | Typed OOXML document generator |
| #20 | `93eb020be1ee7bfa09b3f964a503f6dd7c0418ad` | Android navigation and accessibility layout |
| #21 | `2a1c306acd5e876848f2cdae0a48c4d32c69cde6` | Encrypted archive migration and status history |
| #22 | `0348fdb35ce8b4edb60ef8fb76f54109acb98ba9` | Read-only data consistency inspection |
| #23 | `a39251af2f45a7a2495fcb4c205f8061f30db597` | Resilience and migration regression tests |
| #24 | `5b9dd669875dc16ea0a693ce53325bccfa2a24ab` | Scoped MediaStore exported-file management |
| #25 | `523362b6ea510693ae509b73f8c72e799a80e916` | Password-protected PDF files |
| #26 | `c21d01bd7073c5792d47a1d18f66f48dfcc9cb65` | Password-protected Excel files |
| #27 | `1b9c07df489bb5c18616f48d67ea1178f0fdbade` | PDF pagination and file UI performance |

**Meaning:** Git history proves source inclusion, not end-to-end
correctness, desktop interoperability, or requirement coverage.
There are no independent cherry-picks and thus no duplication.

## Gates before promotion to stable main

- [x] One consolidated Git commit ancestry, all PR #16–27 included.
- [x] Prior build #98 succeeded and published separate production/preview
      APK artifacts from the same integrated source.
- [ ] Candidate-specific full GitHub CI succeeds (integration gate + APK).
- [ ] Device check using **preview package only**
      (`ua.mavka.mobile.preview`) on fabricated data.
- [ ] Real Android PDF/XLSX compatibility verified; no plaintext files.
- [ ] No loss/corruption of existing encrypted data, database, or key
      material across a representative install/update/migration test.
- [ ] Stable signing identity established; CI ephemeral signing key is NOT
      sufficient to update an existing production app.
- [ ] Required external desktop counterpart validated end-to-end.
- [ ] Full specification checked line-by-line, with documented exclusions.
- [ ] Safety/security review and authorized release approval.

## Deployment and PR policy

- **Do not** merge every stacked PR independently: the latest stacked
  head already contains every preceding PR commit.
- Never overwrite a previously installed production APK with a build
  from an unrelated signing key. No uninstall or data reset.
- Do not tag this branch as a stable release or advertise its generic
  green CI as completed user acceptance.
- Keep all original PRs and their histories available for diagnosis.
- `docs/ANDROID_DEVICE_ACCEPTANCE.md` is the practical verification
  checklist; progress on this checklist must reflect executed tests
  rather than assumptions.

The candidate consolidates existing code for software QA. Changes to
operational processes involving controlled or dangerous items are out
of scope for this generic integration/security review.
