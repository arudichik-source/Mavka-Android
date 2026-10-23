// Only Cloudflare Access-validated administrators may reach *any* route.
// Fail closed unless all required bindings are configured. No tokens or
// identity attributes are ever written to logs or returned to the browser.
const text = new TextDecoder();
const jwksCache = new Map();

const decode = value => {
  if (!/^[A-Za-z0-9_-]+$/.test(value)) throw new Error('bad_encoding');
  const raw = atob(value.replaceAll('-', '+').replaceAll('_', '/') +
    '='.repeat((4 - value.length % 4) % 4));
  return Uint8Array.from(raw, c => c.charCodeAt(0));
};
function parsePart(encoded) {
  return JSON.parse(text.decode(decode(encoded)));
}

export function accessSettings(env) {
  const domain = env.ACCESS_TEAM_DOMAIN;
  const audience = env.ACCESS_AUD;
  const adminEmail = env.ADMIN_EMAIL;
  if (typeof domain !== 'string' ||
      !/^https:\/\/[a-z0-9-]+\.cloudflareaccess\.com$/.test(domain) ||
      typeof audience !== 'string' || !/^[a-f0-9]{32,64}$/i.test(audience) ||
      typeof adminEmail !== 'string' ||
      !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(adminEmail)) return null;
  return { domain, audience, adminEmail: adminEmail.toLowerCase() };
}
async function fetchKeys(domain, force = false) {
  const prev = jwksCache.get(domain);
  if (!force && prev && prev.until > Date.now()) return prev.keys;
  const response = await fetch(domain + '/cdn-cgi/access/certs', { redirect: 'error' });
  if (!response.ok) throw new Error('certs_unavailable');
  const data = await response.json();
  if (!Array.isArray(data.keys) || data.keys.length > 32) throw new Error('invalid_certs');
  const keys = data.keys.filter(k => k.kty === 'RSA' && typeof k.kid === 'string');
  jwksCache.set(domain, { keys, until: Date.now() + 10 * 60 * 1000 });
  return keys;
}

export async function verifyAccess(request, env) {
  const config = accessSettings(env);
  if (!config) return { error: 'access_not_configured', status: 503 };
  const token = request.headers.get('Cf-Access-Jwt-Assertion');
  if (!token || token.length > 8192) return { error: 'access_denied', status: 403 };
  try {
    const parts = token.split('.');
    if (parts.length !== 3) throw new Error('invalid_token');
    const header = parsePart(parts[0]);
    const claims = parsePart(parts[1]);
    if (header.alg !== 'RS256' || typeof header.kid !== 'string' ||
        header.kid.length > 200 || !/^[A-Za-z0-9_.-]+$/.test(header.kid)) throw new Error('algorithm');
    const now = Math.floor(Date.now() / 1000);
    if (claims.iss !== config.domain ||
        !(typeof claims.aud === 'string' ? claims.aud === config.audience :
          Array.isArray(claims.aud) && claims.aud.includes(config.audience)) ||
        typeof claims.exp !== 'number' || claims.exp <= now ||
        typeof claims.iat !== 'number' || claims.iat > now + 60 ||
        (claims.nbf !== undefined && (typeof claims.nbf !== 'number' || claims.nbf > now)) ||
        typeof claims.email !== 'string' || claims.email.toLowerCase() !== config.adminEmail)
      throw new Error('claims');
    let keys = await fetchKeys(config.domain);
    let jwk = keys.find(k => k.kid === header.kid);
    if (!jwk) {
      keys = await fetchKeys(config.domain, true);
      jwk = keys.find(k => k.kid === header.kid);
    }
    if (!jwk || jwk.alg && jwk.alg !== 'RS256') throw new Error('key_not_found');
    const key = await crypto.subtle.importKey('jwk', jwk,
      { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['verify']);
    const valid = await crypto.subtle.verify('RSASSA-PKCS1-v1_5', key,
      decode(parts[2]), new TextEncoder().encode(parts[0] + '.' + parts[1]));
    if (!valid) throw new Error('signature');
    return { email: config.adminEmail };
  } catch {
    return { error: 'access_denied', status: 403 };
  }
}
