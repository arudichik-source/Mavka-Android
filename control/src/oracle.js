// Separate read-only Oracle monitoring VPC; no Relay, Shorts or public fetch.
const allowed=['mavka-web.service','mavka-sync-api.service','caddy.service','cloudflared.service','mavka-auth-v2-1.service','mavka-panel-web.service'];
const n=(v,max=10000000000)=>Number.isSafeInteger(v)&&v>=0&&v<=max?v:null;
const pct=(v,max=100)=>Number.isFinite(v)&&v>=0&&v<=max?v:null;
function requestCount(x){
 if(!x||typeof x!=='object')return null;
 const complete=x.complete===true;
 const statuses=complete&&x.statuses&&typeof x.statuses==='object'?
  Object.fromEntries(['2xx','3xx','4xx','5xx'].map(k=>[k,n(x.statuses[k])])):null;
 return {day:typeof x.day==='string'&&/^\d{4}-\d\d-\d\d$/.test(x.day)?x.day:null,
  complete,count:complete?n(x.count):null,observed:n(x.observed),statuses};
}
export function sanitizeOracle(x){
 if(!x||x.state!=='ready')return null;
 const services={};
 for(const k of allowed)services[k]=['active','inactive','unknown'].includes(x.services?.[k])?x.services[k]:'unknown';
 return {state:'ready',checkedAt:typeof x.checkedAt==='string'&&/^\d{4}-\d\d-\d\dT/.test(x.checkedAt)?x.checkedAt.slice(0,30):null,
  cpuPercent:pct(x.cpuPercent),ramPercent:pct(x.ramPercent),diskFreeGb:pct(x.diskFreeGb,1000000),
  siteProbe:['online','degraded','offline'].includes(x.siteProbe)?x.siteProbe:'unknown',
  siteRequests:requestCount(x.siteRequests),apiRequests:requestCount(x.apiRequests),services};
}
export async function oracleTelemetry(env){
 if(typeof env?.ORACLE_MONITOR_KEY!=='string'||env.ORACLE_MONITOR_KEY.length<32||
    typeof env?.ORACLE_MONITOR_VPC?.fetch!=='function')return {state:'not_connected'};
 try{
  const r=await env.ORACLE_MONITOR_VPC.fetch('http://127.0.0.1:7340/v1/telemetry',{
   method:'GET',redirect:'manual',
   headers:{Authorization:'Bearer '+env.ORACLE_MONITOR_KEY,Accept:'application/json'},
   signal:AbortSignal.timeout(8000)});
  if(r.status===401||r.status===403)return {state:'auth_failed'};
  if(!r.ok||Number(r.headers.get('content-length'))>16384)return {state:'unavailable'};
  const raw=await r.text();
  if(raw.length>16384)return {state:'invalid_response'};
  return sanitizeOracle(JSON.parse(raw))||{state:'invalid_response'};
 }catch{return {state:'unavailable'};}
}
