# MaVka Server Control 0.6.0 — separate, read-only Cloudflare console

The initial 0.1 application was deployed successfully to a separate Cloudflare Worker.\nVersion 0.2 introduced real historical graphs and improved read-only reporting;\nverify current deployment status in the Cloudflare dashboard.
It is isolated from `mavka-relay` and its `mavka-messages` database. It
does **not** modify, proxy or call the MaVka message API.

- Independent Worker: `mavka-server-control`.
- Independent D1: `mavka-control` bound as `CONTROL_DB`.
- Ukrainian responsive dashboard with Worker, D1 and R2 Cloudflare GraphQL
  telemetry, 15-minute browser/isolate refresh, quota guides and compact
  30-day snapshots (at most one per quarter hour).
- **Read-only** API: `GET /`, `GET /api/summary`,
  `GET /api/history`, `GET /api/trends`, `GET /api/security`, `GET /api/maintenance`, `GET /api/about`.
- One UTC daily cron for local D1 expiry pruning, but no browser polling after the tab closes, no direct relay device access,
  no R2 object listing, no relay D1 bindings, and no mutation endpoint.
- Workers usage is **mavka-relay only**, not account-wide billable requests;
  D1 row counts are account-wide; R2 current bucket bytes are **not** GB-months.
  Class A/B billing totals are intentionally not guessed. A missing GraphQL
  dataset shows *unavailable*, never fabricated 0.
- Owner-defined R2 bucket threshold: **5 GB (5,000,000,000 bytes)** with
  display warnings at 70%, 85%, and 95%. This is **monitoring only**, not a
  Cloudflare-imposed storage limit, billing allowance, or upload rejection.
  Cloudflare's included R2 Standard storage is billed in GB-months.
- Safe diagnostic error codes distinguish provider validation, auth, timeout,
  runtime TypeError, and network-related failures without leaking API tokens
  or remote response bodies. Failed datasets are never displayed as 0.
- Only auth-approved administrators may access any HTML or API endpoint.
- **0.2**: `GET /api/trends?period=24h|7d|30d` returns only real
  sampled observations (15 minutes / hourly / daily), never interpolated.
  `GET /api/history?period=30d&before=<unix_seconds>` pages 50 stored
  records at a time with a bounded cursor, allowing review of up to 30 days.
  Charts only exist for times when the dashboard actually stored samples.
- The read-only dashboard also displays a security/device integration state.
  `GET /api/security` explicitly returns `not_connected` until a
  **dedicated least-privilege, privacy-safe telemetry channel** is built.
  No fake security events or device counts are displayed; **do not** give
  the control Worker a binding to the private `mavka-messages` D1.
- 0.2 retains the 5 GB owner-defined R2 monitoring threshold and no
  external scripts, CDNs, databases or cryptographic key access.
- **0.2.1**: automatically zoom the chart X-axis to the interval actually
  covered by stored observations when sampling history is still short.
  Label zoomed views and keep UTC timestamps explicit. Use integer Y-axis
  tick marks for counters, avoiding misleading fractional request counts.
- **0.3 device summary**: the dashboard now requests `GET /api/devices`
  after Cloudflare Access validation. An account-internal named **RPC Service
  Binding**, `RELAY_TELEMETRY → mavka-relay / RelayTelemetry`, returns only
  aggregate D1 counts: mobile active, pending, revoked, recently seen in the
  last 30 minutes, and total. A recent D1 sighting does not guarantee the
  handset is online. The dashboard does NOT bind the private Relay D1 database
  and receives no device IDs, keys, phone numbers, names or messages. All RPC
  calls fail closed and display `not_connected` instead of false zeros.
- Deployment order: **first** deploy the updated `mavka-relay` with named
  `RelayTelemetry` entrypoint; **then** deploy `mavka-server-control` with
  its new service binding. Both reuse current Cloudflare bindings, secrets,
  Access and GitHub build pipelines; no D1 migration is required for counts.
- **0.4 security journal:** authenticated `GET /api/security` reads only
  sanitized aggregate counts via `RELAY_TELEMETRY.getSecurityOverview()`
  (same existing named private RPC). Data includes observed rejected requests
  over the last 24 hours and 7 days, top allowlisted reasons, UA/non-UA/
  unknown counts, and hourly UTC summaries. No new Control secrets, no
  additional Analytics token permissions, and NO direct Relay D1 binding.
- The **optional** Relay migration `relay/migrations/0003_security_rollups.sql`
  must be applied manually to `mavka-messages` **only after the owner
  verifies a D1 Time Travel bookmark or database export**. Deployment of
  the Worker does not migrate D1. The SQL only adds one table plus index.
  Until this migration is applied, security metrics show
  `not_connected`, never fabricated zero.
- The rollup is **best effort**, not an exact forensic log. Per-isolate
  coalescing flushes on first event, after 32 events, or on a later event
  after 60 seconds. Unsaved entries may be lost on isolate eviction.
  The authoritative source for individual attempts remains Workers Logs.
  The retention cleanup is 30 days; R2 and all encryption remain unchanged.
