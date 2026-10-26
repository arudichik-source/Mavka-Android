import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { Miniflare, convertV4MiniflareOptions } from 'miniflare';
import worker, { cleanup } from '../src/worker.js';
import { SECURITY_EVENT, securityEvent } from '../src/security-audit.js';
import { flushSecurityRollups, readSecurityRollups, pruneSecurityRollups, queueSecurityRollup, shouldPruneSecurityRollups } from '../src/security-rollups.js';
import { readDeviceTelemetry } from '../src/device-telemetry.js';
import { readMaintenanceCheckpoint, recordMaintenanceCheckpoint, shouldWriteMaintenanceCheckpoint } from '../src/maintenance.js';
import {scanR2Transfers,readR2AuditFromRow} from '../src/r2-audit.js';
import { ApiError } from '../src/protocol.js';
import { signedRequest, verifyDelivery, base64url, MAX_TTL } from '../src/protocol.js';
import { configuration } from '../tools/configure.mjs';

const origin = 'https://mavka-relay.arudichik.workers.dev';
const seconds = () => Math.floor(Date.now() / 1000);
const uuid = () => crypto.randomUUID();
async function identity() {
  const signing = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
  const encryption = await crypto.subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']);
  return { deviceId: uuid(), privateKey: signing.privateKey,
    publicSigningKey: await crypto.subtle.exportKey('jwk', signing.publicKey),
    publicEncryptionKey: await crypto.subtle.exportKey('jwk', encryption.publicKey) };
}
async function fixture(t) {
  const main = await identity();
  const options = { cf: { country: 'UA' }, telemetry: { enabled: false }, workers: [{ name: 'relay',
    modulesRoot: fileURLToPath(new URL('../', import.meta.url)),
    modules: ['worker.js', 'protocol.js', 'security-audit.js', 'security-rollups.js', 'maintenance.js', 'r2-audit.js'].map(name => ({ type: 'ESModule', path: fileURLToPath(new URL('../src/' + name, import.meta.url)) })),
    compatibilityDate: '2026-10-01', d1Databases: { DB: 'relay-tests' },
    bindings: { RELAY_ADMIN_DEVICE_ID: main.deviceId, RELAY_ADMIN_SIGNING_KEY: JSON.stringify(main.publicSigningKey) } }] };
  const mf = new Miniflare(convertV4MiniflareOptions(options));
  t.after(() => mf.dispose());
  let db = await mf.getD1Database('DB');
  const schema = await readFile(new URL('../migrations/0001_relay.sql', import.meta.url), 'utf8');
  const statements = schema.replace(/--[^\n]*/g, '').split(';').map(s => s.trim()).filter(Boolean);
  const migrate = () => db.batch(statements.map(sql => db.prepare(sql)));
  await migrate();
  async function dispatch(request, country = 'UA') {
    const r = await mf.dispatchFetch(request.url, { method: request.method, headers: request.headers,
      ...(request.method === 'GET' ? {} : { body: await request.text() }), cf: { country } });
    return { status: r.status, body: await r.json(), headers: r.headers };
  }
  async function request(who, path, method = 'GET', body, options = {}) {
    const raw = body === undefined ? '' : JSON.stringify(body);
    return dispatch(await signedRequest(origin + path, method, raw, who.deviceId, who.privateKey, options));
  }
  async function bootstrap() {
    const r = await request(main, '/v1/bootstrap', 'POST', { publicEncryptionKey: main.publicEncryptionKey });
    assert.equal(r.status, 201, JSON.stringify(r.body));
  }
  async function enroll(who, activate = true) {
    const r = await request(main, '/v1/devices', 'POST', { deviceId: who.deviceId,
      publicSigningKey: who.publicSigningKey, publicEncryptionKey: who.publicEncryptionKey });
    assert.equal(r.status, 201, JSON.stringify(r.body));
    if (activate) assert.equal((await request(who, '/v1/devices/activate', 'POST', {})).status, 200);
  }
  const restart = async () => {
    await mf.setOptions(convertV4MiniflareOptions({ ...options, workers: [{ ...options.workers[0],
      bindings: { ...options.workers[0].bindings, TEST_GENERATION: uuid() } }] }));
    db = await mf.getD1Database('DB');
  };
  return { main, mf, get db() { return db; }, migrate, dispatch, request, bootstrap, enroll, restart };
}
async function encrypted(plain = 'Synthetic test only') {
  const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']);
  const nonce = crypto.getRandomValues(new Uint8Array(12)); const keyId = '0123456789abcdef';
  const data = new TextEncoder().encode(plain);
  const result = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv: nonce,
    additionalData: new TextEncoder().encode('MAVKA_ENCRYPTED_V1:' + keyId) }, key, data));
  const b64 = value => Buffer.from(value).toString('base64');
  return { key, plain, payload: { format: 'MAVKA_ENCRYPTED_V1', algorithm: 'AES-256-GCM', keyId,
    nonce: b64(nonce), ciphertext: b64(result.slice(0, -16)), tag: b64(result.slice(-16)) } };
}
async function sendBody(recipient, plain) {
  const e = await encrypted(plain);
  return { ...e, body: { messageId: uuid(), recipientDeviceId: recipient.deviceId, expiresAt: seconds() + 3600, payload: e.payload } };
}

