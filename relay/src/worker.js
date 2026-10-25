import { ApiError, VERSION, MAX_BODY, MAX_TTL, requireValue, object, id, publicKey,
  decodeUrl, messageBody, fingerprint, signingInput, encoder } from './protocol.js';
import { reportSecurityEvent } from './security-audit.js';
import { queueSecurityRollup, pruneSecurityRollups, shouldPruneSecurityRollups } from './security-rollups.js';
import { recordMaintenanceCheckpoint } from './maintenance.js';

const SKEW = 300;
const MAX_DEVICES = 500;
const MAX_QUEUE = 10000;
const MAX_DEVICE_QUEUE = 1000;
const RETENTION = 86400;
const HEADERS = { 'Content-Type': 'application/json; charset=utf-8',
  'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'no-referrer', 'Content-Security-Policy': "default-src 'none'; frame-ancestors 'none'" };
function json(value, status = 200) { return new Response(JSON.stringify(value), { status, headers: HEADERS }); }
const nowSeconds = () => Math.floor(Date.now() / 1000);
const stmt = (db, sql, ...args) => db.prepare(sql).bind(...args);
const alive = `EXISTS (SELECT 1 FROM devices WHERE device_id = ? AND status = 'active')`;

async function readBody(request) {
  if (request.method === 'GET') { requireValue(!request.body); return ''; }
  requireValue(request.headers.get('Content-Type')?.split(';')[0].trim().toLowerCase() === 'application/json', 415, 'json_required');
  requireValue(!request.headers.has('Content-Encoding'), 415, 'encoding_not_supported');
  requireValue(request.body, 400, 'body_required');
  const reader = request.body.getReader(); const chunks = []; let size = 0;
  try {
    for (;;) {
      const { value, done } = await reader.read(); if (done) break;
      size += value.byteLength;
      if (size > MAX_BODY) { await reader.cancel(); throw new ApiError(413, 'body_too_large'); }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const part of chunks) { bytes.set(part, offset); offset += part.byteLength; }
  try { return new TextDecoder('utf-8', { fatal: true }).decode(bytes); }
  catch { throw new ApiError(400, 'invalid_utf8'); }
}
function parse(raw) { try { return JSON.parse(raw); } catch { throw new ApiError(400, 'invalid_json'); } }
async function adminConfig(env) {
  try {
    id(env.RELAY_ADMIN_DEVICE_ID);
    const key = await publicKey(JSON.parse(env.RELAY_ADMIN_SIGNING_KEY));
    return { id: env.RELAY_ADMIN_DEVICE_ID, ...key };
  } catch { throw new ApiError(503, 'relay_not_configured'); }
}
function authReject(auditReason) {
  // Keep the public response deliberately generic; only the sanitized security
  // event can distinguish authentication failures for operators.
  const error = new ApiError(401, 'unauthorized');
  error.auditReason = auditReason;
  throw error;
}
async function authenticate(request, env, db, raw, bootstrap, activate, now) {
  const admin = await adminConfig(env);
  const deviceId = request.headers.get('X-Mavka-Device');
  const time = request.headers.get('X-Mavka-Time');
  const nonce = request.headers.get('X-Mavka-Nonce');
  const signature = request.headers.get('X-Mavka-Signature');
  let signatureBytes;
  try {
    id(deviceId); requireValue(/^\d{10}$/.test(time ?? ''));
    decodeUrl(nonce, 16); signatureBytes = decodeUrl(signature, 64);
  } catch { authReject('auth_headers_invalid'); }
  if (Math.abs(now - Number(time)) > SKEW) authReject('auth_timestamp_invalid');
  const row = await stmt(db, 'SELECT * FROM devices WHERE device_id = ?', deviceId).first();
  let signer;
  if (bootstrap) {
    if (deviceId !== admin.id) authReject('auth_main_binding_invalid');
    if (row) requireValue(row.status === 'active' && row.device_type === 'main' && row.public_signing_key === admin.json, 403, 'device_unavailable');
    signer = admin;
  } else {
    if (!(row && (row.status === 'active' || (activate && row.status === 'pending'))))
      authReject('auth_device_unavailable');
    if (row.device_type === 'main' && (deviceId !== admin.id || row.public_signing_key !== admin.json))
      authReject('auth_main_binding_invalid');
    try { signer = await publicKey(JSON.parse(row.public_signing_key)); }
    catch { authReject('auth_public_key_invalid'); }
  }
  const valid = await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, signer.key,
    signatureBytes, await signingInput(request.method, request.url, deviceId, time, nonce, raw));
  if (!valid) authReject('auth_signature_invalid');
  const window = now - now % 60;
  const rateSql = `INSERT INTO relay_rate_windows (device_id, window_start, requests) VALUES (?, ?, 1)
    ON CONFLICT(device_id, window_start) DO UPDATE SET requests = requests + 1 WHERE requests < ? RETURNING requests`;
  const rates = await db.batch([stmt(db, rateSql, '__global__', window, 3000),
    stmt(db, rateSql, deviceId, window, deviceId === admin.id ? 300 : 120)]);
  requireValue(rates.every(r => r.results.length === 1), 429, 'rate_limited');
  const consumed = await db.batch([
    stmt(db, 'INSERT OR IGNORE INTO relay_nonces (device_id, nonce, expires_at) VALUES (?, ?, ?)', deviceId, nonce, Number(time) + SKEW + 1),
    stmt(db, 'UPDATE devices SET last_seen_at = ? WHERE device_id = ? AND (last_seen_at IS NULL OR last_seen_at < ?)', now, deviceId, now - 300),
  ]);
  requireValue(consumed[0].meta.changes === 1, 409, 'request_replayed');
  return { deviceId, row, admin, proof: { senderDeviceId: deviceId, time, nonce, signature } };
}
function isMain(auth) {
  requireValue(auth.deviceId === auth.admin.id && auth.row?.device_type === 'main' && auth.row.status === 'active', 403, 'main_required');
}
function noQuery(url) { requireValue(url.search === ''); }
async function bootstrap(db, auth, body, now) {
  object(body, ['publicEncryptionKey']);
  const encryption = await publicKey(body.publicEncryptionKey, 'encryption');
  const inserted = await stmt(db, `INSERT OR IGNORE INTO devices
    (device_id, device_type, public_signing_key, public_encryption_key, status, created_at, last_seen_at)
    VALUES (?, 'main', ?, ?, 'active', ?, ?)`, auth.deviceId, auth.admin.json, encryption.json, now, now).run();
  const row = await stmt(db, 'SELECT * FROM devices WHERE device_id = ?', auth.deviceId).first();
  requireValue(row && row.device_type === 'main' && row.status === 'active' && row.public_signing_key === auth.admin.json &&
    row.public_encryption_key === encryption.json, 409, 'bootstrap_conflict');
  return json({ deviceId: auth.deviceId, status: 'active' }, inserted.meta.changes ? 201 : 200);
}
async function register(db, auth, body, now) {
  isMain(auth); object(body, ['deviceId', 'publicSigningKey', 'publicEncryptionKey']); id(body.deviceId);
  requireValue(body.deviceId !== auth.admin.id, 409, 'device_conflict');
  const signing = await publicKey(body.publicSigningKey);
  const encryption = await publicKey(body.publicEncryptionKey, 'encryption');
  const inserted = await stmt(db, `INSERT OR IGNORE INTO devices
    (device_id, device_type, public_signing_key, public_encryption_key, status, created_at)
    SELECT ?, 'mobile', ?, ?, 'pending', ? WHERE ${alive}
    AND (SELECT count(*) FROM devices) < ?`, body.deviceId, signing.json, encryption.json, now, auth.deviceId, MAX_DEVICES).run();
  const row = await stmt(db, 'SELECT * FROM devices WHERE device_id = ?', body.deviceId).first();
  requireValue(row, 429, 'device_capacity');
  requireValue(row.status !== 'revoked' && row.device_type === 'mobile' && row.public_signing_key === signing.json && row.public_encryption_key === encryption.json, 409, 'device_conflict');
  return json({ deviceId: row.device_id, status: row.status }, inserted.meta.changes ? 201 : 200);
}
async function activate(db, auth, body, now) {
  object(body, []); requireValue(auth.row?.device_type === 'mobile', 403, 'mobile_required');
  const result = await stmt(db, `UPDATE devices SET status = 'active', last_seen_at = ?
    WHERE device_id = ? AND status IN ('pending', 'active') RETURNING device_id`, now, auth.deviceId).all();
  requireValue(result.results.length === 1, 403, 'device_unavailable');
  return json({ deviceId: auth.deviceId, status: 'active' });
}
async function revoke(db, auth, deviceId, body, now) {
  isMain(auth); object(body, []); id(deviceId);
  requireValue(deviceId !== auth.admin.id, 403, 'cannot_revoke_main');
  const result = await stmt(db, `UPDATE devices SET status = 'revoked', revoked_at = COALESCE(revoked_at, ?)
    WHERE device_id = ? AND device_type = 'mobile' AND ${alive} RETURNING device_id`, now, deviceId, auth.deviceId).all();
  requireValue(result.results.length === 1, 404, 'device_not_found');
  return json({ deviceId, status: 'revoked' });
}
async function send(db, auth, raw, now) {
  const body = messageBody(parse(raw));
  requireValue(body.recipientDeviceId !== auth.deviceId);
  requireValue(body.expiresAt > now && body.expiresAt <= now + MAX_TTL, 400, 'invalid_expiry');
  // Main <-> mobile only: mobile devices cannot address other mobile devices.
  requireValue(auth.deviceId === auth.admin.id || body.recipientDeviceId === auth.admin.id, 403, 'route_forbidden');
  const payloadHash = await fingerprint(auth.deviceId, body);
  const stored = JSON.stringify({ format: 'MAVKA_RELAY_STORED_V1', ...auth.proof, requestBody: raw });
  const results = await db.batch([
    stmt(db, `INSERT OR IGNORE INTO relay_message_ids
      (message_id, sender_device_id, recipient_device_id, payload_sha256, message_expires_at, retain_until)
      SELECT ?, ?, ?, ?, ?, ? WHERE ${alive} AND ${alive}
      AND NOT EXISTS (SELECT 1 FROM messages WHERE message_id = ?)
      AND (SELECT count(*) FROM messages) < ?
      AND (SELECT count(*) FROM messages WHERE sender_device_id = ?) < ?
      AND (SELECT count(*) FROM messages WHERE recipient_device_id = ?) < ?`,
    body.messageId, auth.deviceId, body.recipientDeviceId, payloadHash, body.expiresAt, body.expiresAt + RETENTION,
    auth.deviceId, body.recipientDeviceId, body.messageId, MAX_QUEUE, auth.deviceId, MAX_DEVICE_QUEUE, body.recipientDeviceId, MAX_DEVICE_QUEUE),
    stmt(db, `INSERT OR IGNORE INTO messages
      (message_id, sender_device_id, recipient_device_id, encrypted_payload, created_at, expires_at)
      SELECT ?, ?, ?, ?, ?, ? WHERE EXISTS (SELECT 1 FROM relay_message_ids WHERE message_id = ?
      AND sender_device_id = ? AND recipient_device_id = ? AND payload_sha256 = ? AND acked_at IS NULL)
      AND ${alive} AND ${alive}`, body.messageId, auth.deviceId, body.recipientDeviceId, stored, now, body.expiresAt,
    body.messageId, auth.deviceId, body.recipientDeviceId, payloadHash, auth.deviceId, body.recipientDeviceId),
    stmt(db, 'SELECT * FROM relay_message_ids WHERE message_id = ?', body.messageId),
    stmt(db, 'SELECT message_id FROM messages WHERE message_id = ?', body.messageId),
  ]);
  const receipt = results[2].results[0];
  requireValue(receipt, 409, 'recipient_unavailable_or_queue_full');
  requireValue(receipt.sender_device_id === auth.deviceId && receipt.recipient_device_id === body.recipientDeviceId && receipt.payload_sha256 === payloadHash, 409, 'message_id_conflict');
  requireValue(receipt.acked_at !== null || results[3].results.length === 1, 409, 'device_unavailable');
  return json({ messageId: body.messageId, payloadSha256: payloadHash,
    status: receipt.acked_at === null ? 'queued' : 'delivered' }, results[0].meta.changes ? 201 : 200);
}
async function inbox(db, auth, url, now) {
  const allowed = new Set(['limit', 'afterTime', 'afterId']);
  for (const name of url.searchParams.keys()) requireValue(allowed.has(name) && url.searchParams.getAll(name).length === 1);
  const limitText = url.searchParams.get('limit') ?? '10';
  requireValue(/^[1-9]\d?$/.test(limitText) && Number(limitText) <= 50);
  const afterTime = Number(url.searchParams.get('afterTime') ?? '0');
  const afterId = url.searchParams.get('afterId') ?? '';
  requireValue(Number.isSafeInteger(afterTime) && afterTime >= 0 &&
    (afterId === '' ? !url.searchParams.has('afterTime') : url.searchParams.has('afterTime')));
  if (afterId) id(afterId);
  const result = await stmt(db, `SELECT m.*, r.payload_sha256 FROM messages m
    JOIN relay_message_ids r ON r.message_id = m.message_id
    JOIN devices s ON s.device_id = m.sender_device_id AND s.status = 'active'
    WHERE m.recipient_device_id = ? AND m.expires_at > ? AND r.acked_at IS NULL AND ${alive}
    AND (m.created_at > ? OR (m.created_at = ? AND m.message_id > ?))
    ORDER BY m.created_at, m.message_id LIMIT ?`, auth.deviceId, now, auth.deviceId, afterTime, afterTime, afterId, Number(limitText) + 1).all();
  const messages = []; let bytes = 0;
  for (const row of result.results.slice(0, Number(limitText))) {
    const nextBytes = encoder.encode(row.encrypted_payload).length;
    if (messages.length && bytes + nextBytes > 262144) break;
    bytes += nextBytes;
    messages.push({ messageId: row.message_id, senderDeviceId: row.sender_device_id,
      createdAt: row.created_at, expiresAt: row.expires_at, payloadSha256: row.payload_sha256,
      packet: JSON.parse(row.encrypted_payload) });
  }
  const last = messages.at(-1);
  return json({ messages, next: last && result.results.length > messages.length
    ? { afterTime: last.createdAt, afterId: last.messageId } : null });
}
async function ack(db, auth, messageId, body, now) {
  id(messageId); object(body, ['payloadSha256']);
  requireValue(typeof body.payloadSha256 === 'string' && /^[a-f0-9]{64}$/.test(body.payloadSha256));
  const results = await db.batch([
    stmt(db, `UPDATE relay_message_ids SET acked_at = COALESCE(acked_at, ?)
      WHERE message_id = ? AND recipient_device_id = ? AND payload_sha256 = ?
      AND message_expires_at > ? AND ${alive} RETURNING message_id`,
    now, messageId, auth.deviceId, body.payloadSha256, now, auth.deviceId),
    stmt(db, `DELETE FROM messages WHERE message_id = ? AND recipient_device_id = ?
      AND EXISTS (SELECT 1 FROM relay_message_ids r WHERE r.message_id = messages.message_id
      AND r.recipient_device_id = ? AND r.payload_sha256 = ? AND r.acked_at IS NOT NULL)`,
    messageId, auth.deviceId, auth.deviceId, body.payloadSha256),
  ]);
  requireValue(results[0].results.length === 1, 404, 'message_not_found');
  return json({ messageId, status: 'delivered', deleted: true });
}
export async function cleanup(db, now = nowSeconds()) {
  return db.batch([
    stmt(db, 'DELETE FROM messages WHERE expires_at <= ?', now),
    stmt(db, 'DELETE FROM relay_nonces WHERE expires_at <= ?', now),
    stmt(db, 'DELETE FROM relay_rate_windows WHERE window_start < ?', now - 600),
    stmt(db, 'DELETE FROM relay_message_ids WHERE retain_until <= ?', now),
  ]);
}
export default {
  async fetch(request, env, ctx) {
    let verifiedPrincipal = 'unverified';
    try {
      // cf is trusted runtime metadata. Never use CF-IPCountry/X-Forwarded-* headers.
      requireValue(request.cf?.country === 'UA', 403, 'country_not_allowed');
      const url = new URL(request.url); requireValue(url.protocol === 'https:', 426, 'https_required');
      requireValue(!request.headers.has('Origin'), 403, 'native_clients_only');
      if (request.method === 'GET' && url.pathname === '/health' && !url.search)
        return json({ service: 'mavka-relay', version: VERSION, status: 'running' });
      const isBootstrap = request.method === 'POST' && url.pathname === '/v1/bootstrap';
      const isActivate = request.method === 'POST' && url.pathname === '/v1/devices/activate';
      const revokeMatch = /^\/v1\/devices\/([A-Za-z0-9_-]+)\/revoke$/.exec(url.pathname);
      const ackMatch = /^\/v1\/messages\/([A-Za-z0-9_-]+)\/ack$/.exec(url.pathname);
      const known = (request.method === 'GET' && ['/v1/status', '/v1/devices', '/v1/messages'].includes(url.pathname)) ||
        (request.method === 'POST' && (isBootstrap || isActivate || ['/v1/devices', '/v1/messages'].includes(url.pathname) || revokeMatch || ackMatch));
      requireValue(known, 404, 'not_found');
      if (!(request.method === 'GET' && ['/v1/messages', '/v1/devices'].includes(url.pathname))) noQuery(url);
      requireValue(env.DB?.prepare, 503, 'relay_not_configured');
      // Direct DB queries always use the primary, including after revocation.
      // Do not introduce unconstrained read replicas for authentication.
      const db = env.DB;
      const raw = await readBody(request); const now = nowSeconds();
      const auth = await authenticate(request, env, db, raw, isBootstrap, isActivate, now);
      verifiedPrincipal = auth.row?.device_type === 'mobile' ? 'verified_mobile' : 'verified_main';
      if (isBootstrap) return await bootstrap(db, auth, parse(raw), now);
      if (isActivate) return await activate(db, auth, parse(raw), now);
      if (revokeMatch) return await revoke(db, auth, revokeMatch[1], parse(raw), now);
      if (ackMatch) return await ack(db, auth, ackMatch[1], parse(raw), now);
      if (request.method === 'POST' && url.pathname === '/v1/devices') return await register(db, auth, parse(raw), now);
      if (request.method === 'POST' && url.pathname === '/v1/messages') return await send(db, auth, raw, now);
      if (url.pathname === '/v1/messages') return await inbox(db, auth, url, now);
      isMain(auth);
      if (url.pathname === '/v1/devices') {
        const after = url.searchParams.get('after') ?? '';
        requireValue([...url.searchParams.keys()].every(k => k === 'after') && url.searchParams.getAll('after').length <= 1);
        if (after) id(after);
        const rows = await stmt(db, `SELECT device_id, device_type, public_signing_key, public_encryption_key,
          status, created_at, last_seen_at, revoked_at FROM devices WHERE device_id > ? ORDER BY device_id LIMIT 51`, after).all();
        return json({ devices: rows.results.slice(0, 50), next: rows.results.length > 50 ? rows.results[49].device_id : null });
      }
      const [devices, messages] = await db.batch([db.prepare('SELECT count(*) AS count FROM devices'), db.prepare('SELECT count(*) AS count FROM messages')]);
      return json({ service: 'mavka-relay', version: VERSION, database: 'ready',
        devices: devices.results[0].count, queuedMessages: messages.results[0].count });
    } catch (error) {
      // Never log request bodies, headers, keys, signatures, SQL errors or stacks.
      // This log intentionally omits client-supplied IDs, IPs and raw URLs.
      const security = reportSecurityEvent(request, error, verifiedPrincipal);
      // Best-effort, batch-coalesced audit: storage is never allowed to affect
      // the HTTP denial or leak additional information to its caller.
      try { queueSecurityRollup(security, env, ctx); } catch { /* fail closed */ }
      const status = error instanceof ApiError ? error.status : 503;
      return json({ error: error instanceof ApiError ? error.code : 'service_unavailable' }, status);
    }
  },
  async scheduled(_event, env, ctx) {
    // Always clean expired messages/nonces on the existing 5-minute cron.
    // Once per hour record one tiny D1 checkpoint based on DELETE metadata.
    // Optional migration failure must not impact existing message cleanup.
    ctx.waitUntil((async () => {
      const cleaned = await cleanup(env.DB);
      if (shouldPruneSecurityRollups(_event?.scheduledTime)) {
        const security = await pruneSecurityRollups(env.DB);
        await recordMaintenanceCheckpoint(env.DB, cleaned, security, _event.scheduledTime);
      }
    })());
  },
};