- `MAVKA_RELAY_SECURITY_V1` rejection logs still live in Cloudflare
  Observability. We do not authorize the monitoring panel to read raw logs or
  add a new Observability Write permission. A separate sanitized, strictly
  bounded channel will be needed to show log reasons in the panel.
- **CSV export**: optional same-browser export of real aggregate samples
  covering 24 h, 7 d, or 30 d. The app pages D1 with an existing authenticated
  read-only endpoint, caps downloads at 65 pages (<= 3250 rows), adds a UTF-8
  BOM and semicolon delimiter for Excel compatibility. No files, keys, IPs,
  Relay device IDs or message contents are ever queried or exported.
- A dedicated Cloudflare Workers Observability query API is available, but
  its documented API permission includes `Workers Observability Write`;
  intentionally **do not** expand the existing read-only Analytics token to
  grant log-query access. Security/device integration stays `not_connected`.


## Version 0.6 — automatic cleanup health monitoring

- A new Cloudflare Cron Trigger on `mavka-server-control`, `17 3 * * *`
  (03:17 UTC daily), runs ONE indexed DELETE to prune `usage_snapshots`
  older than 30 days, even when the admin UI is closed.
  The former DELETE on EVERY stats view was removed to save D1 queries.
  No additional D1 schema migration is needed for the Control database.
- The existing `mavka-relay` cron **continues** its 5-minute TTL
  cleanup of messages, nonces, request windows and receipts, plus hourly
  30-day pruning of security rollups. It now writes ONE anonymous
  hourly maintenance checkpoint to a singleton D1 row. It uses D1's
  existing DELETE `meta.changes` values (no extra expiry COUNT scans).
- To enable the checkpoint, after saving a D1 Time Travel bookmark or
  SQL backup, manually apply `relay/migrations/0004_maintenance_status.sql`
  ONLY to `mavka-messages`. The new table does not alter messages,
  devices or security history. Without the optional migration the existing
  TTL cleanup continues unaffected and the dashboard shows `not_connected`.
- The authenticated `GET /api/maintenance` uses the existing private named
  `RelayTelemetry.getMaintenanceStatus()` RPC, with strict whitelist and
  a 5-minute isolate cache. Stale snapshots are highlighted after 90 minutes.
  Values are **rows deleted in the last sampled hourly cleanup only**,
  NOT hourly totals and NOT the current expired backlog. Cloudflare Cron
  Events remain authoritative for each run.
- Approximate added cost: **1 small D1 UPSERT per hour in Relay** (24/day,
  <=744 per 31-day month), **1 Control D1 DELETE and one Control Worker
  scheduled invocation daily**. No per-request security logs, extra GraphQL
  queries, scanning of message contents, new API tokens or R2 objects.
  Existing read-only/admin access remains required for every HTTP API.
- R2 has a separate **seven-day lifecycle only under `transfers/`**.
  Do NOT delete undelivered documents or revoked-device denylist records
  as a blanket storage-pressure response.

## Version 0.5 — conservative monitoring within free-tier quotas
- **No new database/table/token, no external script, no new cron and no
  extra GraphQL queries.** Uses existing cached summaries and private
  5-minute Relay security RPC. The existing dashboard refreshes every 15
  minutes while visible; alerts are **on refresh**, not independent push/SMS/email.
- **Security incident signals:** derive up to 14 days of daily canonical
  reason counts from retained `relay_security_rollups` (30 days).
  A warning is NOT proof of an attack. Every signal is anonymous, approximate
  and should be reviewed in Cloudflare Observability. Alerts are not stored
  again in a separate incidents table and therefore add **zero D1 writes**.
- **Quota warnings:** 70%, 85% and 95% monitoring thresholds calculated
  locally from already-fetched Workers-only, D1-account and R2-bucket
  readings. The owner-defined R2 threshold is **5 GB of current size**:
  NOT an upload block, a paid quota or a GB-month bill. R2 operation
  Class A/B billing remains unknown and is not fabricated.
- **Cleanup:** `mavka-control.usage_snapshots` retains 30 days, pruned on
  new snapshots; Relay's 30-day aggregated security journal is pruned
  **hourly** by its existing every-five-minute cron; messages and nonces
  retain their existing fast TTL cleanup. R2 `transfers/` has an existing
  seven-day temporary-object lifecycle. Do NOT delete undelivered payloads
  or security-critical revoked device records to free storage.
- **Failure behavior:** missing analytics or security RPC means unknown,
  not zero; alerts never revoke devices or block traffic. Cloudflare
  Billing and Observability remain sources of truth for costs and raw logs.

## Security (configure BEFORE using the console)

Use **Cloudflare Access on this Worker only**, not on `mavka-relay`.
Cloudflare Workers > `mavka-server-control` > Access >
**Protect this Worker behind Access** > protect production traffic >
allow only the designated administrator email. Do not use
"Protect all Workers", which would block Android / PC native Relay traffic.

