// Only bounded aggregate reasons from the named private Relay RPC.
// This Control Worker never receives, stores, or reads raw security logs.
const REASONS = new Set([
 'auth_headers_invalid','auth_timestamp_invalid','auth_device_unavailable',
 'auth_main_binding_invalid','auth_public_key_invalid','auth_signature_invalid',
 'unauthorized','country_not_allowed','native_clients_only','https_required',
 'not_found','rate_limited','request_replayed','main_required','mobile_required',
 'device_unavailable','device_not_found','device_conflict','cannot_revoke_main',
 'route_forbidden','message_not_found','invalid_expiry','device_capacity',
 'queue_full','recipient_unavailable','body_too_large','json_required',
 'encoding_not_supported','body_required','message_expired','invalid_request',
 'invalid_base64url','request_rejected','service_unavailable',
]);
const CATEGORIES = new Set(['authentication','perimeter','authorization','replay',
 'rate_limit','route_or_resource','service','invalid_request']);
const SCOPES = new Set(['UA','non_UA','unknown']);
const ok = n => Number.isSafeInteger(n)&&n>=0;
const missing = reason => ({state:'not_connected',source:'relay_aggregate_d1',
  reason,data:null,coverage:'best_effort'});
let last=null, lastAt=0, inFlight=null;
export async function securitySummary(env, now=Date.now()){
 if(typeof env.RELAY_TELEMETRY?.getSecurityOverview!=='function')
   return missing('private_rpc_not_available');
 if(last && now-lastAt<300000) return last;
 if(inFlight) return inFlight;
 inFlight=(async()=>{
  try{
   const raw=await env.RELAY_TELEMETRY.getSecurityOverview();
   if(raw?.state!=='ready'||raw.coverage!=='best_effort'||
     !ok(raw.observedAt)||!ok(raw.total24h)||!ok(raw.total7d)||
     raw.total24h>raw.total7d||!Array.isArray(raw.byReason)||
     !Array.isArray(raw.byCountry)||!Array.isArray(raw.byHour))
     return missing('migration_or_telemetry_unavailable');
   if(raw.byReason.length>12||raw.byCountry.length>3||raw.byHour.length>25 ||
     !raw.byReason.every(r=>r&&CATEGORIES.has(r.category)&&REASONS.has(r.reason)&&ok(r.count))||
     !raw.byCountry.every(x=>x&&SCOPES.has(x.scope)&&ok(x.count))||
     !raw.byHour.every(x=>x&&ok(x.hourStart)&&ok(x.count)))
     return missing('invalid_aggregate_shape');
   // whitelist keys at the receiving edge, never forward extra fields.
   const output={
     state:'ready',source:'private_relay_rpc',coverage:'best_effort',
     observedAt:raw.observedAt,retentionDays:30,
     total24h:raw.total24h,total7d:raw.total7d,
     byReason:raw.byReason.map(x=>({category:x.category,reason:x.reason,count:x.count})),
     byCountry:raw.byCountry.map(x=>({scope:x.scope,count:x.count})),
     byHour:raw.byHour.map(x=>({hourStart:x.hourStart,count:x.count})),
     // These are observed aggregate counts, never proven global totals.
     limitations:'Approximate, may undercount due to Worker isolate buffering. Cloudflare Observability is the source for individual events.',
   };
   last=output;lastAt=Date.now();return output;
  }catch{return missing('private_rpc_unavailable')}
  finally{inFlight=null}
 })();
 return inFlight;
}
