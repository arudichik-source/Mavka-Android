// Private, read-only hourly Relay maintenance marker and daily local pruning.
// This module never opens relay-messages, enumerates devices or reads payloads.
const KEYS=['deleted_messages','deleted_nonces','deleted_rate_windows',
 'deleted_receipts','deleted_security_rollups'];
const validCount=n=>n===null || (Number.isSafeInteger(n)&&n>=0);
const missing=reason=>({state:'not_connected',reason,data:null});
const REMOTE_MISSING=new Set([
 'db_not_bound','no_hourly_checkpoint_yet',
 'invalid_checkpoint','table_or_database_unavailable',
]);
const safeReasons=new Set([
 'no_r2_audit_yet','invalid_timestamp','invalid_audit',
 'r2_binding_missing','r2_list_failed',
]);
const count=n=>Number.isSafeInteger(n)&&n>=0;
function sanitizeR2(raw,now){
 const state=raw?.state;
 if(!['ready','stale','not_connected','unavailable'].includes(state))
  return {state:'not_connected',reason:'no_r2_audit_yet'};
 if(state==='not_connected'||state==='unavailable')return {
  state,reason:safeReasons.has(raw.reason)?raw.reason:'no_r2_audit_yet',
 };
 const d=raw.data,utc=Math.floor(now/1000);
 if(!d||!count(d.auditedAt)||d.auditedAt>utc+300||
  !count(d.sampledObjects)||d.sampledObjects>500||
  !count(d.sampledBytes)||!count(d.overdueObjects)||
  d.overdueObjects>d.sampledObjects||!count(d.oldestAgeSeconds)||
  typeof d.truncated!=='boolean'||d.prefix!=='transfers/'||
  d.sampleLimit!==500||d.lifecycleDays!==7||d.warningAfterDays!==8)
  return {state:'not_connected',reason:'invalid_audit'};
 const ageSeconds=Math.max(0,utc-d.auditedAt);
 // Strict whitelist: object keys, transfer IDs, cursors and extra metadata
 // cannot cross the internal RPC into the authenticated dashboard.
 return {state:ageSeconds>8*3600?'stale':'ready',
  data:{auditedAt:d.auditedAt,ageSeconds,sampledObjects:d.sampledObjects,
   sampledBytes:d.sampledBytes,overdueObjects:d.overdueObjects,
   oldestAgeSeconds:d.oldestAgeSeconds,truncated:d.truncated,
   prefix:'transfers/',sampleLimit:500,lifecycleDays:7,warningAfterDays:8},
  coverage:'bounded_prefix_sample'};
}
let cached=null,cachedAt=0,pending=null;
// A previously cached successful read must NEVER stay "ready" when its
// checkpoint passes the 90-minute threshold. Re-evaluate age on EVERY HTTP
// request, even when Cloudflare RPC itself is cached for five minutes.
function withCurrentAge(result,now) {
 if(!['ready','stale'].includes(result?.state)||!result?.data)return result;
 const at=result.data.checkedAt,nowSeconds=Math.floor(now/1000);
 if(!Number.isSafeInteger(nowSeconds)||at>nowSeconds+300)
  return missing('invalid_checkpoint_time');
 const elapsed=Math.max(0,nowSeconds-at);
 return {...result,state:elapsed>5400?'stale':'ready',
  data:{...result.data,ageSeconds:elapsed,
   r2Audit:sanitizeR2(result.data.r2Audit,now)}};
}
export async function maintenanceSummary(env,now=Date.now()){
 if(typeof env.RELAY_TELEMETRY?.getMaintenanceStatus!=='function')
  return missing('rpc_not_deployed');
 if(cached && now-cachedAt<300000) return withCurrentAge(cached,now);
 if(pending)return pending.then(result=>withCurrentAge(result,now));
 pending=(async()=>{
  try{
   const raw=await env.RELAY_TELEMETRY.getMaintenanceStatus();
   if(!['ready','stale'].includes(raw?.state)||!raw.data)
    return missing(REMOTE_MISSING.has(raw?.reason)
     ? raw.reason : 'migration_or_rpc_unavailable');
   const d=raw.data;
   if(!Number.isSafeInteger(d.checkedAt)||d.checkedAt<0||
      !Number.isSafeInteger(d.ageSeconds)||d.ageSeconds<0||
      !KEYS.every(k=>validCount(d.deletionCounts?.[k]))||
      typeof d.securityPruneSucceeded!=='boolean'||
      d.sampledCronMinutes!==60||d.relayCleanupCronMinutes!==5)
      return missing('invalid_checkpoint');
   const result={state:raw.state,source:'private_relay_rpc',
      data:{checkedAt:d.checkedAt,ageSeconds:d.ageSeconds,
       sampledCronMinutes:60,relayCleanupCronMinutes:5,
       deletionCounts:Object.fromEntries(KEYS.map(k=>[k,d.deletionCounts[k]])),
       securityPruneSucceeded:d.securityPruneSucceeded,
       r2Audit:sanitizeR2(d.r2Audit,now)},
      note:'Hourly sample only, not the hourly total and not current expired backlog'};
   cached=result;cachedAt=Date.now();return result;
  }catch{return missing('rpc_unavailable')}
  finally{pending=null;}
 })();
 return pending.then(result=>withCurrentAge(result,now));
}
export async function pruneControlHistory(db, now=new Date()){
 if(!db?.prepare)return {state:'not_bound',deleted:null};
 try{
  const seconds=Math.floor(now.getTime()/1000);
  if(!Number.isSafeInteger(seconds)||seconds<2592000)throw Error('bad_clock');
  const result=await db.prepare('DELETE FROM usage_snapshots WHERE bucket_start < ?')
   .bind(seconds-30*86400).run();
  if(result?.success===false) return {state:'unavailable',deleted:null};
  return {state:'ready',deleted:Number.isSafeInteger(result?.meta?.changes)?result.meta.changes:null};
 }catch{return {state:'unavailable',deleted:null};}
}
