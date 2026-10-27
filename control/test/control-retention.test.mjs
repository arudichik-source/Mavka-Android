import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { readControlRetention, recordControlRetention } from '../src/control-retention.js';
import control from '../src/worker.js';
import { html } from '../src/ui.js';

const TIME=Date.parse('2026-10-27T12:00:00Z'),nowS=Math.floor(TIME/1000);
const readDb=row=>({prepare(sql){
 assert.match(sql,/FROM control_retention_status WHERE id = 1/);
 return {first:async()=>row};
}});
const info=(attempt=nowS-3600,ok=1,success=attempt,deleted=0)=>({
 last_attempt_at:attempt,last_attempt_ok:ok,
 last_success_at:success,last_deleted:deleted,
});

test('unconfigured, uninitialized and unavailable status never look like success',async()=>{
 assert.deepEqual(await readControlRetention(null,TIME),
  {state:'not_connected',reason:'db_not_bound',data:null});
 assert.equal((await readControlRetention(readDb(null),TIME)).reason,'awaiting_first_run');
 const broken={prepare(){throw Error('secret connection details')}};
 const status=await readControlRetention(broken,TIME);
 assert.equal(status.state,'not_connected');
 assert.equal(status.reason,'status_unavailable');
 assert.doesNotMatch(JSON.stringify(status),/secret/);
});

test('verified successful Cron with zero deletes is healthy, bounded and non-sensitive',async()=>{
 const result=await readControlRetention(readDb({
  ...info(nowS-3600,1,nowS-3600,0),
  token:'never-forward',ip:'192.0.2.5',
 }),TIME);
 assert.equal(result.state,'ready');
 assert.equal(result.data.lastAttemptOk,true);
 assert.equal(result.data.deletedLastSuccess,0);
 assert.equal(result.data.retentionDays,30);
 assert.equal(result.data.scheduleUtc,'03:17');
 assert.equal(Object.keys(result.data).length,7);
 assert.doesNotMatch(JSON.stringify(result),/never-forward|192\.0\.2\.5/);
});

test('stale (>36h) and last failed Cron remain warning states',async()=>{
 const old=nowS-37*3600;
 const stale=await readControlRetention(readDb(info(old,1,old,12)),TIME);
 assert.equal(stale.state,'stale');
 const failed=await readControlRetention(readDb(info(nowS-600,0,old,12)),TIME);
 assert.equal(failed.state,'failed');
 assert.equal(failed.data.lastSuccessAt,old);
 assert.equal(failed.data.deletedLastSuccess,12);
 const failedFirst=await readControlRetention(readDb(info(nowS-600,0,null,null)),TIME);
 assert.equal(failedFirst.state,'failed');
 assert.equal(failedFirst.data.ageSeconds,null);
});

test('future, inconsistent, and invalid row values fail closed',async()=>{
 for(const row of [
  info(nowS+3600,1,nowS+3600,0),
  info(nowS-600,1,nowS-700,0),
  info(nowS-600,1,nowS-600,-1),
  info(nowS-600,7,nowS-600,0),
  info(nowS-600,0,nowS+100,0),
 ]){
  const actual=await readControlRetention(readDb(row),TIME);
  assert.equal(actual.state,'not_connected');
  assert.equal(actual.reason,'invalid_checkpoint');
 }
});

test('daily marker is one bound singleton UPSERT; failures retain last success',async()=>{
 const calls=[];
 const db={prepare(sql){return {bind(...params){calls.push({sql,params});
  return {run:async()=>({success:true})};
 }}}};
 assert.equal((await recordControlRetention(db,{state:'ready',deleted:0},TIME)).state,'recorded');
 assert.equal((await recordControlRetention(db,{state:'unavailable'},TIME)).state,'recorded');
 assert.equal(calls.length,2);
 assert.match(calls[0].sql,/ON CONFLICT\(id\) DO UPDATE SET/);
 assert.match(calls[0].sql,/CASE WHEN excluded.last_attempt_ok=1/);
 assert.deepEqual(calls[0].params,[nowS,1,nowS,0]);
 assert.deepEqual(calls[1].params,[nowS,0,null,null]);
 assert.equal((await recordControlRetention(null,{state:'ready',deleted:0},TIME)).state,'not_bound');
 assert.equal((await recordControlRetention(db,{state:'ready',deleted:-2},TIME)).state,'unavailable');
 assert.equal(calls.length,2);
 const unavailable={prepare(){throw Error('sensitive remote body')}};
 assert.deepEqual(await recordControlRetention(unavailable,{state:'ready',deleted:4},TIME),
  {state:'unavailable'});
});

