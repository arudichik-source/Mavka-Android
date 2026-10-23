# MaVka Relay: minimized security rejection audit (V1)

**Scope:** `mavka-relay` Worker HTTP rejections; **no D1 migration** and no
persistent custom audit database. Records are streamed into the already-enabled
Cloudflare Workers Observability logs. This first version is intentionally
read-only; it does not provide a new administrative API.

## Event

Each rejected HTTP request emits one line containing JSON with
`event: "MAVKA_RELAY_SECURITY_V1"`. Example (illustrative only):

```json
{"event":"MAVKA_RELAY_SECURITY_V1","outcome":"rejected","category":"authentication","reason":"auth_signature_invalid","status":401,"route":"status","method":"GET","countryScope":"UA","principal":"unverified"}
```

Fields are from **fixed, bounded vocabularies**. They exclude request body,
arbitrary path/query values, client IP, raw country/city, device IDs,
account names, signatures, nonces, public/private keys, credentials, exception
messages and stacks. Routes are grouped; device and transfer identifiers are
not emitted. `countryScope` is one of `UA`, `non_UA`, `unknown`.
`principal` is only `unverified`, `verified_main` or `verified_mobile`;
`unverified` can also mean the request was rejected before the entire
authentication sequence completed.

Authorization and cryptographic checks themselves are unchanged.
The public response remains deliberately generic (`401 unauthorized`),
while internal rejection details are visible **only to Cloudflare admins**.
Distinguishable internal reasons include:
- `auth_headers_invalid`: missing/invalid identity, time, signature or nonce;
- `auth_timestamp_invalid`: timestamp outside replay-protection skew;
- `auth_device_unavailable`: unregistered, pending where not allowed, or revoked;
- `auth_main_binding_invalid`: privileged identity mismatch;
- `auth_public_key_invalid`: malformed stored signing key;
- `auth_signature_invalid`: ECDSA verification failed;
- `request_replayed`, `rate_limited`, `country_not_allowed`,
  `native_clients_only`, `main_required`, `not_found`, etc.

**Critical distinction:** an `outcome: rejected` record does **not**
prove an attack, nor an authenticated device. Routine mistakes, health-check
URLs with query strings, and incorrect client configuration can cause 4xx.
Non-API requests and missing resources are separate categories. Use patterns
in repeated requests to identify suspicious activity, not one 4xx code.

## Query in Cloudflare UI

Navigate to Workers & Pages → `mavka-relay` → Observability → Events,
select a recent window, and search for
`MAVKA_RELAY_SECURITY_V1` in the message field. Open an event and
inspect `reason`, `category`, `status`, `route`, and `countryScope`.

Do not paste entire raw Cloudflare invocation JSON in tickets or public chat.
**Cloudflare's own invocation log can independently contain IP addresses,
request headers and networking metadata** even though *these new application
events do not*. Only admins should have access. Wrangler is configured with
`redact_query_string: true`, but do not assume it redacts every header.

Workers Free observability history has a limited retention window; check
your account's actual policy and quota. This V1 cannot reconstruct older
attempts, supply user identity, or provide a permanent audit history.
Later MaVka Server Control may aggregate these coarse categories to a
restricted long-term statistics table after a separate privacy/retention
and D1 rollout review.

## Deployment safety

- No changes to D1 schema, R2, endpoint routing, signing, or HTTP response codes.
- No body/headers/IP/key logging added. No network or storage write for audit.
- Failure of console telemetry is caught; auth rejection still returns normally.
- Tests assert stable allowed fields, internal reason mapping, no sensitive
  strings in events, and unchanged denial responses.
