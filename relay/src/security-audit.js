// Allowlisted, privacy-minimized security telemetry for Workers Observability.
// NEVER add raw URLs/query strings, IPs, headers, device IDs, message bodies,
// signing material, public keys, exception messages, or stack traces here.
import { ApiError } from './protocol.js';

export const SECURITY_EVENT = 'MAVKA_RELAY_SECURITY_V1';

const AUTH_DETAILS = new Set([
  'auth_headers_invalid', 'auth_timestamp_invalid', 'auth_device_unavailable',
  'auth_main_binding_invalid', 'auth_public_key_invalid', 'auth_signature_invalid',
]);
const SAFE_REASONS = new Set([
  'unauthorized', 'country_not_allowed', 'native_clients_only', 'https_required',
  'not_found', 'rate_limited', 'request_replayed', 'main_required',
  'mobile_required', 'device_unavailable', 'device_not_found', 'device_conflict',
  'cannot_revoke_main', 'route_forbidden', 'message_not_found',
  'invalid_expiry', 'device_capacity', 'queue_full', 'recipient_unavailable',
  'body_too_large', 'json_required', 'encoding_not_supported', 'body_required',
  'message_expired', 'invalid_request', 'invalid_base64url',
]);

function route(request) {
  let path;
  try { path = new URL(request.url).pathname; } catch { return 'other'; }
  const method = request.method;
  if (method === 'GET' && path === '/health') return 'health';
  if (method === 'GET' && path === '/v1/status') return 'status';
  if (method === 'POST' && path === '/v1/bootstrap') return 'bootstrap';
  if (method === 'POST' && path === '/v1/devices/activate') return 'activation';
  if (method === 'GET' && path === '/v1/devices') return 'devices';
  if (method === 'POST' && path === '/v1/devices') return 'enrollment';
  if (method === 'GET' && path === '/v1/messages') return 'inbox';
  if (method === 'POST' && path === '/v1/messages') return 'send';
  if (method === 'POST' && /^\/v1\/devices\/[^/]+\/revoke$/.test(path)) return 'revocation';
  if (method === 'POST' && /^\/v1\/messages\/[^/]+\/ack$/.test(path)) return 'message_ack';
  // Future file routes must be matched by shape, not logged with their IDs.
  if (path === '/v1/files/status') return 'files_status';
  if (path === '/v1/files/parts' || path.startsWith('/v1/files/parts/')) return 'files';
  return 'other';
}

function category(status, reason) {
  if (status === 401) return 'authentication';
  if (reason === 'country_not_allowed' || reason === 'native_clients_only' ||
      reason === 'https_required') return 'perimeter';
  if (status === 403) return 'authorization';
  if (reason === 'request_replayed') return 'replay';
  if (status === 429) return 'rate_limit';
  if (status === 404) return 'route_or_resource';
  if (status >= 500) return 'service';
  return 'invalid_request';
}

export function securityEvent(request, error, principal = 'unverified') {
  const status = error instanceof ApiError ? error.status : 503;
  const detailed = error instanceof ApiError && AUTH_DETAILS.has(error.auditReason)
    ? error.auditReason : null;
  const reason = detailed ?? (error instanceof ApiError && SAFE_REASONS.has(error.code)
    ? error.code : status >= 500 ? 'service_unavailable' : 'request_rejected');
  const countryScope = request.cf?.country === 'UA' ? 'UA'
    : typeof request.cf?.country === 'string' ? 'non_UA' : 'unknown';
  return {
    event: SECURITY_EVENT,
    outcome: 'rejected',
    category: category(status, reason),
    reason,
    status,
    route: route(request),
    method: ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'].includes(request.method)
      ? request.method : 'OTHER',
    countryScope,
    principal: ['verified_main', 'verified_mobile'].includes(principal) ? principal : 'unverified',
  };
}

export function reportSecurityEvent(request, error, principal) {
  try {
    const event = securityEvent(request, error, principal);
    // One structured line. Do not serialize request/error objects.
    console.info(JSON.stringify(event));
    return event;
  } catch {
    // A logging failure must never alter Relay's authentication response.
  }
}
