import { verifyAccess } from './access.js';
import { readAnalytics } from './analytics.js';
import { devicesSummary } from './devices.js';
import { securitySummary } from './security.js';
import { assessQuotaWarnings, RETENTION_INFO } from './alerts.js';
import { maintenanceSummary, pruneControlHistory } from './maintenance.js';
import { parsePeriod, parseBefore, readHistoryPage, readTrends } from './history.js';
import { html } from './ui.js';

const headers = {
  'Cache-Control': 'no-store, private',
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'no-referrer',
  'X-Frame-Options': 'DENY',
  'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
};
const json = (data, status = 200) => new Response(JSON.stringify(data), {
  status, headers: { ...headers, 'Content-Type': 'application/json; charset=utf-8' },
});
let recent = null;
let recentAt = 0;
let pending = null;

async function metrics(env) {
  // Per-isolate lightweight cache, bounded to fifteen minutes; no polling
  // is scheduled when the page is closed. No permanent background process.
  const now = Date.now();
  // Keep successful readings for 15 minutes, but retry temporary API
  // failures after one minute instead of caching a blank dashboard for 15.
  if (recent && now - recentAt < (recent.state === 'ready' ? 900000 : 60000))
    return recent;
  if (pending) return pending;
  const task = readAnalytics(env).then(data => {
    recent = data; recentAt = Date.now();
    return data;
  }).finally(() => { pending = null; });
  pending = task;
  return task;
}
function storeSnapshot(db, data) {
  if (!db?.prepare || !['ready', 'partial'].includes(data.state)) return Promise.resolve();
  const bucket = Math.floor(Date.parse(data.at) / 900000) * 900;
  // INSERT OR IGNORE stops duplicate samples across isolates and users.
  const insertion = db.prepare(`INSERT OR IGNORE INTO usage_snapshots
    (bucket_start, workers_requests, workers_errors, d1_rows_read, d1_rows_written,
      r2_payload_bytes, r2_objects)
    VALUES (?, ?, ?, ?, ?, ?, ?)`).bind(bucket,
      data.workers?.requestsToday ?? null, data.workers?.errorsToday ?? null,
      data.d1?.rowsReadToday ?? null, data.d1?.rowsWrittenToday ?? null,
      data.r2?.storage?.bytes ?? null, data.r2?.storage?.objects ?? null);
  // One insert per unique 15-minute bucket. History pruning now runs once
  // daily, even when nobody opens the dashboard. Avoid a DELETE per view.
  return insertion.run();
}
export default {
  async fetch(request, env, ctx) {
    // Deny unauthenticated access to HTML, JSON, robots, and unknown routes.
    const who = await verifyAccess(request, env, ctx);
    if (who.error) return json({ error: who.error }, who.status);
    const url = new URL(request.url);
    if (url.protocol !== 'https:') return json({ error: 'https_required' }, 426);
    if (request.method !== 'GET') return json({ error: 'method_not_allowed' }, 405);
    if ((url.pathname === '/' || url.pathname === '/index.html') && !url.search) {
      const nonce = crypto.randomUUID().replaceAll('-', '');
      const policy = `default-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'; connect-src 'self'; img-src 'none'; script-src 'nonce-${nonce}'; style-src 'nonce-${nonce}'`;
      return new Response(html.replaceAll('__CSP_NONCE__', nonce), {
        headers: { ...headers, 'Content-Type': 'text/html; charset=utf-8',
          'Content-Security-Policy': policy },
      });
    }
    if (url.pathname === '/api/summary' && !url.search) {
      try {
        const data = await metrics(env);
        if (env.CONTROL_DB?.prepare && data.state !== 'unconfigured' &&
            data.state !== 'unavailable') {
          // Complete the tiny D1 snapshot before sending the summary; the UI
          // can then request history without racing a waitUntil background write.
          // History failure never prevents an authorized read-only dashboard.
          await storeSnapshot(env.CONTROL_DB, data).catch(() => {});
        }
        return json({ ...data, quotaAlerts: assessQuotaWarnings(data) });
      } catch { return json({ state: 'unavailable', error: 'analytics_unavailable' }, 503); }
    }
    if (url.pathname === '/api/history' || url.pathname === '/api/trends') {
      const allowed = url.pathname === '/api/history' ? ['period', 'before'] : ['period'];
      const names = [...url.searchParams.keys()];
      if (names.some(k => !allowed.includes(k)) ||
          new Set(names).size !== names.length || names.length > allowed.length)
        return json({ error: 'invalid_query' }, 400);
      const period = parsePeriod(url.searchParams.get('period') ?? '24h');
      const before = parseBefore(url.searchParams.get('before'));
      if (!period || before === undefined) return json({ error: 'invalid_query' }, 400);
      if (url.pathname === '/api/trends') return json(await readTrends(env.CONTROL_DB, period));
      return json(await readHistoryPage(env.CONTROL_DB, period, before));
    }
    if (url.pathname === '/api/devices' && !url.search) return json(await devicesSummary(env));
    if (url.pathname === '/api/maintenance' && !url.search) return json(await maintenanceSummary(env));
    if (url.pathname === '/api/security') {
      if (url.search) return json({ error: 'invalid_query' }, 400);
      // Private named Relay RPC returns bounded aggregate counters only.
      // No direct binding to private mavka-messages or raw Cloudflare logs.
      return json(await securitySummary(env));
    }
    if (url.pathname === '/api/about' && !url.search) return json({
      application: 'MaVka Server Control', version: '0.6.0',
      mode: 'read_only', refreshSeconds: 900,
      storage: env.CONTROL_DB?.prepare ? 'bound' : 'not_bound',
      analyticsConfigured: Boolean(env.CF_ACCOUNT_ID && env.CF_ANALYTICS_TOKEN),
      retention: RETENTION_INFO,
      alertMode: 'on_dashboard_refresh_no_background_notifier',
    });
    return json({ error: 'not_found' }, 404);
  },
  async scheduled(_event, env, ctx) {
    // One daily UTC DELETE and at most one privacy-safe status log per day.
    // Never log SQL errors, device identity, security logs or message data.
    ctx.waitUntil((async () => {
      const result = await pruneControlHistory(env.CONTROL_DB);
      if (result.state !== 'ready') {
        console.warn(JSON.stringify({
          event: 'MAVKA_CONTROL_RETENTION_V1',
          outcome: 'failed',
          reason: result.state === 'not_bound' ? 'db_not_bound' : 'cleanup_unavailable',
        }));
        throw new Error('control_daily_retention_failed');
      }
      console.info(JSON.stringify({
        event: 'MAVKA_CONTROL_RETENTION_V1',
        outcome: 'ok',
        rowsDeleted: result.deleted,
      }));
    })());
  },
};
