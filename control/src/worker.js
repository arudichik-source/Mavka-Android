import { verifyAccess } from './access.js';
import { readAnalytics } from './analytics.js';
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
  if (recent && now - recentAt < 900000) return recent;
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
  return db.prepare(`INSERT OR IGNORE INTO usage_snapshots
    (bucket_start, workers_requests, workers_errors, d1_rows_read, d1_rows_written,
      r2_payload_bytes, r2_objects)
    VALUES (?, ?, ?, ?, ?, ?, ?)`).bind(bucket,
      data.workers?.requestsToday ?? null, data.workers?.errorsToday ?? null,
      data.d1?.rowsReadToday ?? null, data.d1?.rowsWrittenToday ?? null,
      data.r2?.storage?.bytes ?? null, data.r2?.storage?.objects ?? null).run();
}
async function history(db) {
  if (!db?.prepare) return { state: 'not_bound', entries: [] };
  try {
    const results = await db.prepare(`SELECT bucket_start, workers_requests, workers_errors,
      d1_rows_read, d1_rows_written, r2_payload_bytes, r2_objects
      FROM usage_snapshots ORDER BY bucket_start DESC LIMIT 96`).all();
    return { state: 'ready', entries: results.results.reverse() };
  } catch {
    return { state: 'migration_required', entries: [] };
  }
}
export default {
  async fetch(request, env, ctx) {
    // Deny unauthenticated access to HTML, JSON, robots, and unknown routes.
    const who = await verifyAccess(request, env);
    if (who.error) return json({ error: who.error }, who.status);
    const url = new URL(request.url);
    if (url.protocol !== 'https:') return json({ error: 'https_required' }, 426);
    if (request.method !== 'GET' || url.search)
      return json({ error: 'not_found' }, 404);
    if (url.pathname === '/' || url.pathname === '/index.html') {
      const nonce = crypto.randomUUID().replaceAll('-', '');
      const policy = `default-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'; connect-src 'self'; img-src 'none'; script-src 'nonce-${nonce}'; style-src 'nonce-${nonce}'`;
      return new Response(html.replaceAll('__CSP_NONCE__', nonce), {
        headers: { ...headers, 'Content-Type': 'text/html; charset=utf-8',
          'Content-Security-Policy': policy },
      });
    }
    if (url.pathname === '/api/summary') {
      try {
        const data = await metrics(env);
        if (env.CONTROL_DB?.prepare && data.state !== 'unconfigured' &&
            data.state !== 'unavailable') ctx?.waitUntil?.(storeSnapshot(env.CONTROL_DB, data).catch(() => {}));
        return json(data);
      } catch { return json({ state: 'unavailable', error: 'analytics_unavailable' }, 503); }
    }
    if (url.pathname === '/api/history') return json(await history(env.CONTROL_DB));
    if (url.pathname === '/api/about') return json({
      application: 'MaVka Server Control', version: '0.1.0',
      mode: 'read_only', refreshSeconds: 900,
      storage: env.CONTROL_DB?.prepare ? 'bound' : 'not_bound',
      analyticsConfigured: Boolean(env.CF_ACCOUNT_ID && env.CF_ANALYTICS_TOKEN),
    });
    return json({ error: 'not_found' }, 404);
  },
};
