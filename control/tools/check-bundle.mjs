import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { configuration } from './configure.mjs';

// Synthetic D1 ID for a local Wrangler --dry-run; does not touch production.
const folder = await mkdtemp(join(tmpdir(), 'mavka-control-bundle-'));
try {
  const config = configuration({ MAVKA_CONTROL_D1_DATABASE_ID: crypto.randomUUID() });
  config.main = fileURLToPath(new URL('../src/worker.js', import.meta.url));
  config.d1_databases[0].migrations_dir = fileURLToPath(new URL('../migrations', import.meta.url));
  const path = join(folder, 'wrangler.json');
  await writeFile(path, JSON.stringify(config));
  const cli = fileURLToPath(new URL('../node_modules/wrangler/bin/wrangler.js', import.meta.url));
  const run = spawnSync(process.execPath,
    [cli, 'deploy', '--dry-run', '--config', path, '--outdir', join(folder, 'bundle')],
    { stdio: 'inherit', env: { ...process.env, WRANGLER_SEND_METRICS: 'false' } });
  process.exitCode = run.status ?? 1;
} finally {
  await rm(folder, { recursive: true, force: true });
}