test('geofence trusts only request.cf; health discloses no DB or device data', async t => {
  const f = await fixture(t);
  const health = await f.dispatch(new Request(origin + '/health'));
  assert.equal(health.status, 200); assert.equal(health.body.status, 'running');
  assert.equal(health.headers.get('cache-control'), 'no-store');
  for (const country of ['US', 'PL', undefined]) {
    const r = await worker.fetch(Object.assign(new Request(origin + '/health', { headers: { 'CF-IPCountry': 'UA' } }),
      { cf: country ? { country } : undefined }), {});
    assert.equal(r.status, 403);
  }
  const foreign = await f.dispatch(new Request(origin + '/health', { headers: { 'CF-IPCountry': 'UA' } }), 'US');
  assert.equal(foreign.status, 403);
  assert.equal((await f.dispatch(new Request(origin + '/health', { headers: { Origin: 'https://attacker.example' } }))).status, 403);
  assert.equal((await f.dispatch(new Request('http://mavka-relay.arudichik.workers.dev/health'))).status, 426);
});

test('pinned bootstrap, additive migration, main-controlled enrollment and proof of possession', async t => {
  const f = await fixture(t); const mobile = await identity(); const stranger = await identity();
  assert.equal((await f.request(stranger, '/v1/bootstrap', 'POST', { publicEncryptionKey: stranger.publicEncryptionKey })).status, 401);
  await f.bootstrap(); await f.migrate();
  assert.equal((await f.db.prepare('SELECT count(*) AS n FROM devices').first()).n, 1);
  const columns = (await f.db.prepare('PRAGMA table_info(devices)').all()).results;
  assert.equal(columns.find(c => c.name === 'public_encryption_key').notnull, 0);
  assert.equal(columns.find(c => c.name === 'status').dflt_value, "'pending'");
  const legacyId = uuid();
  await f.db.prepare("INSERT INTO devices (device_id, device_type, public_signing_key) VALUES (?, 'mobile', ?)")
    .bind(legacyId, JSON.stringify(stranger.publicSigningKey)).run();
  await f.migrate();
  const legacy = await f.db.prepare('SELECT * FROM devices WHERE device_id = ?').bind(legacyId).first();
  assert.equal(legacy.public_encryption_key, null); assert.equal(legacy.status, 'pending');
  await f.db.prepare('DELETE FROM devices WHERE device_id = ?').bind(legacyId).run();
  assert.equal((await f.request(f.main, '/v1/bootstrap', 'POST', { publicEncryptionKey: f.main.publicEncryptionKey })).status, 200);
  assert.equal((await f.request(f.main, '/v1/bootstrap', 'POST', { publicEncryptionKey: mobile.publicEncryptionKey })).status, 409);
  assert.equal((await f.request(mobile, '/v1/devices/activate', 'POST', {})).status, 401);
  await f.enroll(mobile, false);
  assert.equal((await f.request(mobile, '/v1/messages')).status, 401);
  const impersonation = await signedRequest(origin + '/v1/devices/activate', 'POST', '{}', mobile.deviceId, stranger.privateKey);
  assert.equal((await f.dispatch(impersonation)).status, 401);
  assert.equal((await f.request(mobile, '/v1/devices/activate', 'POST', {})).status, 200);
  assert.equal((await f.request(f.main, '/v1/devices', 'POST', { deviceId: mobile.deviceId,
    publicSigningKey: stranger.publicSigningKey, publicEncryptionKey: mobile.publicEncryptionKey })).status, 409);
  assert.equal((await f.request(mobile, '/v1/devices', 'POST', { deviceId: stranger.deviceId,
    publicSigningKey: stranger.publicSigningKey, publicEncryptionKey: stranger.publicEncryptionKey })).status, 403);
  assert.equal((await f.request(mobile, '/v1/devices')).status, 403);
  const status = await f.request(f.main, '/v1/status'); assert.equal(status.status, 200); assert.equal(status.body.devices, 2);
});

test('request signature covers body, method, origin, path, query, device and persistent nonce', async t => {
  const f = await fixture(t); await f.bootstrap();
  const r = await signedRequest(origin + '/v1/status', 'GET', '', f.main.deviceId, f.main.privateKey);
  assert.equal((await f.dispatch(r.clone())).status, 200);
  assert.equal((await f.dispatch(r.clone())).body.error, 'request_replayed');
  await f.restart();
  assert.equal((await f.dispatch(r.clone())).body.error, 'request_replayed');
  const future = await signedRequest(origin + '/v1/status', 'GET', '', f.main.deviceId, f.main.privateKey, { time: seconds() + 299 });
  assert.equal((await f.dispatch(future.clone())).status, 200);
  await cleanup(f.db, seconds() + 1);
  assert.equal((await f.dispatch(future.clone())).body.error, 'request_replayed');
  const altered = await signedRequest(origin + '/v1/status', 'GET', '', f.main.deviceId, f.main.privateKey);
  assert.equal((await f.dispatch(new Request(origin + '/v1/devices?after=' + uuid(), { headers: altered.headers }))).status, 401);
  const wrongHost = await signedRequest('https://other.example/v1/status', 'GET', '', f.main.deviceId, f.main.privateKey);
  assert.equal((await f.dispatch(new Request(origin + '/v1/status', { headers: wrongHost.headers }))).status, 401);
  const signedPost = await signedRequest(origin + '/v1/bootstrap', 'POST', JSON.stringify({ publicEncryptionKey: f.main.publicEncryptionKey }), f.main.deviceId, f.main.privateKey);
  assert.equal((await f.dispatch(new Request(signedPost.url, { method: 'POST', headers: signedPost.headers, body: '{}' }))).status, 401);
  for (const time of [seconds() - 301, seconds() + 301])
    assert.equal((await f.request(f.main, '/v1/status', 'GET', undefined, { time })).status, 401);
  const nonce = base64url(crypto.getRandomValues(new Uint8Array(16)));
  const concurrent = await Promise.all([1, 2].map(() => f.request(f.main, '/v1/status', 'GET', undefined, { nonce })));
  assert.deepEqual(concurrent.map(r => r.status).sort(), [200, 409]);
});