test('Cron deletes before attempting optional marker and keeps deletion success',async()=>{
 const statements=[];
 const db={prepare(sql){
  if(sql.startsWith('DELETE FROM usage_snapshots'))return {bind(...params){
   statements.push({kind:'delete',params});
   return {run:async()=>({success:true,meta:{changes:0}})};
  }};
  if(sql.startsWith('INSERT INTO control_retention_status'))return {bind(...params){
   statements.push({kind:'marker',params});
   return {run:async()=>({success:true})};
  }};
  throw Error('unexpected query');
 }};
 const tasks=[],ctx={waitUntil(p){tasks.push(p)}};
 await control.scheduled({}, {CONTROL_DB:db},ctx);
 await Promise.all(tasks);
 assert.deepEqual(statements.map(x=>x.kind),['delete','marker']);
 assert.equal(statements[1].params[1],1);
 assert.equal(statements[1].params[3],0);
 const withoutMigration={prepare(sql){
  if(sql.startsWith('DELETE FROM usage_snapshots'))return {bind(){
   return {run:async()=>({success:true,meta:{changes:1}})};
  }};
  throw Error('no such table');
 }};
 const oldTasks=[],oldCtx={waitUntil(p){oldTasks.push(p)}};
 await control.scheduled({}, {CONTROL_DB:withoutMigration},oldCtx);
 await Promise.all(oldTasks);
 assert.equal(oldTasks.length,1,'missing optional table does not abort actual cleanup');
});

test('failed DELETE still fails scheduled execution even when status write succeeds',async()=>{
 const calls=[];
 const db={prepare(sql){if(sql.startsWith('DELETE'))return {bind(){
  calls.push('delete');
  return {run:async()=>({success:false})};
 }};
 if(sql.startsWith('INSERT INTO control_retention_status'))return {bind(){
  calls.push('marker');
  return {run:async()=>({success:true})};
 }};
 throw Error('unknown');
 }};
 const tasks=[],ctx={waitUntil(p){tasks.push(p)}};
 await control.scheduled({}, {CONTROL_DB:db},ctx);
 await assert.rejects(Promise.all(tasks),/control_daily_retention_failed/);
 assert.deepEqual(calls,['delete','marker']);
});

test('existing authorized /api/maintenance response includes Control independently of Relay',async()=>{
 const team='https://sample.cloudflareaccess.com',aud='0123456789abcdef0123456789abcdef';
 const env={ACCESS_TEAM_DOMAIN:team,ACCESS_AUD:aud,ADMIN_EMAIL:'admin@example.invalid',
  CONTROL_DB:readDb(info(nowS-3600,1,nowS-3600,2))};
 const ctx={access:{aud,getIdentity:async()=>({email:'admin@example.invalid'})}};
 const request=new Request('https://control.example.workers.dev/api/maintenance');
 const original=Date.now;
 Date.now=()=>TIME;
 try{
  const response=await control.fetch(request,env,ctx);
  assert.equal(response.status,200);
  const result=await response.json();
  assert.equal(result.state,'not_connected','Relay remains independent');
  assert.equal(result.controlRetention.state,'ready');
  assert.equal(result.controlRetention.data.deletedLastSuccess,2);
  assert.equal((await control.fetch(request,env)).status,403);
 }finally{Date.now=original}
});

test('UI and SQL migration are minimal and the existing refresh adds no HTTP request',()=>{
 const sql=readFileSync(new URL('../migrations/0002_control_retention_status.sql',import.meta.url),'utf8');
 assert.match(sql,/CREATE TABLE IF NOT EXISTS control_retention_status/);
 assert.match(sql,/PRIMARY KEY CHECK\(id = 1\)/);
 assert.doesNotMatch(sql,/DROP|DELETE|ALTER TABLE|mavka-messages[\s\S]*CREATE TABLE/);
 for(const id of ['control-retention-pill','control-retention-status','control-retention-age',
   'control-retention-deleted','control-retention-attempt'])
  assert.ok(html.includes('id="'+id+'"'),id);
 assert.match(html,/function showControlRetention\(result\)/);
 assert.match(html,/showControlRetention\(result\?\.controlRetention\)/);
 assert.match(html,/MaVka Server Control 0\.9\.0/);
 assert.ok(html.includes('get(\\'/api/maintenance\\')'));
});
