import test from 'node:test';
import assert from 'node:assert/strict';
import { maintenanceSummary, pruneControlHistory } from '../src/maintenance.js';

test('five-minute RPC cache never masks a checkpoint turning stale at 90 minutes',async()=>{
 const now=Date.now(), checkedAt=Math.floor(now/1000)-89*60;
 let rpcCalls=0;
 const env={RELAY_TELEMETRY:{getMaintenanceStatus:async()=>{
  rpcCalls++;
  return {state:'ready',data:{
   checkedAt,ageSeconds:0, // intentionally stale provider age, recompute locally
   sampledCronMinutes:60,relayCleanupCronMinutes:5,securityPruneSucceeded:true,
   deletionCounts:{deleted_messages:0,deleted_nonces:0,
    deleted_rate_windows:0,deleted_receipts:0,deleted_security_rollups:0}
  }};
 }}};
 const a=await maintenanceSummary(env,now);
 assert.equal(a.state,'ready');
 assert.ok(a.data.ageSeconds>=89*60);
 const b=await maintenanceSummary(env,now+2*60*1000);
 assert.equal(b.state,'stale');
 assert.ok(b.data.ageSeconds>5400);
 assert.equal(rpcCalls,1,'Do not spend additional D1/RPC requests to detect staleness');
});
test('D1 cleanup result with explicit success:false cannot be shown as completed',async()=>{
 const db={prepare(){return {bind(){return {run:async()=>({
  success:false,meta:{changes:0}
 })}}}}};
 const result=await pruneControlHistory(db,new Date('2026-10-26T03:17:00Z'));
 assert.deepEqual(result,{state:'unavailable',deleted:null});
});
