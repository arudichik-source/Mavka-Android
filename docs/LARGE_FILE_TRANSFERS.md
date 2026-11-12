# Encrypted large files: Android implementation, 2026-10-12

Working PR: #31. No changes to approved key package, ExchangeKeyStore or rotation.

## Implemented
- PDF, XLSX, DOCX and DOC: authenticated encryption, separate private vault,
  explicit SAF plaintext-copy export, and confirmed local deletion.
- Up to 32,000,000 source bytes, split into 256,000-byte parts (max 125).
- Each `.mavkapart` is `MAVKA_FILE_PART_V1` / AES-256-GCM. AAD binds key ID,
  transfer ID, original name/MIME, whole size/SHA-256, index and part count.
- Parts may arrive out of order; duplicates do not duplicate inventory or files.
  Conflicting metadata, content or authentication tags fail closed.
- Accepted parts are re-encrypted locally using Android Keystore, atomically
  written and read back. Partial assembly survives restarts and key rotation
  for already received parts. Missing parts still require their original key.
- Whole-file size/signature/hash and private-vault read-back precede Gmail Trash.
  A durable completion and cleanup checkpoint allows retry after interrupted Trash.
  Mixed letters remain for explicit manual handling.
- Gmail part messages are searched independently of the latest 15 ordinary mails;
  up to 150 part messages per refresh. Refresh again to process a larger backlog.
- Outgoing encrypted parts and the SMTP checkpoint survive restart. Reselecting
  the same file resumes the existing queued transfer. Files screen exposes resume
  and confirmed deletion of the outgoing queue and incomplete incoming transfers.
- Source bytes never go into a mail, Drive bundle, accounting DB or public temp file.
- Legacy `.mavkafile`: distinct 4.3 MB MIME cap and 3.1 MB decoded cap. Decoder
  writes are bounded while reading, before an oversized allocation can complete.
- Confirmations without keys stay pending; only bootstrap registration may be plain.

## Google Drive
An existing `MAVKA_ENCRYPTED_V1` JSON envelope from the exact authorized Gmail
sender decrypts to this command (camelCase fields):
`format=MAVKA_DRIVE_FILE_V1`, `fileId`, `downloadUrl`, `encryptedSize`,
`sha256` (uppercase hash of exact encrypted bundle bytes), `transferId`.

Only authenticated live mail can start the automatic download. Plain JSON and
cached previews cannot. The download is HTTPS, bounded to the advertised size,
with a Google-host redirect allowlist and a checksum before parsing.
Drive contains `MAVKA_FILE_BUNDLE_V1` JSON with `parts` as encrypted packet
objects, never the plaintext Office/PDF file. The command's transfer ID must
match all parts. Failed/partial downloads keep the Gmail command for retry;
received parts are checkpointed. The network download itself currently restarts,
not HTTP Range resume.

Current route accepts an already accessible Google Drive blob download link
(e.g. its webContentLink). It DOES NOT implement Google-account OAuth login,
refresh tokens or access to private files. HTML sign-in/confirmation pages fail
closed. A private-Drive OAuth client and consent configuration remain a separate
integration step. No files are made public by the Android app.

## Counterpart/test producer
`tools/MavkaEncrypt` uses an existing 32-byte test key for synthetic testing:
```
dotnet run --project tools/MavkaEncrypt -- file-parts test.key test.pdf parts-output
dotnet run --project tools/MavkaEncrypt -- drive-command test.key parts-output/encrypted-drive-bundle.json FILE_ID GOOGLE_DOWNLOAD_LINK command.json
```
Send `.mavkapart` files separately with subject `MAVKA_FILE_PART_V1`, or upload
only the encrypted bundle and send `command.json` from the configured sender.
No mail/Drive upload is performed by this tool. Production desktop automation
must adopt the documented protocol; it has not been changed in this repository.

## Verification and remaining device checks
`tools/TransferChecks` covers real GCM, metadata/tag tampering, out-of-order
assembly, duplicates, restart persistence, SMTP checkpoint, scoped deletion,
Word roundtrip, Drive origin/size validation and bounded download failures.
CI compiles actual Android integration. SQL migration smoke checks remain green.
Device Gmail/Drive delivery, Office viewers, Keystore recovery, largest-file
memory behavior and a stable APK signing identity still require practical tests.
The old native PDF/XLSX password-export route remains separate from this transfer.
DOC/DOCX reception/export is supported; Word document generation is not added.
