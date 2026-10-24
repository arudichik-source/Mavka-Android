import test from 'node:test';
import assert from 'node:assert/strict';
import { configuration } from '../tools/configure.mjs';
import { accessSettings, verifyAccess } from '../src/access.js';
import { readAnalytics, analyticsConfigured, safeErrorKind, LIMITS } from '../src/analytics.js';
import { html } from '../src/ui.js';
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

test('Worker-level Access ctx.access identity works without JWT and never bypasses checks', async () => {
  const makeCtx = (aud, identity) => ({
    access: { aud, getIdentity: async () => identity },
  });
  const valid = makeCtx(audience, { email: email.toUpperCase() });
  assert.deepEqual(await verifyAccess(request(null), env, valid), { email });
  const home = await control.fetch(request(null), env, valid);
  assert.equal(home.status, 200);
  assert.match(await home.text(), /MaVka Server Control/);
  const status = await control.fetch(request(null, '/api/about'), env, valid);
  assert.equal(status.status, 200);
  assert.equal((await status.json()).mode, 'read_only');

  const unknown = makeCtx(audience, { email: 'attacker@example.invalid' });
  const foreign = makeCtx('f'.repeat(32), { email });
  const invalid = { access: { aud: audience, getIdentity: async () => { throw Error('failed'); } } };
  const noIdentity = { access: { aud: audience } };
  for (const ctx of [unknown, foreign, invalid, noIdentity, { access: {} }]) {
    assert.equal((await verifyAccess(request(null), env, ctx)).status, 403);
    assert.equal((await control.fetch(request(null), env, ctx)).status, 403);
  }
  assert.equal((await control.fetch(request(null), env)).status, 403);
  assert.equal((await control.fetch(request(null), {}, valid)).status, 503);
  // The verified Access context is not enough to bypass strict URL routing.
  const withQuery = await control.fetch(request(null, '/?utm_source=chatgpt.com'), env, valid);
  assert.equal(withQuery.status, 404);
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

test('all four GraphQL queries use Cloudflare lowercase string scalar and return real mock metrics', async () => {
  const safeEnv = { CF_ACCOUNT_ID: 'a'.repeat(32), CF_ANALYTICS_TOKEN: 'test-token-not-real-long-enough' };
  const observed = [];
  const original = globalThis.fetch;
  globalThis.fetch = async (_endpoint, options) => {
    const payload = JSON.parse(options.body);
    observed.push(payload.query);
    assert.match(payload.query, /\$account: string!/);
    assert.doesNotMatch(payload.query, /\$account: String!/);
    assert.equal(payload.variables.account, safeEnv.CF_ACCOUNT_ID);
    assert.equal(options.headers.Authorization, 'Bearer ' + safeEnv.CF_ANALYTICS_TOKEN);
    if (payload.query.includes('workersInvocationsAdaptive'))
      return Response.json({ data: { viewer: { accounts: [{
        workersInvocationsAdaptive: [{sum: {requests: 117, errors: 2}}]
      }] } } });
    if (payload.query.includes('d1AnalyticsAdaptiveGroups'))
      return Response.json({ data: { viewer: { accounts: [{
        d1AnalyticsAdaptiveGroups: [{sum: {rowsRead: 201, rowsWritten: 8}}]
      }] } } });
    if (payload.query.includes('r2StorageAdaptiveGroups'))
      return Response.json({ data: { viewer: { accounts: [{
        r2StorageAdaptiveGroups: [{max: {payloadSize: 4096, metadataSize: 128, objectCount: 3},
          dimensions: {datetime: '2026-10-24T06:00:00Z'}}]
      }] } } });
    return Response.json({ data: { viewer: { accounts: [{
      r2OperationsAdaptiveGroups: [{sum: {requests: 9}, dimensions: {actionType: 'GetObject'}}]
    }] } } });
  };
  try {
    const data = await readAnalytics(safeEnv, new Date('2026-10-24T09:00:00Z'));
    assert.equal(observed.length, 4);
    assert.equal(data.state, 'ready');
    assert.deepEqual(data.errors, []);
    assert.deepEqual(data.diagnostics, {
      workers: 'ok', d1: 'ok', r2Storage: 'ok', r2Operations: 'ok',
    });
    assert.equal(data.workers.requestsToday, 117);
    assert.equal(data.d1.rowsWrittenToday, 8);
    assert.equal(data.r2.storage.bytes, 4096);
    assert.equal(data.r2.operations.requestsThisMonth, 9);
  } finally { globalThis.fetch = original; }
});

test('GraphQL schema errors get sanitized classification without raw backend messages', async () => {
  const e = { CF_ACCOUNT_ID: 'b'.repeat(32), CF_ANALYTICS_TOKEN: 'test-token-not-real-long-enough' };
  const old = globalThis.fetch;
  globalThis.fetch = async () => Response.json({
    errors: [{ message: 'Unknown type \\"String\\"; private diagnostic TOKEN_XYZ' }],
  });
  try {
    const data = await readAnalytics(e, new Date('2026-10-24T09:00:00Z'));
    assert.equal(data.state, 'unavailable');
    assert.deepEqual(data.diagnostics, {
      workers: 'query_invalid', d1: 'query_invalid',
      r2Storage: 'query_invalid', r2Operations: 'query_invalid',
    });
    assert.equal(JSON.stringify(data).includes('TOKEN_XYZ'), false);
  } finally { globalThis.fetch = old; }
});

test('5 GB is the owner-defined R2 monitoring threshold, not the Cloudflare GB-month allowance', () => {
  assert.equal(LIMITS.r2MonitoringBytes, 5_000_000_000);
  assert.equal(LIMITS.r2StandardGbMonths, 10);
  assert.match(html, /5 ГБ \\(контрольний поріг\\)/);
  assert.match(html, /r2MonitoringBytes/);
  assert.doesNotMatch(html, /10 ГБ \\(орієнтир\\)/);
});

test('unexpected provider/network errors are classified without disclosing secrets', async () => {
  assert.equal(safeErrorKind(new TypeError('token secret should not show')), 'fetch_or_runtime_type_error');
  assert.equal(safeErrorKind({ name: 'TimeoutError', message: 'secret' }), 'timeout');
  assert.equal(safeErrorKind(new ReferenceError('secret runtime data')), 'runtime_reference_error');
  const env = { CF_ACCOUNT_ID: 'a'.repeat(32), CF_ANALYTICS_TOKEN: 'long-test-token-never-real' };
  const prev = globalThis.fetch;
  globalThis.fetch = async () => { throw new TypeError('do not leak this sensitive host or token'); };
  try {
    const data = await readAnalytics(env, new Date('2026-10-24T09:00:00Z'));
    assert.equal(data.state, 'unavailable');
    assert.deepEqual(data.diagnostics, {
      workers: 'fetch_or_runtime_type_error', d1: 'fetch_or_runtime_type_error',
      r2Storage: 'fetch_or_runtime_type_error', r2Operations: 'fetch_or_runtime_type_error',
    });
    assert.equal(JSON.stringify(data).includes('sensitive host'), false);
  } finally { globalThis.fetch = prev; }
});