The Worker uses the **Cloudflare-authenticated platform context**
`ctx.access` for Worker-level Access, checking the **exact configured AUD**
and `ADMIN_EMAIL`. With no platform context, it can accept only a valid
`Cf-Access-Jwt-Assertion` verified by RS256 signature, issuer, application
audience, expiry and administrator email (for compatible Access integrations).
Neither raw request headers nor user-supplied identities are trusted.
Without configuration it returns HTTP 503; without a verified Access identity
it returns HTTP 403. No bypass is provided for debug or health routes.
**Cloudflare Access must also be enabled at the edge** to provide its login UI.

Runtime variables/secrets (Workers > Settings > Variables and Secrets):

The generated Wrangler config uses `keep_vars: true` so runtime variables
entered in the Cloudflare dashboard are retained on subsequent GitHub builds.
Never put API tokens in GitHub/build environment variables or in Wrangler
`vars`. The API token must be configured as a **runtime secret**.

| Name | Type | Source |
| --- | --- | --- |
| `ACCESS_TEAM_DOMAIN` | text | `https://your-team.cloudflareaccess.com` |
| `ACCESS_AUD` | text | Application Audience tag from the specific Access app |
| `ADMIN_EMAIL` | secret | Exact administrator login email (never commit) |
| `CF_ACCOUNT_ID` | text | Cloudflare account ID |
| `CF_ANALYTICS_TOKEN` | **secret** | Restricted token with read-only Analytics GraphQL access |

The API token stays **on the Worker** and never appears in the HTML, API
response or GitHub. Start with minimum account Analytics:Read permission;
confirm extra dataset permissions in Cloudflare if partial results appear.
Never supply an account-wide Edit or Administrator token.

## Create the separate control database

In Cloudflare Dashboard:

1. **Storage & databases → D1 SQL Database → Create database**.
2. Name: `mavka-control` (do not select `mavka-messages`).
3. Open `mavka-control → Studio / Console`. Run statements from
   [migrations/0001_control.sql](migrations/0001_control.sql) **only against
   `mavka-control`**.
4. Note the new D1 Database ID. Use it **only** for the control Worker binding.
   The `CONTROL_DB` binding is generated by `tools/configure.mjs`.
5. The SQL contains one table, `usage_snapshots`, with no personal identifiers.
   Cleanup of samples older than 30 days happens only when a new successful
   statistics sample is inserted.

**No migration of `mavka-messages` is required for this application**.
This setup does not change PR #38, which still awaits its own migration.

## Connect GitHub and deploy without software on the PC

The source lives in `arudichik-source/Mavka-Android/control`.
Use a **separate** Cloudflare Worker linked to the same GitHub repository,
with **Root directory `control`**. Do not change the existing
`mavka-relay` Build root (`relay`).

- Build-time environment variable:
  `MAVKA_CONTROL_D1_DATABASE_ID` = the **new** D1 UUID.
- Build command: `npm run configure`.
- Deploy command: `npx wrangler deploy`.
- The Worker project has pinned Wrangler under `devDependencies`.
- Before enabling the URL publicly, configure Access for **only this Worker**
  and fill the runtime variables above.
- GitHub CI is a **dry-run only**; no automatic Cloudflare deployment is
  initiated by opening or merging a PR. Production setup is an explicit
  Cloudflare dashboard action.

The generated `control/wrangler.json` is not checked in and must never
contain secrets. If the database ID is missing, configuration fails.
Cloudflare Access protects the Worker across all its associated URLs;
our second JWT check additionally blocks unauthorized direct requests.

## Data collection / free-tier precautions

- UI refresh timer: once per **15 minutes**, only while page is visible.
- Isolate cache: **15 minutes**. Multiple isolates/PoPs can independently
  use the GraphQL API; there is no global request quota guarantee.
- Four read-only GraphQL datasets (Workers, D1 rows, R2 storage, R2 ops).
- Snapshot: `INSERT OR IGNORE` one 15-minute bucket, with 30-day pruning.
- No background scheduler, no R2 listing or copies of file content.
- The D1 database uses small storage; its row operations still count against
  **account-wide free-tier limits**. Dashboard availability and metrics
  retention follow Cloudflare's current plan restrictions.
- Historical rows only exist for periods in which someone used the panel.
- Partial analytics results must not be interpreted as zero usage.
- Quota percentages are **guides**, not hard spending limits.
- The real R2 invoice, Class A/B usage and any overage must be verified in
  **Cloudflare Billing**.

## Development and tests

```bash
cd control
npm install --ignore-scripts
npm run check
npm run check-bundle
```

GitHub Actions runs the tests and a Wrangler `deploy --dry-run` with a
**synthetic** D1 ID; it never accesses a production account or stores
credentials. Run locally only in an environment where Node is available:
**the user's Windows PC does not need Node.js**.
