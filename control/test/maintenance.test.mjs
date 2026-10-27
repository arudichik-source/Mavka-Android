import test from 'node:test';
import assert from 'node:assert/strict';
import { maintenanceSummary, pruneControlHistory } from '../src/maintenance.js';
import { html } from '../src/ui.js';

test('private maintenance RPC removes all unapproved fields before dashboard response',async()=>{
 const now=Date.now();
 const stub={
  state:'ready',data:{
   checkedAt:Math.floor(now/1000),ageSeconds:60,
   sampledCronMinutes:60,relayCleanupCronMinutes:5,
   securityPruneSucceeded:true,
   deletionCounts:{
    deleted_messages:2,deleted_nonces:5,deleted_rate_windows:3,
    deleted_receipts:4,deleted_security_rollups:0,
    deviceId:'private-id-never-forward'
   },messageBody:'private-message',ipAddress:'192.0.2.100',
  },secretKey:'never-forward',
 };
 const env={RELAY_TELEMETRY:{getMaintenanceStatus:async()=>stub}};
 const response=await maintenanceSummary(env,now);
 assert.equal(response.state,'ready');
 assert.equal(response.data.deletionCounts.deleted_messages,2);
 assert.equal(response.data.securityPruneSucceeded,true);
 assert.equal(Object.keys(response.data.deletionCounts).length,5);
 for(const word of ['private-id-never-forward','private-message','192.0.2.100','never-forward'])
  assert.ok(!JSON.stringify(response).includes(word));
});
test('missing, invalid or stale checkpoint never creates fictional fresh success',async()=>{
 assert.equal((await maintenanceSummary({})).state,'not_connected');
 const clock=Date.now()+360000;
 const bad={RELAY_TELEMETRY:{getMaintenanceStatus:async()=>({
  state:'ready',data:{checkedAt:0,ageSeconds:0,relayCleanupCronMinutes:5,
   sampledCronMinutes:60,securityPruneSucceeded:true,
   deletionCounts:{deleted_messages:-1,deleted_nonces:0,
     deleted_rate_windows:0,deleted_receipts:0,deleted_security_rollups:0}}
 })}};
 assert.equal((await maintenanceSummary(bad,clock)).state,'not_connected');
 const tooOld={RELAY_TELEMETRY:{getMaintenanceStatus:async()=>({
  state:'stale',data:{
   checkedAt:Math.floor(Date.now()/1000)-7200,ageSeconds:7200,
   sampledCronMinutes:60,relayCleanupCronMinutes:5,
   securityPruneSucceeded:false,
   deletionCounts:{deleted_messages:null,deleted_nonces:0,
    deleted_rate_windows:0,deleted_receipts:0,deleted_security_rollups:null}
  }
 })}};
 const stale=await maintenanceSummary(tooOld,clock);
 assert.equal(stale.state,'stale');
 assert.equal(stale.data.deletionCounts.deleted_security_rollups,null);
});
test('Control daily expiry cleanup is one bounded indexed SQL DELETE, with null on errors',async()=>{
 const calls=[];
 const db={prepare(sql){return {bind(...params){
  calls.push({sql,params});
  return {run:async()=>({meta:{changes:3}})};
 }}}};
 const today=new Date('2026-10-25T03:17:00.000Z');
 const data=await pruneControlHistory(db,today);
 assert.equal(data.state,'ready');assert.equal(data.deleted,3);
 assert.equal(calls.length,1);
 assert.match(calls[0].sql,/DELETE FROM usage_snapshots WHERE bucket_start < \?/);
 assert.deepEqual(calls[0].params,[Math.floor(today.getTime()/1000)-30*86400]);
 assert.equal((await pruneControlHistory(null)).state,'not_bound');
 assert.equal((await pruneControlHistory({prepare(){throw Error('Secret')}})).state,'unavailable');
});
test('new UI only presents one-hour sampled deletion figures and explicit stale state',()=>{
 for(const id of ['cleanup-pill','cleanup-status','cleanup-msg','cleanup-nonces',
  'cleanup-receipts','cleanup-rate','cleanup-security'])
  assert.ok(html.includes('id="'+id+'"'),id);
 assert.ok(html.includes('MaVka Server Control 0.8.1'));
 assert.ok(html.includes("showMaintenance(results[5]"));
 assert.ok(html.includes('це не підрахунок за всю годину') ||
  html.includes('Це не підрахунок за всю годину'));
});
