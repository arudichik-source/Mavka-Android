// Best-effort, privacy-preserving 15-minute security aggregate journal.
// Detailed rejection logs already exist in Cloudflare Workers Observability.
// Do NOT write attacker IP, user-agent, raw path/query, device ID, nonce, keys,
// signatures, request bodies, message contents or arbitrary exception text.
//
// Deliberately coalesced by isolate to avoid an extra D1 write on every hostile
// request. Not an exact count: isolated workers may be evicted before flushing.
// Each flush is bounded and non-blocking. Failure NEVER affects Relay response.
const BUCKET = 900, RETENTION_SECONDS = 30 * 86400, FLUSH_MS = 60000;
const MAX_BATCH_EVENTS = 32, MAX_GROUPS = 32;
const REASONS = new Set([
  'auth_headers_invalid','auth_timestamp_invalid','auth_device_unavailable',
  'auth_main_binding_invalid','auth_public_key_invalid','auth_signature_invalid',
  'unauthorized','country_not_allowed','native_clients_only','https_required',
  'not_found','rate_limited','request_replayed','main_required','mobile_required',
  'device_unavailable','device_not_found','device_conflict','cannot_revoke_main',
  'route_forbidden','message_not_found','invalid_expiry','device_capacity',
  'queue_full','recipient_unavailable','body_too_large','json_required',
  'encoding_not_supported','body_required','message_expired','invalid_request',
  'invalid_base64url','request_rejected','service_unavailable',
]);
const CATEGORIES = new Set(['authentication','perimeter','authorization','replay',
  'rate_limit','route_or_resource','service','invalid_request']);
const ROUTES = new Set(['health','status','bootstrap','activation','devices',
  'enrollment','inbox','send','revocation','message_ack','files_status',
  'files','other']);
const COUNTRIES = new Set(['UA','non_UA','unknown']);
const PRINCIPALS = new Set(['verified_main','verified_mobile','unverified']);
const validStatus = n => Number.isInteger(n) && n >= 400 && n <= 599;
const integer = n => Number.isSafeInteger(n) && n >= 0 ? n : 0;
const SIGNAL_REASONS = new Set(['auth_signature_invalid','request_replayed',
  'auth_main_binding_invalid','country_not_allowed','rate_limited','other']);
// Security retention runs only hourly although Relay message cleanup stays
// every five minutes. A no-op deletion every five minutes wastes D1 queries.
export const shouldPruneSecurityRollups = scheduledTime =>
  Number.isFinite(scheduledTime) && Math.floor(scheduledTime/60000)%60===0;
const buf = new Map();
let eventsInBuffer = 0, lastFlushMs = 0, flushing = false;

function safe(event, timestamp) {
  if (!event || event.event !== 'MAVKA_RELAY_SECURITY_V1' ||
      event.outcome !== 'rejected' || !validStatus(event.status)) return null;
  return {
    bucket: Math.floor(timestamp / BUCKET) * BUCKET,
    category: CATEGORIES.has(event.category) ? event.category : 'invalid_request',
    reason: REASONS.has(event.reason) ? event.reason : 'request_rejected',
    country: COUNTRIES.has(event.countryScope) ? event.countryScope : 'unknown',
    route: ROUTES.has(event.route) ? event.route : 'other',
    status: event.status,
    principal: PRINCIPALS.has(event.principal) ? event.principal : 'unverified',
  };
}
const keyOf = x => JSON.stringify([x.bucket,x.category,x.reason,x.country,
  x.route,x.status,x.principal]);

export async function flushSecurityRollups(db, groups) {
  if (!db?.prepare || !groups?.length) return false;
  try {
    const statements = groups.slice(0,MAX_GROUPS).map(x =>
      db.prepare(`INSERT INTO relay_security_rollups
        (bucket_start,category,reason,country_scope,route,status,principal_scope,events)
        VALUES (?,?,?,?,?,?,?,?)
        ON CONFLICT(bucket_start,category,reason,country_scope,route,status,principal_scope)
        DO UPDATE SET events = events + excluded.events`)
        .bind(x.bucket,x.category,x.reason,x.country,x.route,x.status,
          x.principal,x.events));
    await db.batch(statements);
    return true;
  } catch { return false; }
}

export function queueSecurityRollup(event, env, ctx, now = Date.now()) {
  if (!env?.DB?.prepare || typeof ctx?.waitUntil !== 'function') return;
  const data = safe(event,Math.floor(now / 1000));
  if (!data) return;
  let key = keyOf(data);
  if (!buf.has(key) && buf.size >= MAX_GROUPS) {
    // Fixed overflow classification; never grow unbounded attacker-controlled
    // categories or create a key per ID/path.
    data.category = 'invalid_request';
    data.reason = 'request_rejected';
    data.route = 'other';
    data.country = 'unknown';
    data.principal = 'unverified';
    key = keyOf(data);
  }
  const previous = buf.get(key);
  if (previous) previous.events += 1;
  else buf.set(key,{...data,events:1});
  eventsInBuffer += 1;
  if (flushing || (lastFlushMs !== 0 &&
    now-lastFlushMs < FLUSH_MS && eventsInBuffer < MAX_BATCH_EVENTS)) return;
  const entries = [...buf.values()];
  buf.clear();eventsInBuffer=0;flushing=true;lastFlushMs=now;
  // RPC/SQL failures are swallowed by flushSecurityRollups. Do not report any
  // detailed event again to the public user in case of storage failure.
  ctx.waitUntil(flushSecurityRollups(env.DB,entries).finally(()=>{flushing=false}));
}