test('encrypted delivery: repeated fetch retains data; recipient proof, durable ACK, deletion and no resurrection', async t => {
  const f = await fixture(t); const mobile = await identity(); const other = await identity();
  await f.bootstrap(); await f.enroll(mobile); await f.enroll(other);
  const data = await sendBody(mobile, 'Synthetic classified-looking text is never sent in plaintext');
  const sent = await f.request(f.main, '/v1/messages', 'POST', data.body);
  assert.equal(sent.status, 201, JSON.stringify(sent.body));
  assert.equal((await f.request(f.main, '/v1/messages', 'POST', data.body)).status, 200);
  const stored = await f.db.prepare('SELECT * FROM messages').first();
  assert.equal(stored.encrypted_payload.includes(data.plain), false);
  assert.equal((await f.request(other, '/v1/messages')).body.messages.length, 0);
  const inbox = await f.request(mobile, '/v1/messages'); assert.equal(inbox.body.messages.length, 1);
  assert.equal((await f.request(mobile, '/v1/messages')).body.messages.length, 1);
  const delivery = inbox.body.messages[0];
  const payload = await verifyDelivery(delivery, origin, f.main.publicSigningKey, mobile.deviceId);
  const decrypted = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: Buffer.from(payload.nonce, 'base64'),
    additionalData: new TextEncoder().encode('MAVKA_ENCRYPTED_V1:' + payload.keyId) }, data.key,
  Buffer.concat([Buffer.from(payload.ciphertext, 'base64'), Buffer.from(payload.tag, 'base64')]));
  assert.equal(new TextDecoder().decode(decrypted), data.plain);
  await assert.rejects(verifyDelivery(delivery, origin, other.publicSigningKey, mobile.deviceId));
  const changed = structuredClone(delivery); changed.packet.requestBody = changed.packet.requestBody.replace(data.body.messageId, uuid());
  await assert.rejects(verifyDelivery(changed, origin, f.main.publicSigningKey, mobile.deviceId));
  const path = '/v1/messages/' + data.body.messageId + '/ack';
  assert.equal((await f.request(other, path, 'POST', { payloadSha256: sent.body.payloadSha256 })).status, 404);
  assert.equal((await f.request(f.main, path, 'POST', { payloadSha256: sent.body.payloadSha256 })).status, 404);
  assert.equal((await f.request(mobile, path, 'POST', { payloadSha256: '0'.repeat(64) })).status, 404);
  assert.equal((await f.db.prepare('SELECT count(*) AS n FROM messages').first()).n, 1);
  const ack = await f.request(mobile, path, 'POST', { payloadSha256: sent.body.payloadSha256 });
  assert.equal(ack.status, 200); assert.equal(ack.body.deleted, true);
  assert.equal((await f.db.prepare('SELECT count(*) AS n FROM messages').first()).n, 0);
  assert.equal((await f.request(mobile, path, 'POST', { payloadSha256: sent.body.payloadSha256 })).status, 200);
  const retry = await f.request(f.main, '/v1/messages', 'POST', data.body);
  assert.equal(retry.status, 200); assert.equal(retry.body.status, 'delivered');
  assert.equal((await f.db.prepare('SELECT count(*) AS n FROM messages').first()).n, 0);
  const receipt = await f.db.prepare('SELECT * FROM relay_message_ids').first();
  assert.equal(Object.hasOwn(receipt, 'encrypted_payload'), false); assert.ok(receipt.acked_at);
  const alteredBody = { ...data.body, payload: (await encrypted()).payload };
  assert.equal((await f.request(f.main, '/v1/messages', 'POST', alteredBody)).status, 409);
});

test('ACK is transactional: a failing DELETE rolls back the delivery marker', async t => {
  const f = await fixture(t); const mobile = await identity(); await f.bootstrap(); await f.enroll(mobile);
  const data = await sendBody(mobile); const sent = await f.request(f.main, '/v1/messages', 'POST', data.body);
  await f.db.prepare("CREATE TRIGGER fail_ack BEFORE DELETE ON messages BEGIN SELECT RAISE(ABORT, 'test failure'); END").run();
  const ack = await f.request(mobile, '/v1/messages/' + data.body.messageId + '/ack', 'POST', { payloadSha256: sent.body.payloadSha256 });
  assert.equal(ack.status, 503); assert.deepEqual(ack.body, { error: 'service_unavailable' });
  assert.equal((await f.db.prepare('SELECT acked_at FROM relay_message_ids').first()).acked_at, null);
  assert.equal((await f.db.prepare('SELECT count(*) AS n FROM messages').first()).n, 1);
});

