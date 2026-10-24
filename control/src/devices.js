// The only allowed connection to Relay is a named, private Cloudflare RPC.
// Never bind mavka-messages or expose raw device/identity rows in this Worker.
const keys = ['total','mainActive','mobileActive','mobilePending',
  'mobileRevoked','mobileSeenRecently','mobileNotSeenRecently'];
let cached = null, cachedAt = 0, pending = null;
const unavailable = reason => ({ state: 'not_connected', reason, data: null });
export async function devicesSummary(env, now = Date.now()) {
  if (typeof env.RELAY_TELEMETRY?.getDeviceCounts !== 'function')
    return unavailable('service_binding_not_configured');
  if (cached && now - cachedAt < 300000) return cached;
  if (pending) return pending;
  pending = (async () => {
    try {
      const data = await env.RELAY_TELEMETRY.getDeviceCounts();
      if (data?.state !== 'ready' || !keys.every(k => Number.isSafeInteger(data[k]) && data[k]>=0)
        || !Number.isSafeInteger(data.observedAt)
        || data.mobileSeenRecently>data.mobileActive ||
        data.mobileNotSeenRecently !== data.mobileActive-data.mobileSeenRecently)
        return unavailable('telemetry_unavailable');
      const sanitized = Object.fromEntries(keys.map(k=>[k,data[k]]));
      const result = {
        state: 'ready', source: 'internal_rpc', data: sanitized,
        observedAt: data.observedAt, activityWindowMinutes: 30,
        detail: 'Aggregate counts only; no personal data or messages',
      };
      cached = result;cachedAt = Date.now();
      return result;
    } catch {
      return unavailable('telemetry_unavailable');
    } finally { pending = null; }
  })();
  return pending;
}
