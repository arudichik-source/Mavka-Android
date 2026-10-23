# MaVka Relay — private R2 encrypted file parts, draft V1

**Status:** implementation branch only; do not deploy before D1 migration and CI checks.
Existing devices, message routes, ECDSA request signatures, nonce replay protection,
country restriction and `DB` binding remain in use. R2 uses the existing private
`FILES` binding to `mavka-private-files`.

## Trust boundary

- The Android / Windows client encrypts *before* upload with AES-256-GCM.
- Worker stores ONLY validated `MAVKA_ENCRYPTED_V1` envelopes, not plaintext,
  file names, application secrets, or private keys.
- **Every** request requires existing ECDSA P-256 `X-Mavka-*` headers; signature
  covers HTTP method, exact URL + query and body bytes, with persistent nonce
  replay prevention. HTTPS and Ukrainian IP restriction still apply.
- R2 object access is never public; no `r2.dev`, bucket keys, presigned URLs,
  or direct client access to R2.
- Only the intended active recipient may download/ACK a part; only the main
  device and its mobiles may exchange file parts.
- Clients MUST authenticate the transfer announcement via an existing signed
  Relay message from the **trusted pairing key**, verify part SHA-256 against the
  manifest, then AES-GCM authenticate/decrypt and assemble parts in index order.
  A sender ID returned by the server is not proof of the sender's identity.

## API

All request bodies are signed JSON, max 49,152 bytes (existing Relay limit).

### POST /v1/files/parts

Body:

```json
{
  "recipientDeviceId": "device-id-from-pairing",
  "transferId": "0123456789abcdef0123456789abcdef",
  "index": 0,
  "count": 2,
  "expiresAt": 1790000000,
  "payload": {
    "format": "MAVKA_ENCRYPTED_V1",
    "algorithm": "AES-256-GCM",
    "keyId": "0123456789abcdef",
    "nonce": "base64-12-bytes",
    "ciphertext": "base64-AES-GCM-ciphertext",
    "tag": "base64-16-bytes"
  }
}
```

Illustrative schema only: use real valid Base64 and a future expiration.
`transferId` is random 32-character lowercase hex. `count` between 1 and
4096; index 0-based; expiration within **6 days**. Encrypted envelope is
strictly checked; plaintext is rejected. Return 201 for created, 200 for
matching idempotent replay with a new signed nonce. A conflicting part returns
409; an already ACKed part cannot be resurrected.

### GET /v1/files/parts/:senderDeviceId/:transferId/:index

Recipient-only, signed GET. Returns sender ID, transfer ID, index, count,
`expiresAt`, `payloadSha256` (SHA-256 hex of canonical serialized encrypted
envelope) and encrypted `payload`. There is no filename or plaintext.
Check the signed transfer announcement and digest before decryption.

### POST /v1/files/parts/:senderDeviceId/:transferId/:index/ack

Recipient-only, signed JSON body `{"payloadSha256":"<64 lowercase hex>"}`.
After local checksum/decryption/storage succeeds, client ACKs. Server retains a
D1 tombstone, deletes the R2 encrypted part, and returns `delivered`.
Repeated ACK is idempotent. A scheduled cleanup retains durable ACK receipts
until 24h **after original transfer expiration**, preventing resurrection while
the transfer remains valid.

## Storage, limits and lifecycle

Objects reside exclusively under:
`transfers/<recipientDeviceId>/<senderDeviceId>/<transferId>/<index-4-digits>`.

The configured R2 lifecycle `cleanup-transfers` deletes uploads after
7 days. A 6-day API TTL leaves a safety margin; this is **not** permanent
archive storage. Existing `documents/` and `backups/` behavior is unchanged.
Hard caps: 4096 parts per transfer, 10,000 pending parts account-wide and
2,000 per sender (checked via D1 ledger), plus existing per-device API
rate limits. Larger files must be encrypted and broken into chunks by
clients; 12,000-byte source chunks agreed in the current relay protocol
remain a sensible default. Never upload an unencrypted attachment to R2.

## Safe rollout (NOT automatic)

1. Run `npm ci --ignore-scripts && npm run check && npm run check-bundle` in
   `relay/`. CI must be green.
2. Before merging the API code, **back up** existing D1.
3. Apply `migrations/0002_r2_file_parts.sql` exactly once to production D1
   `mavka-messages` (it is additive, uses `IF NOT EXISTS`); verify table.
   `wrangler deploy` does not automatically apply D1 migrations.
4. Only then merge the new API branch so GitHub/Cloudflare Builds can deploy.
5. Verify production `/health`, `/v1/status` signed, and test with synthetic
   ciphertext and a real registered device. Keep R2 private.
6. Integrate file notifications and client-side cryptography with Windows
   and Android via separate changes. **No APK changes** are included here.

No migration, production release or real message transfer is triggered by
this draft branch.