test('mobile responses to main, pagination, expiry filtering and scheduled cleanup', async t => {
  const f = await fixture(t); const mobile = await identity(); await f.bootstrap(); await f.enroll(mobile);
  for (let i = 0; i < 3; i++) {
    const data = await sendBody(f.main);
    assert.equal((await f.request(mobile, '/v1/messages', 'POST', data.body)).status, 201);
  }
  const page1 = (await f.request(f.main, '/v1/messages?limit=2')).body;
  assert.equal(page1.messages.length, 2); assert.ok(page1.next);
  const page2 = (await f.request(f.main, '/v1/messages?limit=2&afterTime=' + page1.next.afterTime + '&afterId=' + page1.next.afterId)).body;
  assert.equal(page2.messages.length, 1); assert.equal(page2.next, null);
  assert.equal(new Set([...page1.messages, ...page2.messages].map(m => m.messageId)).size, 3);
  for (const path of ['/v1/messages?limit=51', '/v1/messages?limit=1&limit=2', '/v1/messages?bad=1', '/v1/messages?afterTime=1'])
    assert.equal((await f.request(f.main, path)).status, 400);
  const expiry = seconds() - 1;
  await f.db.prepare('UPDATE messages SET created_at = ?, expires_at = ?').bind(expiry - 10, expiry).run();
  assert.equal((await f.request(f.main, '/v1/messages')).body.messages.length, 0);
  await cleanup(f.db, seconds() + MAX_TTL + 86401);
  for (const table of ['messages', 'relay_nonces', 'relay_rate_windows', 'relay_message_ids'])
    assert.equal((await f.db.prepare('SELECT count(*) AS n FROM ' + table).first()).n, 0);
  assert.equal((await f.db.prepare('SELECT count(*) AS n FROM devices').first()).n, 2);
});

test('revocation blocks new requests, reactivation, reregistration and delivery to a revoked target', async t => {
  const f = await fixture(t); const mobile = await identity(); const other = await identity();
  await f.bootstrap(); await f.enroll(mobile); await f.enroll(other);
  const data = await sendBody(other);
  assert.equal((await f.request(mobile, '/v1/messages', 'POST', data.body)).status, 403);
  assert.equal((await f.request(f.main, '/v1/devices/' + f.main.deviceId + '/revoke', 'POST', {})).status, 403);
  assert.equal((await f.request(f.main, '/v1/devices/' + mobile.deviceId + '/revoke', 'POST', {})).status, 200);
  for (const path of ['/v1/messages', '/v1/status']) assert.equal((await f.request(mobile, path)).status, 401);
  assert.equal((await f.request(mobile, '/v1/devices/activate', 'POST', {})).status, 401);
  const reregister = await f.request(f.main, '/v1/devices', 'POST', { deviceId: mobile.deviceId,
    publicSigningKey: mobile.publicSigningKey, publicEncryptionKey: mobile.publicEncryptionKey });
  assert.equal(reregister.status, 409);
  const toRevoked = await sendBody(mobile);
  assert.equal((await f.request(f.main, '/v1/messages', 'POST', toRevoked.body)).status, 409);
});

test('plaintext, invalid envelope, private public-key input, oversize and unknown routes are rejected', async t => {
  const f = await fixture(t); const mobile = await identity(); await f.bootstrap(); await f.enroll(mobile);
  const data = await sendBody(mobile);
  for (const body of [{ ...data.body, payload: { text: 'plaintext' } }, { ...data.body, extra: 'plaintext' },
    { ...data.body, expiresAt: seconds() - 1 }, { ...data.body, expiresAt: seconds() + MAX_TTL + 30 },
    { ...data.body, payload: { ...data.payload, tag: 'broken' } },
    { ...data.body, payload: { ...data.payload, ciphertext: Buffer.alloc(32769).toString('base64') } }])
    assert.equal((await f.request(f.main, '/v1/messages', 'POST', body)).status, 400);
  const badPublic = { deviceId: uuid(), publicSigningKey: { ...mobile.publicSigningKey, d: 'private' }, publicEncryptionKey: mobile.publicEncryptionKey };
  assert.equal((await f.request(f.main, '/v1/devices', 'POST', badPublic)).status, 400);
  badPublic.publicSigningKey = { ...mobile.publicSigningKey, x: 'A' };
  assert.equal((await f.request(f.main, '/v1/devices', 'POST', badPublic)).status, 400);
  const oversized = await signedRequest(origin + '/v1/messages', 'POST', ' '.repeat(49153), f.main.deviceId, f.main.privateKey);
  assert.equal((await f.dispatch(oversized)).status, 413);
  const noJson = await signedRequest(origin + '/v1/messages', 'POST', '{}', f.main.deviceId, f.main.privateKey);
  noJson.headers.set('Content-Type', 'text/plain'); assert.equal((await f.dispatch(noJson)).status, 415);
  assert.equal((await f.request(f.main, '/v1/unknown')).status, 404);
  assert.equal((await f.request(f.main, '/v1/messages', 'POST', { ...data.body, messageId: "x'); DROP TABLE devices; --" })).status, 400);
  assert.equal((await f.db.prepare('SELECT count(*) AS n FROM devices').first()).n, 2);
});

