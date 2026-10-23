## Verified Android1.3.1 dependency maintenance — 23 October 2026

Sourcebe90de5c266c1c78668dfa44c2a0e0f71e4c3d14, workflow39927596457 APK/Android15 SUCCESS. No known vulnerable direct/transitive packages in fresh local and independent CI audits; no advisory suppression, restore/audit failure blocks build. MailKit/MimeKit4.18.1, OpenMcdf3.1.4, PKCS10.0.13. MIME24, protected PDF/XLSX portable and native4 checks PASS; retained state-repair15/awake/polling/search/distribution/33000-documents/reset suites PASS. Package/code ua.mavka.mobile.registrationqa/2026102302, display1.3.1-security-qa. Same certificate2410279934353190b0a4eb60e80c9df5d79502856a2838b85c0b7c1b72b266bd. APK artifact12490426660/23298912bytes/SHA25678BF6CB34382F1FEDD215090185A2009C166B5BB7069E0C3493F3C8B5055FCAD; ZIP9c869dedafff32e075f1296b802a9592ac6aa1ab6294b5103bdfbbb2d832c798; audit artifact12490735213; emulator artifact12490184151. Local deliverable builds/Mavka-Android-1.3.1-20261023/Mavka-Android-1.3.1.apk. In-place upgrade, no reset; no physical-phone/live-mail claim. Desktop fresh audit also clear; installed runtime remains0ac7cdf, no new desktop binary required for dependencies. Later docs commit is not a new compiled APK.

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
