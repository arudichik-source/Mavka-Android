import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { configuration } from './configure.mjs';

// Synthetic configuration only. Does not read or overwrite a user's wrangler.json.
const folder = await mkdtemp(join(tmpdir(), 'mavka-relay-bundle-'));
try {
  const key = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
  const config = await configuration({ MAVKA_D1_DATABASE_ID: crypto.randomUUID(),
    MAVKA_ADMIN_DEVICE_ID: crypto.randomUUID(),
    MAVKA_ADMIN_SIGNING_JWK: JSON.stringify(await crypto.subtle.exportKey('jwk', key.publicKey)) });
  config.main = fileURLToPath(new URL('../src/worker.js', import.meta.url));
  config.d1_databases[0].migrations_dir = fileURLToPath(new URL('../migrations', import.meta.url));
  const path = join(folder, 'wrangler.json'); await writeFile(path, JSON.stringify(config));
  const result = spawnSync(process.execPath, [fileURLToPath(new URL('../node_modules/wrangler/bin/wrangler.js', import.meta.url)),
    'deploy', '--dry-run', '--config', path, '--outdir', join(folder, 'bundle')],
  { stdio: 'inherit', env: { ...process.env, WRANGLER_SEND_METRICS: 'false' } });
  process.exitCode = result.status ?? 1;
} finally { await rm(folder, { recursive: true, force: true }); }