test('per-device and global rate limits and bounded queue storage', async t => {
  const f = await fixture(t); const mobile = await identity(); await f.bootstrap(); await f.enroll(mobile);
  const current = seconds(); const window = current - current % 60;
  await f.db.prepare('INSERT INTO relay_rate_windows VALUES (?, ?, 120) ON CONFLICT(device_id, window_start) DO UPDATE SET requests=120').bind(mobile.deviceId, window).run();
  assert.equal((await f.request(mobile, '/v1/messages')).status, 429);
  await f.db.prepare('INSERT INTO relay_rate_windows VALUES (?, ?, 3000) ON CONFLICT(device_id, window_start) DO UPDATE SET requests=3000').bind('__global__', window).run();
  assert.equal((await f.request(f.main, '/v1/status')).status, 429);
  await f.db.prepare('DELETE FROM relay_rate_windows').run();
  const data = await sendBody(mobile);
  await f.db.prepare('INSERT INTO messages VALUES (?, ?, ?, ?, ?, ?)').bind(data.body.messageId,
    f.main.deviceId, mobile.deviceId, '{}', seconds(), seconds()+3600).run();
  assert.equal((await f.request(f.main, '/v1/messages', 'POST', data.body)).status, 409);
  assert.equal((await f.db.prepare('SELECT count(*) AS n FROM relay_message_ids').first()).n, 0);
  await f.db.prepare('DELETE FROM messages').run();
  // Seed synthetic queue rows, not 1000 network requests; exercise atomic SQL capacity guard.
  await f.db.prepare(`WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<1000)
    INSERT INTO messages SELECT 'synthetic-' || printf('%020d', x), ?, ?, '{}', ?, ? FROM n`).bind(f.main.deviceId, mobile.deviceId, seconds(), seconds()+3600).run();
  assert.equal((await f.request(f.main, '/v1/messages', 'POST', data.body)).status, 409);
  assert.equal((await f.db.prepare('SELECT count(*) AS n FROM relay_message_ids').first()).n, 0);
});

test('configuration fails closed and never creates a database or private-key binding', async () => {
  await assert.rejects(configuration({}));
  const main = await identity();
  const input = { MAVKA_D1_DATABASE_ID: uuid(), MAVKA_ADMIN_DEVICE_ID: main.deviceId, MAVKA_ADMIN_SIGNING_JWK: JSON.stringify(main.publicSigningKey) };
  const config = await configuration(input);
  assert.equal(config.name, 'mavka-relay'); assert.equal(config.d1_databases[0].binding, 'DB');
  assert.equal(config.preview_urls, false); assert.deepEqual(config.triggers.crons, ['*/5 * * * *']);
  assert.deepEqual(config.observability, {
    enabled: true,
    redact_query_string: true,
    logs: { enabled: true, invocation_logs: true, persist: true, head_sampling_rate: 1 },
    traces: { enabled: false },
    issues: { enabled: false },
  });
  assert.deepEqual(config.r2_buckets, [{ binding: 'FILES', bucket_name: 'mavka-private-files' }]);
  await assert.rejects(configuration({ ...input, MAVKA_ADMIN_SIGNING_JWK: JSON.stringify({ ...main.publicSigningKey, d: 'secret' }) }));
  assert.equal(Object.hasOwn(JSON.parse(config.vars.RELAY_ADMIN_SIGNING_KEY), 'd'), false);
});

test('security telemetry is allowlisted, anonymous, and does not change HTTP denials', async () => {
  const secretId = 'SENSITIVE_DEVICE_0123456789';
  const secretQuery = 'QUERY_SECRET_NEVER_LOG';
  const secretIp = '198.51.100.42';
  const req = Object.assign(new Request(origin + '/v1/devices/' + secretId +
    '/revoke?token=' + secretQuery, { method: 'POST', headers: {
      'CF-Connecting-IP': secretIp, 'X-Mavka-Device': secretId,
      'X-Mavka-Signature': 'SECRET_SIGNATURE_NEVER_LOG' } }), { cf: { country: 'US' } });
  const denied = new ApiError(401, 'unauthorized');
  denied.auditReason = 'auth_signature_invalid';
  const event = securityEvent(req, denied, 'verified_main');
  assert.deepEqual(event, {
    event: SECURITY_EVENT, outcome: 'rejected', category: 'authentication',
    reason: 'auth_signature_invalid', status: 401, route: 'revocation',
    method: 'POST', countryScope: 'non_UA', principal: 'verified_main',
  });
  const output = JSON.stringify(event);
  for (const sensitive of [secretId, secretQuery, secretIp, 'SECRET_SIGNATURE_NEVER_LOG', 'US'])
    assert.equal(output.includes('"' + sensitive + '"'), false);
  assert.equal(output.includes(secretId), false);
  assert.equal(output.includes(secretQuery), false);
  const rawError = new ApiError(400, 'attacker-controlled-secret');
  assert.equal(securityEvent(req, rawError).reason, 'request_rejected');
  assert.equal(securityEvent(req, new Error('secret stack trace')).reason, 'service_unavailable');

  const emitted = []; const previous = console.info;
  console.info = (...args) => emitted.push(args.join(' '));
  try {
    const foreign = Object.assign(new Request(origin + '/health?token=' + secretQuery), { cf: { country: 'US' } });
    const response = await worker.fetch(foreign, {});
    assert.equal(response.status, 403);
    assert.deepEqual(await response.json(), { error: 'country_not_allowed' });
    const valid = Object.assign(new Request(origin + '/health'), { cf: { country: 'UA' } });
    assert.equal((await worker.fetch(valid, {})).status, 200);
  } finally {
    console.info = previous;
  }
  assert.equal(emitted.length, 1);
  const parsed = JSON.parse(emitted[0]);
  assert.equal(parsed.event, SECURITY_EVENT);
  assert.equal(parsed.reason, 'country_not_allowed');
  assert.equal(parsed.route, 'health');
  assert.equal(parsed.principal, 'unverified');
  assert.equal(emitted[0].includes(secretQuery), false);
});

