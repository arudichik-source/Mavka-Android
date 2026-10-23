import { writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

export function configuration(env) {
  const id = env.MAVKA_CONTROL_D1_DATABASE_ID;
  if (typeof id !== 'string' ||
      !/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(id) ||
      id === '00000000-0000-0000-0000-000000000000') {
    throw new Error('Create a separate D1 database mavka-control and set MAVKA_CONTROL_D1_DATABASE_ID.');
  }
  return {
    $schema: './node_modules/wrangler/config-schema.json',
    name: 'mavka-server-control', main: 'src/worker.js',
    compatibility_date: '2026-10-23',
    workers_dev: true, preview_urls: false,
    observability: {
      enabled: true,
      redact_query_string: true,
      logs: { enabled: true, invocation_logs: true, persist: true, head_sampling_rate: 1 },
      traces: { enabled: false },
      issues: { enabled: false },
    },
    d1_databases: [{
      binding: 'CONTROL_DB',
      database_name: 'mavka-control',
      database_id: id,
      migrations_dir: 'migrations',
    }],
  };
}
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  try {
    const config = configuration(process.env);
    await writeFile(new URL('../wrangler.json', import.meta.url),
      JSON.stringify(config, null, 2) + '\n');
    console.log('Created a separate control/wrangler.json; no remote changes performed.');
  } catch {
    console.error('Control Worker configuration refused: missing valid separate D1 database ID.');
    process.exitCode = 1;
  }
}
