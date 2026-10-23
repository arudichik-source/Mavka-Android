# Android 1.3.1 — dependency security maintenance, 23 October 2026

Candidate: versionCode2026102302, display1.3.1-security-qa, existing package
ua.mavka.mobile.registrationqa and retained signing identity. This version
supersedes1.3.0 without requiring uninstall, re-enrollment or data reset.
No accounting, queue, protocol or production mail operations are performed.

Shared explicit versions in DependencyVersions.props:

| Package | Previous resolved | New resolved |
|---|---|---|
| MailKit |4.14.1|4.18.1|
| MimeKit |4.14.0|4.18.1 (MailKit dependency)|
| OpenMcdf |3.1.3|3.1.4|
| System.Security.Cryptography.Pkcs |8.0.1|10.0.13|

MailKit/MimeKit stay in the compatible4.x branch; OpenMcdf uses its patched
3.1.x version, preserving ExcelEncryptor2.0.5.1 API. PKCS is explicitly pinned
to10.0.13 because a MailKit update alone does not guarantee a patched version
for every transitive path (PDFsharp is another dependency).

Primary references:
- https://www.nuget.org/packages/MailKit/4.18.1
- https://github.com/advisories/GHSA-5qwm-7pvp-w988 (OpenMcdf fixed3.1.4)
- https://github.com/advisories/GHSA-846f-f764-76mx (PKCS fixed10.0.13)

Fresh local NuGet audit of all direct/transitive Android packages: no known
vulnerable packages reported. Same audit of the current desktop App: none.
This is a feed-based known-advisory result, not proof of absence of all defects.
No advisory is suppressed. Restore errors out on NU1900/NU1901–NU1904, including
an unavailable vulnerability feed. CI saves its independent JSON audit artifact.

Local checks: Android Debug build succeeded;24 MIME bundle/routing/Ukrainian
metadata/byte-preservation checks PASS; PDF AES256 wrong/correct password PASS;
Excel Agile byte roundtrip and wrong-password denial PASS. Existing compiler
nullable/Android obsolete-API warnings remain outside this security maintenance.

QA adds four actual Android XLSX/PDF encryption/decryption/password assertions
under the existing isolated-emulator/real-profile guards. Existing full Android
storage, queue/repair15, keep-awake, polling, search/distribution,33000 document
UI and reset checks remain enabled. Signed Release/full native CI still pending
for this candidate; never label it verified or replace the delivered APK until
they pass and the retained signing certificate is confirmed.
