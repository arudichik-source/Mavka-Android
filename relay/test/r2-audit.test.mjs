import test from 'node:test';
import assert from 'node:assert/strict';
import {scanR2Transfers,recordR2Audit,readR2AuditFromRow,shouldAuditR2} from '../src/r2-audit.js';

const t=Date.UTC(2026,9,26,12,0);
const secs=Math.floor(t/1000);
const item=(ageDays,size=1024)=>({
  key:'transfers/PRIVATE_DEVICE_ID/SECRET_TRANSFER_ID',
  size, uploaded:new Date(t-ageDays*86400000),
  etag:'secret-etag', customMetadata:{secret:'never_return'},
});

test('R2 scan occurs only once every 6 hours from existing five minute Cron',()=>{
 for(const hour of [0,6,12,18])assert.equal(shouldAuditR2(Date.UTC(2026,9,26,hour)),true);
 for(const hour of [1,3,7,13,23])assert.equal(shouldAuditR2(Date.UTC(2026,9,26,hour)),false);
 assert.equal(shouldAuditR2(t+5*60000),false);
 // Cron.scheduledTime has millisecond precision; a nonzero seconds field
 // must not skip the one intended UTC six-hour window.
 assert.equal(shouldAuditR2(t+12345),true);
 assert.equal(shouldAuditR2(t+59999),true);
 assert.equal(shouldAuditR2(t-1),false);
 assert.equal(shouldAuditR2(null),false);
});
test('single capped metadata list exposes only anonymous age/size/count',async()=>{
 let calls=0;
 const audit=await scanR2Transfers({list:async query=>{
  calls++;
  assert.deepEqual(query,{prefix:'transfers/',limit:500});
  return {objects:[item(2,2048),item(9,4096)],truncated:false,
    cursor:'cursor-should-never-return'};
 }},secs);
 assert.deepEqual(audit,{
  state:'ready',scanned:2,bytes:6144,overdue:1,
  oldestAgeSeconds:9*86400,truncated:false,
 });
 assert.equal(calls,1);
 const serialized=JSON.stringify(audit);
 for(const sensitive of ['PRIVATE_DEVICE_ID','SECRET_TRANSFER_ID','secret-etag','cursor-should-never-return'])
  assert.equal(serialized.includes(sensitive),false);
});
test('truncated prefix is explicitly partial; other bucket content is not scanned',async()=>{
 const audit=await scanR2Transfers({list:async()=>({
  objects:[item(1)],truncated:true,
 })},secs);
 assert.equal(audit.state,'ready');assert.equal(audit.truncated,true);
 assert.equal(audit.scanned,1);assert.equal(audit.overdue,0);
 const bad=await scanR2Transfers({list:async()=>({objects:[item(2,{bad:'size'})],truncated:false})},secs);
 assert.equal(bad.state,'failed');assert.equal(bad.scanned,null);
 const missing=await scanR2Transfers({},secs);
 assert.equal(missing.state,'missing_binding');
 const failed=await scanR2Transfers({list:async()=>{throw Error('private bucket error')}},secs);
 assert.equal(failed.state,'failed');
 assert.equal(JSON.stringify(failed).includes('private'),false);
});
test('one R2 result upserts into existing singleton, with missing migrations safely ignored',async()=>{
 let calls=[],statement='';
 const fake={prepare(sql){statement=sql;return {bind(...values){
  calls.push(values);return {run:async()=>({success:true,meta:{changes:1}})};
 }}}};
 const sample={state:'ready',scanned:1,bytes:2048,overdue:0,oldestAgeSeconds:43200,truncated:true};
 assert.equal(await recordR2Audit(fake,sample,t),true);
 assert.equal(calls.length,1);
 assert.match(statement,/UPDATE relay_maintenance_status/);
 assert.equal(statement.includes('DELETE '),false);
 assert.deepEqual(calls[0],[secs,'ready',1,2048,0,43200,1]);
 assert.equal(await recordR2Audit(fake,sample,t+5*60000),false);
 assert.equal(calls.length,1);
 const noRows={prepare(){return{bind(){return{run:async()=>({success:true,meta:{changes:0}})}}}}};
 assert.equal(await recordR2Audit(noRows,sample,t),false);
 const oldColumns={prepare(){throw Error('no such column')}};
 assert.equal(await recordR2Audit(oldColumns,sample,t),false);
});
test('single-row read never copies object identifiers or yields false totals',()=>{
 const base={r2_audit_at:secs,r2_audit_state:'ready',
 r2_sample_count:2,r2_sample_bytes:9216,r2_overdue_count:1,
 r2_oldest_age_seconds:9*86400,r2_sample_truncated:1,
 bucketKey:'SECRET_KEY',device_id:'SECRET_DEVICE_ID'};
 const result=readR2AuditFromRow(base,secs+120);
 assert.equal(result.state,'ready');
 assert.equal(result.data.truncated,true);
 assert.equal(result.data.sampledBytes,9216);
 assert.equal(result.data.overdueObjects,1);
 assert.equal(result.data.lifecycleDays,7);
 assert.equal(result.data.warningAfterDays,8);
 assert.equal(JSON.stringify(result).includes('SECRET'),false);
 assert.equal(readR2AuditFromRow(base,secs+9*3600).state,'stale');
 assert.equal(readR2AuditFromRow({r2_audit_at:null},secs).state,'not_connected');
 assert.equal(readR2AuditFromRow({...base,r2_sample_count:-1},secs).state,'not_connected');
 assert.equal(readR2AuditFromRow({...base,r2_audit_state:'failed'},secs).state,'unavailable');
});
