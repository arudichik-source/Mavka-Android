// Private D1 aggregation performed inside the Relay Worker only.
// Returns exclusively aggregate integers, never device IDs, keys, or message data.
// Invoked exclusively through the named Cloudflare service RPC entrypoint.
const int = x => Number.isSafeInteger(x) && x >= 0 ? x : 0;
export async function readDeviceTelemetry(db, now = Math.floor(Date.now()/1000)) {
  if (!db?.prepare) return { state: 'unavailable', reason: 'db_not_bound' };
  try {
    const result = await db.prepare(`SELECT
      COUNT(*) AS total,
      SUM(CASE WHEN device_type = 'main' AND status = 'active' THEN 1 ELSE 0 END) AS main_active,
      SUM(CASE WHEN device_type = 'mobile' AND status = 'active' THEN 1 ELSE 0 END) AS mobile_active,
      SUM(CASE WHEN device_type = 'mobile' AND status = 'pending' THEN 1 ELSE 0 END) AS mobile_pending,
      SUM(CASE WHEN device_type = 'mobile' AND status = 'revoked' THEN 1 ELSE 0 END) AS mobile_revoked,
      SUM(CASE WHEN device_type = 'mobile' AND status = 'active'
        AND last_seen_at >= ? THEN 1 ELSE 0 END) AS mobile_seen_recently
      FROM devices`).bind(now - 1800).first();
    if (!result) return { state: 'unavailable', reason: 'database_unavailable' };
    const total = int(result.total);
    const mobileActive = int(result.mobile_active);
    const mobileSeenRecently = Math.min(int(result.mobile_seen_recently),mobileActive);
    return {
      state: 'ready', observedAt: now, activityWindowMinutes: 30,
      total, mainActive: int(result.main_active),
      mobileActive, mobilePending: int(result.mobile_pending),
      mobileRevoked: int(result.mobile_revoked),
      mobileSeenRecently,
      mobileNotSeenRecently: mobileActive - mobileSeenRecently,
    };
  } catch {
    return { state: 'unavailable', reason: 'database_unavailable' };
  }
}
