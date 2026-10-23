import { ApiError, requireValue, object, id, encryptedEnvelope, sha256, encoder } from './protocol.js';

// This API stores *only* the client-encrypted envelope. No file names, plaintext,
// AES keys, or private signing keys reach R2.
const MAX_PARTS = 4096;
const MAX_FILE_TTL = 6 * 86400; // R2 lifecycle is 7 days; leave a safety margin.
const TOMBSTONE_TTL = 86400;
const PART_PATH = /^\/v1\/files\/parts\/([A-Za-z0-9][A-Za-z0-9_-]{15,79})\/([a-f0-9]{32})\/(0|[1-9][0-9]{0,3})(\/ack)?$/;
const UPLOAD_PATH = '/v1/files/parts';
const stmt = (db, sql, ...args) => db.prepare(sql).bind(...args);
const response = (value, status = 200) => new Response(JSON.stringify(value),
  { status, headers: { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff' } });
const keyFor = (recipient, sender, transfer, index) =>
  `transfers/${recipient}/${sender}/${transfer}/${String(index).padStart(4, '0')}`;
const active = "EXISTS (SELECT 1 FROM devices WHERE device_id = ? AND status = 'active')";
function parse(raw) { try { return JSON.parse(raw); } catch { throw new ApiError(400, 'invalid_json'); } }

export function isFileRoute(method, pathname) {
  if (method === 'POST' && pathname === UPLOAD_PATH) return true;
  const m = PART_PATH.exec(pathname);
  return !!m && (m[4] ? method === 'POST' : method === 'GET');
}

async function upload(env, db, auth, raw, now) {
  const body = parse(raw);
  object(body, ['recipientDeviceId', 'transferId', 'index', 'count', 'expiresAt', 'payload']);
  id(body.recipientDeviceId);
  requireValue(body.recipientDeviceId !== auth.deviceId, 400, 'self_transfer_forbidden');
  requireValue(auth.deviceId === auth.admin.id || body.recipientDeviceId === auth.admin.id,
    403, 'route_forbidden');
  requireValue(typeof body.transferId === 'string' && /^[a-f0-9]{32}$/.test(body.transferId));
  requireValue(Number.isSafeInteger(body.count) && body.count >= 1 && body.count <= MAX_PARTS);
  requireValue(Number.isSafeInteger(body.index) && body.index >= 0 && body.index < body.count);
  requireValue(Number.isSafeInteger(body.expiresAt) && body.expiresAt > now &&
    body.expiresAt <= now + MAX_FILE_TTL, 400, 'invalid_expiry');
  const payload = encryptedEnvelope(body.payload);
  const stored = JSON.stringify(payload);
  const size = encoder.encode(stored).byteLength;
  const digest = await sha256(stored);
  const args = [body.recipientDeviceId, auth.deviceId, body.transferId, body.index];
  await stmt(db, `INSERT OR IGNORE INTO relay_file_parts
    (recipient_device_id, sender_device_id, transfer_id, part_index, part_count,
      payload_sha256, size_bytes, expires_at, created_at)
    SELECT ?, ?, ?, ?, ?, ?, ?, ?, ? WHERE ${active} AND ${active}
      AND (SELECT count(*) FROM relay_file_parts WHERE acked_at IS NULL AND expires_at > ?) < 10000
      AND (SELECT count(*) FROM relay_file_parts WHERE sender_device_id = ?
        AND acked_at IS NULL AND expires_at > ?) < 2000`,
    ...args, body.count, digest, size, body.expiresAt, now,
    auth.deviceId, body.recipientDeviceId, now, auth.deviceId, now).run();
  const row = await stmt(db, `SELECT * FROM relay_file_parts WHERE
    recipient_device_id = ? AND sender_device_id = ? AND transfer_id = ? AND part_index = ?`, ...args).first();
  requireValue(row, 409, 'recipient_unavailable_or_file_capacity');
  requireValue(row.part_count === body.count && row.payload_sha256 === digest &&
    row.expires_at === body.expiresAt && row.size_bytes === size, 409, 'file_part_conflict');
  if (row.acked_at !== null) return response({ transferId: body.transferId, index: body.index, status: 'delivered' });
  const key = keyFor(...args);
  const written = await env.FILES.put(key, stored, {
    onlyIf: new Headers({ 'If-None-Match': '*' }),
    httpMetadata: { contentType: 'application/json', cacheControl: 'no-store' },
    customMetadata: { payloadSha256: digest, partCount: String(body.count), expiresAt: String(body.expiresAt) },
  });
  if (written === null) {
    const existing = await env.FILES.head(key);
    requireValue(existing?.customMetadata?.payloadSha256 === digest &&
      existing?.size === size, 409, 'file_part_conflict');
  }
  // A concurrent receiver ACK may have committed before the R2 write.
  const after = await stmt(db, `SELECT acked_at FROM relay_file_parts WHERE
    recipient_device_id = ? AND sender_device_id = ? AND transfer_id = ? AND part_index = ?`, ...args).first();
  if (after?.acked_at !== null && after?.acked_at !== undefined) {
    await env.FILES.delete(key);
    return response({ transferId: body.transferId, index: body.index, status: 'delivered' });
  }
  return response({ transferId: body.transferId, index: body.index, count: body.count,
    payloadSha256: digest, status: 'stored' }, written === null ? 200 : 201);
}

async function part(env, db, auth, raw, now, match) {
  const [, sender, transferId, indexText, ackSuffix] = match;
  const index = Number(indexText);
  requireValue(index < MAX_PARTS);
  const args = [auth.deviceId, sender, transferId, index];
  const row = await stmt(db, `SELECT * FROM relay_file_parts WHERE
    recipient_device_id = ? AND sender_device_id = ? AND transfer_id = ? AND part_index = ?`, ...args).first();
  requireValue(row, 404, 'file_part_not_found');
  const key = keyFor(...args);
  if (ackSuffix) {
    const input = parse(raw);
    object(input, ['payloadSha256']);
    requireValue(input.payloadSha256 === row.payload_sha256, 409, 'file_hash_mismatch');
    await stmt(db, `UPDATE relay_file_parts SET acked_at = COALESCE(acked_at, ?)
      WHERE recipient_device_id = ? AND sender_device_id = ? AND transfer_id = ? AND part_index = ?`,
      now, ...args).run();
    await env.FILES.delete(key);
    return response({ transferId, index, status: 'delivered', deleted: true });
  }
  requireValue(row.acked_at === null && row.expires_at > now, 404, 'file_part_not_found');
  // Revocation immediately blocks previously uploaded parts from the sender.
  requireValue(await stmt(db, "SELECT device_id FROM devices WHERE device_id = ? AND status = 'active'", sender).first(),
    403, 'sender_unavailable');
  const r2 = await env.FILES.get(key);
  requireValue(r2, 404, 'file_part_not_found');
  const stored = await r2.text();
  requireValue(await sha256(stored) === row.payload_sha256 &&
    encoder.encode(stored).byteLength === row.size_bytes, 503, 'file_integrity_error');
  return response({ senderDeviceId: sender, transferId, index, count: row.part_count,
    expiresAt: row.expires_at, payloadSha256: row.payload_sha256, payload: JSON.parse(stored) });
}

export async function handleFileRoute(request, env, db, auth, raw, now) {
  requireValue(env.FILES?.put && env.FILES?.get && env.FILES?.delete, 503, 'file_storage_not_configured');
  if (request.method === 'POST' && new URL(request.url).pathname === UPLOAD_PATH)
    return upload(env, db, auth, raw, now);
  const match = PART_PATH.exec(new URL(request.url).pathname);
  requireValue(match, 404, 'not_found');
  return part(env, db, auth, raw, now, match);
}