test('named RPC device telemetry returns aggregates without device records or keys', async t => {
  const f = await fixture(t);
  await f.bootstrap();
  const mob1 = await identity(), mob2 = await identity();
  await f.enroll(mob1);
  await f.enroll(mob2, false);
  const snapshot = await readDeviceTelemetry(f.db);
  assert.equal(snapshot.state, 'ready');
  assert.equal(snapshot.total, 3);
  assert.equal(snapshot.mainActive, 1);
  assert.equal(snapshot.mobileActive, 1);
  assert.equal(snapshot.mobilePending, 1);
  assert.equal(snapshot.mobileRevoked, 0);
  assert.equal(snapshot.mobileSeenRecently, 1);
  assert.ok(snapshot.mobileSeenRecently <= snapshot.mobileActive);
  assert.equal(JSON.stringify(snapshot).includes(mob1.deviceId), false);
  assert.equal(JSON.stringify(snapshot).includes(mob2.deviceId), false);
  assert.equal(JSON.stringify(snapshot).includes('public_signing_key'), false);
  assert.equal(JSON.stringify(snapshot).includes('public_encryption_key'), false);
  // Metadata-only read does not mutate Relay's authorization state.
  assert.equal((await f.request(f.main, '/v1/status')).status, 200);
});
test('device aggregation returns unavailable instead of inventing zeros if D1 fails', async () => {
  const bad = { prepare(){throw new Error('private SQL connection details') } };
  const result = await readDeviceTelemetry(bad);
  assert.deepEqual(result, {state:'unavailable',reason:'database_unavailable'});
  assert.equal(JSON.stringify(result).includes('private SQL'), false);
});


test('security rollup aggregation uses only bounded allowlisted anonymous dimensions', async t => {
  const f=await fixture(t);
  const migration=await readFile(new URL('../migrations/0003_security_rollups.sql',import.meta.url),'utf8');
  const statements=migration.replace(/--[^\n]*/g,'').split(';').map(x=>x.trim()).filter(Boolean);
  await f.db.batch(statements.map(sql=>f.db.prepare(sql)));
  const now=seconds();
  const bucket=Math.floor(now/900)*900;
  const source={
    bucket, category:'authentication',reason:'auth_signature_invalid',
    country:'UA',route:'status',status:401,principal:'unverified',events:3,
  };
  assert.equal(await flushSecurityRollups(f.db,[source]),true);
  assert.equal(await flushSecurityRollups(f.db,[{...source,events:2}]),true);
  const dashboard=await readSecurityRollups(f.db,now);
  assert.equal(dashboard.state,'ready');
  assert.equal(dashboard.coverage,'best_effort');
  assert.equal(dashboard.total24h,5);
  assert.equal(dashboard.total7d,5);
  assert.deepEqual(dashboard.byReason,[{category:'authentication',reason:'auth_signature_invalid',count:5}]);
  assert.deepEqual(dashboard.byCountry,[{scope:'UA',count:5}]);
  assert.equal(dashboard.byHour.length,1);
  assert.equal(dashboard.byHour[0].count,5);
  assert.deepEqual(dashboard.signalDays,[{
    dayStart:Math.floor(bucket/86400)*86400,
    reason:'auth_signature_invalid',count:5,
  }]);
  for(const forbidden of ['device_id','x-mavka','clientIp','nonce','signature','message','email','publicKey']){
    assert.equal(Object.keys(dashboard).some(k=>k===forbidden),false);
  }
  await pruneSecurityRollups(f.db,now+31*86400);
  assert.equal((await readSecurityRollups(f.db,now+31*86400)).total24h,0);
});
test('security aggregation schema absent cannot block Relay or fabricate incident counts',async t=>{
  const f=await fixture(t);
  const data=await readSecurityRollups(f.db);
  assert.equal(data.state,'not_connected');
  assert.equal(data.total24h,null);
  assert.equal(data.byReason.length,0);
  assert.equal(await flushSecurityRollups(f.db,[{
    bucket:seconds(),category:'authentication',reason:'unauthorized',
    country:'UA',route:'status',status:401,principal:'unverified',events:1,
  }]),false);
  await pruneSecurityRollups(f.db); // missing optional table: must not throw
  const health=await f.dispatch(new Request(origin+'/health'));
  assert.equal(health.status,200);
});
test('untrusted security event metadata is converted to fixed enum categories before persistence',async()=>{
  const statements=[];
  const db={ prepare(sql){return{bind(...values){
    statements.push({sql,values});return{run:async()=>({})};
  }}},batch:async x=>x.map(()=>({})) };
  const now=Date.now(),event={
    event:SECURITY_EVENT,outcome:'rejected',category:'PRIVATE_UNKNOWN',
    reason:'LEAK-PRIVATE-TOKEN',countryScope:'CLIENT-CONTROLLED-IP',
    route:'path/privateDeviceID',status:401,principal:'malicious',
  };
  const tasks=[],ctx={waitUntil(p){tasks.push(p)}};
  queueSecurityRollup(event,{DB:db},ctx,now);
  await Promise.all(tasks);
  assert.equal(statements.length,1);
  const values=JSON.stringify(statements[0].values);
  for(const secret of ['LEAK-PRIVATE-TOKEN','CLIENT-CONTROLLED-IP','path/privateDeviceID','PRIVATE_UNKNOWN','malicious'])
    assert.equal(values.includes(secret),false);
  assert.equal(values.includes('request_rejected'),true);
});

