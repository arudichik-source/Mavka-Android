// Private, read-only hourly Relay maintenance marker and daily local pruning.
// This module never opens relay-messages, enumerates devices or reads payloads.
const KEYS=['deleted_messages','deleted_nonces','deleted_rate_windows',
 'deleted_receipts','deleted_security_rollups'];
const validCount=n=>n===null || (Number.isSafeInteger(n)&&n>=0);
const missing=reason=>({state:'not_connected',reason,data:null});
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
  data:{...result.data,ageSeconds:elapsed}};
}
export async function maintenanceSummary(env,now=Date.now()){
 if(typeof env.RELAY_TELEMETRY?.getMaintenanceStatus!=='function')
  return missing('rpc_not_deployed');
 if(cached && now-cachedAt<300000) return withCurrentAge(cached,now);
 if(pending)return pending.then(result=>withCurrentAge(result,now));
 pending=(async()=>{
  try{
   const raw=await env.RELAY_TELEMETRY.getMaintenanceStatus();
   if(!['ready','stale'].includes(raw?.state)||!raw.data)return missing('migration_or_rpc_unavailable');
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
       securityPruneSucceeded:d.securityPruneSucceeded},
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
