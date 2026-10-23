import { writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { id, publicKey } from '../src/protocol.js';

export async function configuration(env) {
  if (!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(env.MAVKA_D1_DATABASE_ID ?? '') ||
      env.MAVKA_D1_DATABASE_ID === '00000000-0000-0000-0000-000000000000')
    throw new Error('Set MAVKA_D1_DATABASE_ID to the existing mavka-messages database UUID.');
  id(env.MAVKA_ADMIN_DEVICE_ID);
  const root = await publicKey(JSON.parse(env.MAVKA_ADMIN_SIGNING_JWK ?? '{}'));
  return {
    $schema: './node_modules/wrangler/config-schema.json', name: 'mavka-relay',
    main: 'src/worker.js', compatibility_date: '2026-10-01',
    workers_dev: true, preview_urls: false,
    observability: { enabled: false },
    d1_databases: [{ binding: 'DB', database_name: 'mavka-messages',
      database_id: env.MAVKA_D1_DATABASE_ID, migrations_dir: 'migrations' }],
    vars: { RELAY_ADMIN_DEVICE_ID: env.MAVKA_ADMIN_DEVICE_ID, RELAY_ADMIN_SIGNING_KEY: root.json },
    r2_buckets: [{ binding: 'FILES', bucket_name: 'mavka-private-files' }],
    triggers: { crons: ['*/5 * * * *'] },
  };
}
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  try {
    const config = await configuration(process.env);
    await writeFile(new URL('../wrangler.json', import.meta.url), JSON.stringify(config, null, 2) + '\n');
    console.log('Prepared local wrangler.json. No remote changes made.');
  } catch {
    console.error('Configuration blocked: provide a valid existing D1 UUID, admin device ID and public P-256 signing JWK. Never supply a private JWK.');
    process.exitCode = 1;
  }
}
