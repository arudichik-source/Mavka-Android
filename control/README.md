# MaVka Server Control 0.2 — separate, read-only Cloudflare console

The initial 0.1 application was deployed successfully to a separate Cloudflare Worker.\nVersion 0.2 adds real historical graphs and improved read-only reporting;\nverify current deployment status in the Cloudflare dashboard.
It is isolated from `mavka-relay` and its `mavka-messages` database. It
does **not** modify, proxy or call the MaVka message API.

- Independent Worker: `mavka-server-control`.
- Independent D1: `mavka-control` bound as `CONTROL_DB`.
- Ukrainian responsive dashboard with Worker, D1 and R2 Cloudflare GraphQL
  telemetry, 15-minute browser/isolate refresh, quota guides and compact
  30-day snapshots (at most one per quarter hour).
- **Read-only** API: `GET /`, `GET /api/summary`,
  `GET /api/history`, `GET /api/trends`, `GET /api/security`, `GET /api/about`.
- No cron, no automatic polling after the tab closes, no relay device access,
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