test('30-day security cleanup is hourly rather than every Relay five-minute cycle',()=>{
 const t=Date.UTC(2026,9,25,12,0,0);
 assert.equal(shouldPruneSecurityRollups(t),true);
 assert.equal(shouldPruneSecurityRollups(t+5*60000),false);
 assert.equal(shouldPruneSecurityRollups(t+55*60000),false);
 assert.equal(shouldPruneSecurityRollups(t+60*60000),true);
 assert.equal(shouldPruneSecurityRollups(undefined),false);
});
test('14-day signal history is bounded by date and suppresses detailed origin information',async t=>{
 const f=await fixture(t);
 const migration=await readFile(new URL('../migrations/0003_security_rollups.sql',import.meta.url),'utf8');
 await f.db.batch(migration.replace(/--[^\n]*/g,'').split(';')
  .map(s=>s.trim()).filter(Boolean).map(sql=>f.db.prepare(sql)));
 const now=seconds(),hour=Math.floor(now/900)*900;
 const sample=(bucket,reason,events=1)=>({
  bucket,category:'authentication',reason,country:'non_UA',
  route:'other',status:401,principal:'unverified',events,
 });
 assert.equal(await flushSecurityRollups(f.db,[
  sample(hour,'request_replayed',3),
  sample(hour-4*86400,'rate_limited',7),
  sample(hour-16*86400,'country_not_allowed',5),
 ]),true);
 const d=await readSecurityRollups(f.db,now);
 assert.equal(d.state,'ready');
 assert.equal(d.signalDays.length,2);
 assert.equal(d.signalDays.some(x=>x.reason==='country_not_allowed'),false);
 assert.equal(d.signalDays.some(x=>x.reason==='request_replayed'),true);
 assert.equal(d.signalDays.some(x=>x.reason==='rate_limited'),true);
 for(const item of d.signalDays){
  assert.deepEqual(Object.keys(item).sort(),['count','dayStart','reason'].sort());
 }
});

