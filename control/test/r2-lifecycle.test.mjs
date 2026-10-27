import test from 'node:test';
import assert from 'node:assert/strict';
import { maintenanceSummary } from '../src/maintenance.js';
import { html } from '../src/ui.js';

function sample(now,withExtra={}){
 const checkedAt=Math.floor(now/1000);
 return {state:'ready',data:{
  checkedAt,ageSeconds:10,sampledCronMinutes:60,relayCleanupCronMinutes:5,
  securityPruneSucceeded:true,
  deletionCounts:{
   deleted_messages:0,deleted_nonces:0,deleted_rate_windows:0,
   deleted_receipts:0,deleted_security_rollups:0,
  },
  r2Audit:{state:'ready',data:{
   auditedAt:checkedAt-60,ageSeconds:60,sampledObjects:2,
   sampledBytes:8192,overdueObjects:1,oldestAgeSeconds:9*86400,
   truncated:true,prefix:'transfers/',sampleLimit:500,lifecycleDays:7,
   warningAfterDays:8,
   secretKey:'NEVER-EXPOSE-R2-KEY',deviceId:'PRIVATE-ID',
  },...withExtra},
 }};
}
test('private maintenance RPC forwards only bounded safe R2 lifecycle statistics',async()=>{
 const now=Date.now();
 const env={RELAY_TELEMETRY:{getMaintenanceStatus:async()=>sample(now)}};
 const d=await maintenanceSummary(env,now);
 assert.equal(d.state,'ready');
 const r=d.data.r2Audit;
 assert.equal(r.state,'ready');
 assert.equal(r.data.truncated,true);
 assert.equal(r.data.sampledObjects,2);
 assert.equal(r.data.sampledBytes,8192);
 assert.equal(r.data.overdueObjects,1);
 assert.equal(r.coverage,'bounded_prefix_sample');
 const all=JSON.stringify(d);
 for(const secret of ['NEVER-EXPOSE-R2-KEY','PRIVATE-ID'])
  assert.equal(all.includes(secret),false);
 assert.deepEqual(Object.keys(r.data).sort(),[
  'auditedAt','ageSeconds','sampledObjects','sampledBytes','overdueObjects',
  'oldestAgeSeconds','truncated','prefix','sampleLimit','lifecycleDays',
  'warningAfterDays'
 ].sort());
});
test('R2 status cache re-evaluates stale age without extra Relay RPC request',async()=>{
 const now=Date.now();
 // Isolate module-level RPC cache from other tests.
 const {maintenanceSummary:localSummary}=await import('../src/maintenance.js?test=r2-freshness');
 let calls=0;
 const env={RELAY_TELEMETRY:{getMaintenanceStatus:async()=>{
  calls++;return sample(now-7*3600000-59*60000);
 }}};
 const a=await localSummary(env,now);
 assert.equal(a.data.r2Audit.state,'ready');
 const b=await localSummary(env,now+2*60000);
 assert.equal(b.data.r2Audit.state,'stale');
 assert.equal(calls,1);
});
test('new UI gives privacy and incomplete-sample warnings instead of claiming full bucket usage',()=>{
 for(const id of ['r2-audit-status','r2-audit-pill','r2-audit-count',
  'r2-audit-bytes','r2-audit-overdue','r2-audit-oldest','r2-audit-warning'])
  assert.ok(html.includes('id="'+id+'"'),id);
 assert.ok(html.includes('MaVka Server Control 0.7.1'));
 assert.ok(html.includes('showR2Audit(result?.data?.r2Audit)'));
 assert.ok(html.includes('НЕ є повним підсумком'));
 assert.ok(html.includes('не для всього бакета'));
});
