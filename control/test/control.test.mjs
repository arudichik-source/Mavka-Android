import test from 'node:test';
import assert from 'node:assert/strict';
import { configuration } from '../tools/configure.mjs';
import { accessSettings, verifyAccess } from '../src/access.js';
import { readAnalytics, analyticsConfigured } from '../src/analytics.js';
import control from '../src/worker.js';

const domain = 'https://mavka-test.cloudflareaccess.com';
const audience = '0123456789abcdef0123456789abcdef';
const email = 'control-test@example.invalid';
const env = { ACCESS_TEAM_DOMAIN: domain, ACCESS_AUD: audience, ADMIN_EMAIL: email };
const url = 'https://mavka-server-control.example.workers.dev/';
const enc = object => Buffer.from(JSON.stringify(object)).toString('base64url');
let counter = 0;

async function signedToken(claims = {}) {
  const pair = await crypto.subtle.generateKey({ name: 'RSASSA-PKCS1-v1_5',
    modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]),
    hash: 'SHA-256' }, true, ['sign', 'verify']);
  const jwk = await crypto.subtle.exportKey('jwk', pair.publicKey);
  jwk.kid = 'test-key-' + (++counter); jwk.alg = 'RS256'; jwk.use = 'sig';
  const now = Math.floor(Date.now() / 1000);
  const payload = { iss: domain, aud: [audience], iat: now - 10,
    exp: now + 3600, email, ...claims };
  const prefix = enc({ alg: 'RS256', kid: jwk.kid }) + '.' + enc(payload);
  const signature = Buffer.from(await crypto.subtle.sign('RSASSA-PKCS1-v1_5',
    pair.privateKey, new TextEncoder().encode(prefix))).toString('base64url');
  return { token: prefix + '.' + signature, jwk };
}
function request(token, path = '/') {
  return new Request(url.replace(/\/$/, '') + path, { headers:
    token ? { 'Cf-Access-Jwt-Assertion': token } : {} });
}
function stubWebFetch(jwk, graphqlAnswer) {
  const original = globalThis.fetch;
  globalThis.fetch = async (input, options) => {
    if (String(input).endsWith('/cdn-cgi/access/certs'))
      return Response.json({ keys: [jwk] });
    if (String(input).endsWith('/graphql') && graphqlAnswer)
      return graphqlAnswer(options);
    throw new Error('unexpected_request');
  };
  return () => { globalThis.fetch = original; };
}

test('configuration isolates D1 and fails closed without a real database ID', () => {
  assert.throws(() => configuration({}));
  const cfg = configuration({ MAVKA_CONTROL_D1_DATABASE_ID: crypto.randomUUID() });
  assert.equal(cfg.name, 'mavka-server-control');
  assert.deepEqual(cfg.d1_databases.map(d => d.database_name), ['mavka-control']);
  assert.equal(cfg.d1_databases[0].binding, 'CONTROL_DB');
  assert.equal(cfg.preview_urls, false);
  assert.equal(cfg.keep_vars, true);
  assert.equal(cfg.observability.redact_query_string, true);
  assert.equal(cfg.triggers, undefined);
  assert.equal(Object.hasOwn(cfg, 'vars'), false);
  assert.equal(JSON.stringify(cfg).includes('mavka-messages'), false);
});

test('unconfigured and unauthenticated control Worker never exposes HTML or APIs', async () => {
  assert.equal(accessSettings({}), null);
  assert.equal((await control.fetch(request(null), {})).status, 503);
  for (const path of ['/', '/api/summary', '/api/history', '/api/about']) {
    const res = await control.fetch(request(null, path), env);
    assert.equal(res.status, 403);
    assert.equal((await res.json()).error, 'access_denied');
  }
  assert.equal(analyticsConfigured({}), false);
  assert.equal((await readAnalytics({})).state, 'unconfigured');
});

test('Access signature, audience, email, expiry and allowed method enforced', async () => {
  const { token, jwk } = await signedToken();
  const undo = stubWebFetch(jwk);
  try {
    assert.deepEqual(await verifyAccess(request(token), env), { email });
    const home = await control.fetch(request(token), env);
    assert.equal(home.status, 200);
    assert.ok((await home.text()).includes('MaVka Server Control'));
    assert.ok(home.headers.get('content-security-policy').includes('nonce-'));
    const about = await control.fetch(request(token, '/api/about'), env);
    assert.equal((await about.json()).mode, 'read_only');
    const summary = await control.fetch(request(token, '/api/summary'), env);
    assert.equal((await summary.json()).state, 'unconfigured');
    assert.equal((await control.fetch(new Request(url, {method:'POST',
      headers:{'Cf-Access-Jwt-Assertion':token} }),env)).status, 404);
    const altered = token.slice(0, -3) + 'ABC';
    assert.equal((await verifyAccess(request(altered), env)).status, 403);
    assert.equal((await verifyAccess(request(token), { ...env, ADMIN_EMAIL:'other@example.invalid' })).status, 403);
    assert.equal((await verifyAccess(request(token), { ...env, ACCESS_AUD:'f'.repeat(32) })).status, 403);
  } finally { undo(); }
});

test('Cloudflare analytics return partial rather than fabricated zero on dataset errors', async () => {
  const aenv = { CF_ACCOUNT_ID: 'a'.repeat(32), CF_ANALYTICS_TOKEN: 'token-at-least-twenty-chars' };
  const old = globalThis.fetch;
  globalThis.fetch = async (_, options) => {
    const body = JSON.parse(options.body);
    let account = {};
    if (body.query.includes('workersInvocationsAdaptive'))
      account = { workersInvocationsAdaptive: [{ sum: { requests: 42, errors: 1 } }] };
    else if (body.query.includes('d1AnalyticsAdaptiveGroups'))
      account = { d1AnalyticsAdaptiveGroups: [{ sum: { rowsRead: 123, rowsWritten: 5 } }] };
    else if (body.query.includes('r2StorageAdaptiveGroups'))
      account = { r2StorageAdaptiveGroups: [{max: {payloadSize: 1024, metadataSize: 12, objectCount: 3},
        dimensions: {datetime:'2026-10-23T10:00:00Z'}}] };
    else return new Response('unavailable', { status: 503 });
    return Response.json({ data: { viewer: { accounts: [account] } } });
  };
  try {
    const data = await readAnalytics(aenv, new Date('2026-10-23T10:00:00Z'));
    assert.equal(data.state, 'partial');
    assert.equal(data.workers.requestsToday, 42);
    assert.equal(data.d1.rowsReadToday, 123);
    assert.equal(data.r2.storage.bytes, 1024);
    assert.equal(data.r2.operations, null);
    assert.equal(data.limits.workersRequestsPerDay, 100000);
    assert.deepEqual(data.errors, ['r2Operations_unavailable']);
  } finally { globalThis.fetch = old; }
});
