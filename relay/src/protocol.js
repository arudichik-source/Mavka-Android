// Shared wire-format helpers. This module contains no private keys or storage.
export const VERSION = '1.0.0';
export const MAX_BODY = 49152;
export const MAX_TTL = 7 * 86400;
export const ID_RE = /^[A-Za-z0-9][A-Za-z0-9_-]{15,79}$/;
export const encoder = new TextEncoder();

export class ApiError extends Error {
  constructor(status, code) { super(code); this.status = status; this.code = code; }
}
export function requireValue(condition, status = 400, code = 'invalid_request') {
  if (!condition) throw new ApiError(status, code);
}
export function object(value, names) {
  requireValue(value !== null && typeof value === 'object' && !Array.isArray(value));
  requireValue(Object.keys(value).length === names.length && names.every(n => Object.hasOwn(value, n)));
  return value;
}
export function id(value) {
  requireValue(typeof value === 'string' && ID_RE.test(value)); return value;
}
export function base64url(bytes) {
  return btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
}
export function decodeUrl(value, length) {
  requireValue(typeof value === 'string' && /^[A-Za-z0-9_-]+$/.test(value) && value.length <= 512);
  let bytes;
  try { bytes = Uint8Array.from(atob(value.replaceAll('-', '+').replaceAll('_', '/') + '='.repeat((4 - value.length % 4) % 4)), c => c.charCodeAt(0)); }
  catch { throw new ApiError(400, 'invalid_base64url'); }
  requireValue(bytes.length === length && base64url(bytes) === value);
  return bytes;
}
function decodeBase64(value, min, max) {
  requireValue(typeof value === 'string' && value.length <= Math.ceil(max / 3) * 4 && /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(value));
  const bytes = Uint8Array.from(atob(value), c => c.charCodeAt(0));
  requireValue(bytes.length >= min && bytes.length <= max && btoa(String.fromCharCode(...bytes)) === value);
  return value;
}
export async function sha256(value) {
  const bytes = typeof value === 'string' ? encoder.encode(value) : value;
  return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join('');
}
export async function publicKey(value, usage = 'signing') {
  requireValue(value && typeof value === 'object' && !Array.isArray(value) && !Object.hasOwn(value, 'd'));
  requireValue(value.kty === 'EC' && value.crv === 'P-256');
  decodeUrl(value.x, 32); decodeUrl(value.y, 32);
  const jwk = { kty: 'EC', crv: 'P-256', x: value.x, y: value.y };
  try {
    const key = await crypto.subtle.importKey('jwk', jwk,
      { name: usage === 'signing' ? 'ECDSA' : 'ECDH', namedCurve: 'P-256' }, false,
      usage === 'signing' ? ['verify'] : []);
    return { jwk, key, json: JSON.stringify(jwk) };
  } catch { throw new ApiError(400, 'invalid_public_key'); }
}
export function encryptedEnvelope(value) {
  object(value, ['format', 'algorithm', 'keyId', 'nonce', 'ciphertext', 'tag']);
  requireValue(value.format === 'MAVKA_ENCRYPTED_V1' && value.algorithm === 'AES-256-GCM');
  requireValue(typeof value.keyId === 'string' && /^[a-fA-F0-9]{16}$/.test(value.keyId));
  decodeBase64(value.nonce, 12, 12); decodeBase64(value.tag, 16, 16);
  decodeBase64(value.ciphertext, 1, 32768);
  return { format: value.format, algorithm: value.algorithm, keyId: value.keyId,
    nonce: value.nonce, ciphertext: value.ciphertext, tag: value.tag };
}
export function messageBody(value) {
  object(value, ['messageId', 'recipientDeviceId', 'expiresAt', 'payload']);
  id(value.messageId); id(value.recipientDeviceId);
  requireValue(Number.isSafeInteger(value.expiresAt) && value.expiresAt > 0);
  return { messageId: value.messageId, recipientDeviceId: value.recipientDeviceId,
    expiresAt: value.expiresAt, payload: encryptedEnvelope(value.payload) };
}
export async function fingerprint(senderDeviceId, body) {
  return sha256(JSON.stringify({ senderDeviceId, ...messageBody(body) }));
}
export async function signingInput(method, url, deviceId, time, nonce, body) {
  const target = new URL(url);
  return encoder.encode(['MAVKA_RELAY_REQUEST_V1', target.origin, method,
    target.pathname + target.search, deviceId, String(time), nonce, await sha256(body)].join('\n'));
}
export async function signedRequest(url, method, rawBody, deviceId, privateKey, options = {}) {
  const time = options.time ?? Math.floor(Date.now() / 1000);
  const nonce = options.nonce ?? base64url(crypto.getRandomValues(new Uint8Array(16)));
  const signature = base64url(new Uint8Array(await crypto.subtle.sign(
    { name: 'ECDSA', hash: 'SHA-256' }, privateKey,
    await signingInput(method, url, deviceId, time, nonce, rawBody))));
  return new Request(url, { method, headers: {
    'Content-Type': 'application/json', 'X-Mavka-Device': deviceId, 'X-Mavka-Time': String(time),
    'X-Mavka-Nonce': nonce, 'X-Mavka-Signature': signature,
  }, ...(method === 'GET' ? {} : { body: rawBody }) });
}
// Call before decrypting/persisting and ACKing. The sender key must come from
// trusted pairing, NEVER from a key supplied by the Relay response itself.
export async function verifyDelivery(delivery, origin, trustedSenderJwk, recipientDeviceId) {
  const packet = delivery.packet;
  object(packet, ['format', 'senderDeviceId', 'time', 'nonce', 'signature', 'requestBody']);
  requireValue(packet.format === 'MAVKA_RELAY_STORED_V1');
  id(packet.senderDeviceId); decodeUrl(packet.nonce, 16);
  requireValue(typeof packet.time === 'string' && /^\d{10}$/.test(packet.time));
  requireValue(typeof packet.requestBody === 'string' && encoder.encode(packet.requestBody).length <= MAX_BODY);
  const trusted = await publicKey(trustedSenderJwk);
  const valid = await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, trusted.key,
    decodeUrl(packet.signature, 64), await signingInput('POST', new URL('/v1/messages', origin),
      packet.senderDeviceId, packet.time, packet.nonce, packet.requestBody));
  requireValue(valid, 400, 'invalid_delivery_signature');
  const body = messageBody(JSON.parse(packet.requestBody));
  requireValue(body.messageId === delivery.messageId && body.recipientDeviceId === recipientDeviceId &&
    delivery.senderDeviceId === packet.senderDeviceId && body.expiresAt === delivery.expiresAt);
  requireValue(await fingerprint(packet.senderDeviceId, body) === delivery.payloadSha256);
  requireValue(body.expiresAt > Math.floor(Date.now() / 1000), 410, 'message_expired');
  return body.payload;
}