test('hourly maintenance checkpoint uses actual DELETE metadata; no extra expiry scans',async t=>{
 const f=await fixture(t);
 const now=seconds(), hourMs=Math.floor(Date.now()/3600000)*3600000;
 const sql=await readFile(new URL('../migrations/0004_maintenance_status.sql',import.meta.url),'utf8');
 await f.db.batch(sql.replace(/--[^\n]*/g,'').split(';').map(s=>s.trim()).filter(Boolean)
   .map(s=>f.db.prepare(s)));
 const secSql=await readFile(new URL('../migrations/0003_security_rollups.sql',import.meta.url),'utf8');
 await f.db.batch(secSql.replace(/--[^\n]*/g,'').split(';').map(s=>s.trim()).filter(Boolean)
   .map(s=>f.db.prepare(s)));
 await f.db.prepare('INSERT INTO relay_nonces(device_id,nonce,expires_at) VALUES (?,?,?)')
  .bind(f.main.deviceId,'sample-expired-nonce',now-100).run();
 await f.db.prepare('INSERT INTO relay_rate_windows(device_id,window_start,requests) VALUES (?,?,?)')
  .bind(f.main.deviceId,now-3600,1).run();
 const promises=[];
 await worker.scheduled({scheduledTime:hourMs},{DB:f.db},{waitUntil:p=>promises.push(p)});
 await Promise.all(promises);
 const status=await readMaintenanceCheckpoint(f.db,Math.floor(hourMs/1000)+60);
 assert.equal(status.state,'ready');
 assert.equal(status.data.checkedAt,Math.floor(hourMs/1000));
 assert.equal(status.data.deletionCounts.deleted_nonces,1);
 assert.equal(status.data.deletionCounts.deleted_rate_windows,1);
 assert.equal(status.data.deletionCounts.deleted_messages,0);
 assert.equal(status.data.securityPruneSucceeded,true);
 assert.equal(Object.keys(status.data.deletionCounts).length,5);
 const raw=JSON.stringify(status);
 assert.equal(raw.includes(f.main.deviceId),false);
 assert.equal(raw.includes('sample-expired-nonce'),false);
 assert.equal((await readMaintenanceCheckpoint(f.db,Math.floor(hourMs/1000)+6000)).state,'stale');
 const before=status.data.checkedAt;
 const again=[];
 await worker.scheduled({scheduledTime:hourMs+5*60000},{DB:f.db},{waitUntil:p=>again.push(p)});
 await Promise.all(again);
 assert.equal((await readMaintenanceCheckpoint(f.db,Math.floor(hourMs/1000)+60)).data.checkedAt,before);
});
test('hourly report fails closed without optional D1 migration; five-minute cleanup still works',async t=>{
 const f=await fixture(t);
 assert.equal(shouldWriteMaintenanceCheckpoint(Date.UTC(2026,9,25,12)),true);
 assert.equal(shouldWriteMaintenanceCheckpoint(Date.UTC(2026,9,25,12,5)),false);
 const tasks=[];
 await worker.scheduled({scheduledTime:Date.UTC(2026,9,25,12)},
  {DB:f.db},{waitUntil:p=>tasks.push(p)});
 await Promise.all(tasks);
 assert.equal((await readMaintenanceCheckpoint(f.db)).state,'not_connected');
 assert.equal(await recordMaintenanceCheckpoint(f.db,Array(4).fill({meta:{changes:0}}),
  {success:true,deleted:0},Date.UTC(2026,9,25,13)),false);
 assert.equal((await f.dispatch(new Request(origin+'/health'))).status,200);
});

test('scheduled six-hour R2 audit writes one sampled prefix checkpoint without touching objects',async t=>{
 const f=await fixture(t);
 for(const name of ['0003_security_rollups.sql','0004_maintenance_status.sql','0005_r2_audit.sql']){
   const migration=await readFile(new URL('../migrations/'+name,import.meta.url),'utf8');
   await f.db.batch(migration.replace(/--[^\n]*/g,'').split(';')
     .map(s=>s.trim()).filter(Boolean).map(sql=>f.db.prepare(sql)));
 }
 // Simulate a scheduler timestamp inside the matching UTC minute, not
 // exactly on the millisecond boundary. Production used to skip this.
 const scheduledMs=Date.UTC(2026,9,26,6,0,17,250);
 let listing=0;
 const fakeR2={list:async options=>{
  listing++;
  assert.deepEqual(options,{prefix:'transfers/',limit:500});
  return {objects:[{key:'transfers/PRIVATE/SECRET/part',
    uploaded:new Date(scheduledMs-9*86400000),size:4096}],truncated:false};
 }};
 const wait=[];
 await worker.scheduled({scheduledTime:scheduledMs},{DB:f.db,FILES:fakeR2},
   {waitUntil:p=>wait.push(p)});
 await Promise.all(wait);
 assert.equal(listing,1);
 const t0=Math.floor(scheduledMs/1000);
 const status=await readMaintenanceCheckpoint(f.db,t0+60);
 assert.equal(status.state,'ready');
 assert.equal(status.data.r2Audit.state,'ready');
 assert.equal(status.data.r2Audit.data.sampledObjects,1);
 assert.equal(status.data.r2Audit.data.sampledBytes,4096);
 assert.equal(status.data.r2Audit.data.overdueObjects,1);
 assert.equal(status.data.r2Audit.data.oldestAgeSeconds,9*86400);
 assert.equal(JSON.stringify(status).includes('PRIVATE'),false);
 assert.equal(JSON.stringify(status).includes('SECRET'),false);
 const fiveLater=[];
 await worker.scheduled({scheduledTime:scheduledMs+5*60000},
   {DB:f.db,FILES:fakeR2},{waitUntil:p=>fiveLater.push(p)});
 await Promise.all(fiveLater);
 assert.equal(listing,1,'never scan R2 every five minutes');
});
test('optional migration 0005 absent does not break hourly Relay cleanup',async t=>{
 const f=await fixture(t);
 for(const name of ['0003_security_rollups.sql','0004_maintenance_status.sql']){
  const migration=await readFile(new URL('../migrations/'+name,import.meta.url),'utf8');
  await f.db.batch(migration.replace(/--[^\n]*/g,'').split(';')
    .map(s=>s.trim()).filter(Boolean).map(sql=>f.db.prepare(sql)));
 }
 const jobs=[];
 const ms=Date.UTC(2026,9,26,12,0);
 await worker.scheduled({scheduledTime:ms},{DB:f.db,FILES:{list:async()=>({
  objects:[],truncated:false
 })}},{waitUntil:p=>jobs.push(p)});
 await Promise.all(jobs);
 const status=await readMaintenanceCheckpoint(f.db,ms/1000+60);
 assert.equal(status.state,'ready');
 assert.equal(status.data.r2Audit.state,'not_connected');
 assert.equal((await f.dispatch(new Request(origin+'/health'))).status,200);
});