const unavailable = reason => ({state:'not_connected', reason, source:'relay_aggregate_d1',
  coverage:'best_effort', periodHours:24, total24h:null, total7d:null,
  byReason:[], byCountry:[], byHour:[], signalDays:[]});
function sanitizeRows(rows, max, mapper) {
  return (rows||[]).slice(0,max).map(mapper).filter(Boolean);
}
export async function readSecurityRollups(db, now = Math.floor(Date.now()/1000)) {
  if (!db?.prepare) return unavailable('db_not_bound');
  try {
    const start24 = now - 86400, start7 = now - 7*86400;
    const [totals,reasonRows,countries,hours,days] = await db.batch([
      db.prepare(`SELECT COALESCE(SUM(CASE WHEN bucket_start>=? THEN events ELSE 0 END),0) AS events_24h,
        COALESCE(SUM(events),0) AS events_7d
        FROM relay_security_rollups WHERE bucket_start>=?`).bind(start24,start7),
      db.prepare(`SELECT category,reason,SUM(events) AS count
        FROM relay_security_rollups WHERE bucket_start>=?
        GROUP BY category,reason ORDER BY count DESC LIMIT 12`).bind(start24),
      db.prepare(`SELECT country_scope AS scope,SUM(events) AS count
        FROM relay_security_rollups WHERE bucket_start>=?
        GROUP BY country_scope ORDER BY count DESC LIMIT 3`).bind(start24),
      db.prepare(`SELECT CAST(bucket_start/3600 AS INTEGER)*3600 AS hour_start,
        SUM(events) AS count FROM relay_security_rollups WHERE bucket_start>=?
        GROUP BY CAST(bucket_start/3600 AS INTEGER) ORDER BY hour_start ASC LIMIT 25`).bind(start24),
      // Only six coarse reasons for 14 days, at most 90 groups. This is
      // computed on existing 30-day rollups, no extra D1 writes or incident DB.
      db.prepare(`SELECT CAST(bucket_start/86400 AS INTEGER)*86400 AS day_start,
        CASE WHEN reason IN ('auth_signature_invalid','request_replayed',
          'auth_main_binding_invalid','country_not_allowed','rate_limited')
          THEN reason ELSE 'other' END AS signal_reason,
        SUM(events) AS count
        FROM relay_security_rollups WHERE bucket_start >= ?
        GROUP BY CAST(bucket_start/86400 AS INTEGER), signal_reason
        ORDER BY day_start DESC, signal_reason ASC LIMIT 90`).bind(now - 14*86400),
    ]);
    const count24 = integer(totals.results?.[0]?.events_24h);
    const count7 = integer(totals.results?.[0]?.events_7d);
    return {
      state:'ready', source:'relay_aggregate_d1', observedAt:now,
      coverage:'best_effort', periodHours:24, retentionDays:30,
      note:'Rollups are opportunistic, batched per Worker isolate, and may undercount. Raw Cloudflare Workers Logs are authoritative for individual requests.',
      total24h:count24, total7d:count7,
      byReason:sanitizeRows(reasonRows.results,12,row=>
        CATEGORIES.has(row.category)&&REASONS.has(row.reason)
          ? {category:row.category,reason:row.reason,count:integer(row.count)}:null),
      byCountry:sanitizeRows(countries.results,3,row=>
        COUNTRIES.has(row.scope)?{scope:row.scope,count:integer(row.count)}:null),
      byHour:sanitizeRows(hours.results,25,row=>
        Number.isSafeInteger(row.hour_start)&&row.hour_start>=0
          ? {hourStart:row.hour_start,count:integer(row.count)}:null),
      // 14-day anonymous incident *signals*, not full forensic history.
      // Includes no route, principal, IP, device, headers or payload fields.
      signalDays:sanitizeRows(days.results,90,row=>
        Number.isSafeInteger(row.day_start)&&row.day_start>=0&&
        SIGNAL_REASONS.has(row.signal_reason)
          ? {dayStart:row.day_start,reason:row.signal_reason,count:integer(row.count)}:null),
    };
  } catch { return unavailable('migration_or_database_unavailable'); }
}
export async function pruneSecurityRollups(db, now = Math.floor(Date.now()/1000)) {
  if (!db?.prepare) return;
  try {
    await db.prepare('DELETE FROM relay_security_rollups WHERE bucket_start < ?')
      .bind(now - RETENTION_SECONDS).run();
  } catch { /* Older installations may not yet have this optional table. */ }
}
