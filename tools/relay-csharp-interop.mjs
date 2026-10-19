import { readFile, writeFile } from 'node:fs/promises';
import { strict as assert } from 'node:assert';
import { verifyDelivery, fingerprint, signingInput, base64url } from '../relay/src/protocol.js';
const f = JSON.parse(await readFile(process.argv[2], 'utf8'));
await verifyDelivery(f.delivery, 'https://mavka-relay.arudichik.workers.dev', f.trustedKey, f.recipient);
const tamper = structuredClone(f.delivery); tamper.packet.requestBody += ' ';
await assert.rejects(() => verifyDelivery(tamper, 'https://mavka-relay.arudichik.workers.dev', f.trustedKey, f.recipient));
const pair = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
const exported = await crypto.subtle.exportKey('jwk', pair.publicKey);
const trustedKey = { kty: exported.kty, crv: exported.crv, x: exported.x, y: exported.y };
const delivery = structuredClone(f.delivery);
delivery.packet.time = String(Math.floor(Date.now() / 1000));
delivery.packet.signature = base64url(new Uint8Array(await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, pair.privateKey,
  await signingInput('POST', 'https://mavka-relay.arudichik.workers.dev/v1/messages', f.sender, delivery.packet.time,
    delivery.packet.nonce, delivery.packet.requestBody))));
assert.equal(await fingerprint(f.sender, JSON.parse(delivery.packet.requestBody)), delivery.payloadSha256);
await writeFile(process.argv[3], JSON.stringify({ trustedKey, sender: f.sender, recipient: f.recipient, delivery }));
console.log('C# -> server JavaScript proof verification and JavaScript -> C# fixture: PASS (synthetic only).');
