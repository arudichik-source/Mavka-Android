## Android1.3.2 Gmail update candidate — 23 October2026

Operator phone screenshots confirm60 protected parts/3 mails per tap, complete portions acknowledged but mirror still waiting. PC journal has34 sent Gmail letters for2734 asset/32909 document operations. This was a partial-mail refresh UX/performance defect, not evidence that the APK upgrade failed.

One manual/foreground Gmail action now drains successive bounded3-message portions until no unattempted work. Failed/unsupported UIDs remain in Gmail for next refresh but cannot starve later parts in this session. UIDVALIDITY-scoped attempt set; finite128-pass budget and cancellable15-minute Gmail session, durable stages preserved on timeout/reset. Existing AES/sender/access/hash/complete-batch checks unchanged. Incoming Gmail changes metadata now includes oldest unprocessed pages rather than repeatedly taking only newest100.

Bounded flat changeset MIME <=4MB and <=50JSON attachments is retrieved as one message and sender/subject revalidated; exact part paths preserve existing bounded decode/authentication. Other MIME stays on-demand. No per-attachment network roundtrip for this standard format. Physical-device speed remains unmeasured.

Visible staged N/M parts and applied asset/document counts distinguish queue from current mirror. The separate local MVO ledger is preserved. No user must clear queue or resend PC package merely to install this fix. Existing incomplete parts resume after upgrade.

Local: compile including isolated QA succeeds (baseline nullable/Android API warnings retained), MIME51 and portable registration/drain cases PASS. New native34-part requested response verifies3/34 progress, no partial application and final34 documents; pending Android15 CI. Existing security audit/protected PDF-XLSX/33000-doc/lifecycle/reset gates retained. VersionCode2026102303, display1.3.2-gmail-qa, same package and retained signer required.

DELIVERY PREFERENCE: operator wants GitHub-only Android installers; no ready APK/APK ZIP copies on PC.1.3.1 delivery copies deleted after verifying unexpired GitHub artifact12490426660; historical local paths in older notes are no longer deliverables. Keep source/tests/evidence, generate signed APK only in existing authorized feature-branch CI. No main push or production release. No live messages sent/deleted by development tools.