// Bounded reads of the existing, separate mavka-control D1 table.
// No direct access to mavka-messages or to private Relay device records.
// All stored values are anonymous resource counters.
export const PERIODS = Object.freeze({ '24h': 86400, '7d': 7 * 86400, '30d': 30 * 86400 });
const FIELDS = `bucket_start, workers_requests, workers_errors,
  d1_rows_read, d1_rows_written, r2_payload_bytes, r2_objects`;
const PAGE_SIZE = 50;

export function parsePeriod(value) {
  return Object.hasOwn(PERIODS, value) ? value : null;
}
export function parseBefore(value) {
  if (value === null) return null;
  if (typeof value !== 'string' || !/^[1-9][0-9]{8,11}$/.test(value)) return undefined;
  const number = Number(value);
  return Number.isSafeInteger(number) ? number : undefined;
}
function unavailable(state, period) {
  return { state, period, entries: [], nextBefore: null };
}
const nowSeconds = now => Math.floor(now.getTime() / 1000);

export async function readHistoryPage(db, period = '24h', before = null, now = new Date()) {
  if (!parsePeriod(period) || before === undefined) return unavailable('invalid_query', period);
  if (!db?.prepare) return unavailable('not_bound', period);
  try {
    const end = nowSeconds(now);
    const begin = end - PERIODS[period];
    const cursor = before === null ? end + 1 : Math.min(before, end + 1);
    const stmt = db.prepare(`SELECT ${FIELDS} FROM usage_snapshots
      WHERE bucket_start >= ? AND bucket_start < ?
      ORDER BY bucket_start DESC LIMIT ?`).bind(begin, cursor, PAGE_SIZE + 1);
    const rows = (await stmt.all()).results ?? [];
    const entries = rows.slice(0, PAGE_SIZE);
    return { state: 'ready', period, entries, nextBefore:
      rows.length > PAGE_SIZE ? entries.at(-1).bucket_start : null,
      hasMore: rows.length > PAGE_SIZE };
  } catch {
    return unavailable('database_unavailable', period);
  }
}
export async function readTrends(db, period = '24h', now = new Date()) {
  if (!parsePeriod(period)) return unavailable('invalid_query', period);
  if (!db?.prepare) return unavailable('not_bound', period);
  try {
    const end = nowSeconds(now);
    const begin = end - PERIODS[period];
    // Latest real sample per 15-minute/hour/day bucket. Never interpolate
    // absent samples or convert missing values into zero.
    const interval = period === '24h' ? 900 : period === '7d' ? 3600 : 86400;
    const stmt = db.prepare(`SELECT ${FIELDS} FROM usage_snapshots
      WHERE bucket_start IN (
        SELECT MAX(bucket_start) FROM usage_snapshots
        WHERE bucket_start >= ? AND bucket_start <= ?
        GROUP BY CAST(bucket_start / ? AS INTEGER)
      ) ORDER BY bucket_start ASC LIMIT 240`).bind(begin, end, interval);
    const entries = (await stmt.all()).results ?? [];
    return { state: 'ready', period, intervalSeconds: interval,
      entries, coverage: entries.length, from: begin, to: end,
      retentionDays: 30,
      sampledOnlyOnDashboardViews: true,
      dailyCountersResetAtUtcMidnight: true };
  } catch {
    return unavailable('database_unavailable', period);
  }
}
