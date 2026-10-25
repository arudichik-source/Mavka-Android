import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { Miniflare, convertV4MiniflareOptions } from 'miniflare';
import { publicKey, decodeUrl, signingInput } from '../src/protocol.js';

test('Windows PowerShell 5.1 CNG keys: non-exportable, stable identity, Web Crypto signature compatibility',
  { skip: process.platform !== 'win32' }, async () => {
    const folder = await mkdtemp(join(tmpdir(), 'mavka-relay-windows-'));
    try {
      const output = join(folder, 'public-fixture.json');
      const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-File',
        fileURLToPath(new URL('../tools/Initialize-RelayAdmin.ps1', import.meta.url)), '-SelfTest', '-OutputFile', output],
      { encoding: 'utf8', timeout: 45000 });
      assert.equal(result.status, 0, result.stderr || result.stdout);
      const fixture = JSON.parse(await readFile(output, 'utf8'));
      assert.equal(fixture.format, 'MAVKA_RELAY_WINDOWS_QA_V1');
      for (const flag of ['privateExportDenied', 'userScoped', 'identityReused', 'wrongDatabaseRejected'])
        assert.equal(fixture[flag], true, flag);
      assert.equal(Object.hasOwn(fixture.publicSigningKey, 'd'), false);
      assert.equal(Object.hasOwn(fixture.publicEncryptionKey, 'd'), false);
      const signing = await publicKey(fixture.publicSigningKey);
      await publicKey(fixture.publicEncryptionKey, 'encryption');
      const valid = await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, signing.key,
        decodeUrl(fixture.signature, 64), await signingInput('POST',
          'https://mavka-relay.arudichik.workers.dev/v1/bootstrap', fixture.deviceId,
          fixture.time, fixture.nonce, fixture.rawBody));
      assert.equal(valid, true);
      const invalid = await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, signing.key,
        decodeUrl(fixture.signature, 64), await signingInput('POST',
          'https://mavka-relay.arudichik.workers.dev/v1/bootstrap', fixture.deviceId,
          fixture.time, fixture.nonce, fixture.rawBody + ' '));
      assert.equal(invalid, false);
      const origin = 'https://mavka-relay.arudichik.workers.dev';
      const mf = new Miniflare(convertV4MiniflareOptions({ cf: { country: 'UA' }, telemetry: { enabled: false },
        workers: [{ name: 'relay', modulesRoot: fileURLToPath(new URL('../', import.meta.url)),
          modules: ['worker.js', 'protocol.js', 'security-audit.js', 'security-rollups.js', 'maintenance.js'].map(name => ({ type: 'ESModule', path: fileURLToPath(new URL('../src/' + name, import.meta.url)) })),
          compatibilityDate: '2026-10-01', d1Databases: { DB: 'windows-relay-tests' },
          bindings: { RELAY_ADMIN_DEVICE_ID: fixture.deviceId, RELAY_ADMIN_SIGNING_KEY: JSON.stringify(fixture.publicSigningKey) } }] }));
      try {
        const db = await mf.getD1Database('DB');
        const schema = await readFile(new URL('../migrations/0001_relay.sql', import.meta.url), 'utf8');
        await db.batch(schema.replace(/--[^\n]*/g, '').split(';').map(s => s.trim()).filter(Boolean).map(sql => db.prepare(sql)));
        const response = await mf.dispatchFetch(origin + '/v1/bootstrap', { method: 'POST', body: fixture.rawBody,
          cf: { country: 'UA' }, headers: { 'Content-Type': 'application/json',
            'X-Mavka-Device': fixture.deviceId, 'X-Mavka-Time': fixture.time,
            'X-Mavka-Nonce': fixture.nonce, 'X-Mavka-Signature': fixture.signature } });
        assert.equal(response.status, 201, await response.text());
        assert.equal((await db.prepare('SELECT status FROM devices WHERE device_id = ?').bind(fixture.deviceId).first()).status, 'active');
      } finally { await mf.dispose(); }
    } finally { await rm(folder, { recursive: true, force: true }); }
  });
