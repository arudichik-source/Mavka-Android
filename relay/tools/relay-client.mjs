// Local integration/QA helper. Production apps must use Windows/Android key stores.
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { signedRequest, id } from '../src/protocol.js';

async function main() {
  const [command, ...args] = process.argv.slice(2);
  if (command === 'keygen' && args.length === 1) {
    const folder = resolve(args[0]);
    // Refuse an existing directory, so a mistyped command cannot replace an identity.
    await mkdir(folder, { mode: 0o700 });
    const signing = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
    const encryption = await crypto.subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']);
    const deviceId = crypto.randomUUID();
    const publicIdentity = { deviceId, publicSigningKey: await crypto.subtle.exportKey('jwk', signing.publicKey),
      publicEncryptionKey: await crypto.subtle.exportKey('jwk', encryption.publicKey) };
    await writeFile(join(folder, 'identity.private.json'), JSON.stringify({ ...publicIdentity,
      privateSigningKey: await crypto.subtle.exportKey('jwk', signing.privateKey),
      privateEncryptionKey: await crypto.subtle.exportKey('jwk', encryption.privateKey) }, null, 2), { flag: 'wx', mode: 0o600 });
    await writeFile(join(folder, 'enrollment.public.json'), JSON.stringify(publicIdentity, null, 2), { flag: 'wx', mode: 0o600 });
    await writeFile(join(folder, 'bootstrap.public.json'), JSON.stringify({ publicEncryptionKey: publicIdentity.publicEncryptionKey }, null, 2), { flag: 'wx', mode: 0o600 });
    console.log('Created a local test identity and public enrollment/bootstrap files. Keep the private file outside Git.');
    return;
  }
  if (command === 'request' && (args.length === 3 || args.length === 4)) {
    const [identityPath, method, address, bodyPath] = args;
    const url = new URL(address);
    if (url.protocol !== 'https:' || url.username || url.password || url.hash || !['GET', 'POST'].includes(method))
      throw new Error('HTTPS GET/POST required.');
    const identity = JSON.parse(await readFile(identityPath, 'utf8')); id(identity.deviceId);
    const key = await crypto.subtle.importKey('jwk', identity.privateSigningKey,
      { name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign']);
    const body = bodyPath ? await readFile(bodyPath, 'utf8') : (method === 'POST' ? '{}' : '');
    if (method === 'GET' && bodyPath) throw new Error('GET cannot have a body.');
    const request = await signedRequest(url, method, body, identity.deviceId, key);
    const result = await fetch(request, { redirect: 'error', signal: AbortSignal.timeout(30000) });
    console.log('HTTP', result.status); console.log(await result.text());
    if (!result.ok) process.exitCode = 1;
    return;
  }
  console.error('Usage: node tools/relay-client.mjs keygen <new-directory>\n       node tools/relay-client.mjs request <identity.private.json> GET|POST <https-url> [body.json]');
  process.exitCode = 1;
}
main().catch(() => { console.error('Command failed. Check file paths, key format, URL and server response.'); process.exitCode = 1; });
