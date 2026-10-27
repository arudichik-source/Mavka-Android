import test from 'node:test';
import assert from 'node:assert/strict';
import { Script } from 'node:vm';
import { html } from '../src/ui.js';

const opening='<script nonce="__CSP_NONCE__">';
const js=html.slice(html.indexOf(opening)+opening.length,html.indexOf('</script>'));
const begin=js.indexOf('function sourceHealthTime(');
const after=js.indexOf('function telemetrySourceRows(',begin);
assert.ok(begin>=0&&after>begin,'diagnosis is a pure client-side function');
const diagnose=new Function('fmt',js.slice(begin,after)+'\nreturn diagnoseCrons;')(
 n=>Number.isFinite(n)?String(n):'—');
const time=iso=>Date.parse(iso);
const seconds=iso=>Math.floor(time(iso)/1000);
const relay=(when,pruned=true)=>({state:'ready',data:{
 checkedAt:seconds(when),securityPruneSucceeded:pruned}});
const control=(when,override={})=>({state:'ready',data:{
 lastSuccessAt:seconds(when),lastAttemptAt:seconds(when),
 lastAttemptOk:true,deletedLastSuccess:0,...override}});
const both=(rel,c)=>({...rel,controlRetention:c});

test('UTC daily due slot honours two-hour grace immediately after 03:17',()=>{
 const now=time('2026-10-28T04:00:00Z');
 const result=diagnose(both(relay('2026-10-28T03:45:00Z'),control('2026-10-27T03:20:00Z')),now);
 assert.equal(new Date(result.lastDue).toISOString(),'2026-10-27T03:17:00.000Z');
 assert.equal(new Date(result.nextDue).toISOString(),'2026-10-29T03:17:00.000Z');
 assert.equal(result.control.level,'ok');
 assert.equal(result.relay.level,'ok');
 assert.equal(result.overall,'ok');
});

test('missed daily confirmation is warned after 05:17 UTC, earlier than 36h stale age',()=>{
 const now=time('2026-10-28T05:18:00Z');
 const result=diagnose(both(relay('2026-10-28T05:10:00Z'),control('2026-10-27T03:20:00Z')),now);
 assert.equal(result.control.level,'warn');
 assert.equal(result.control.reason,'missed_confirmation');
 assert.match(result.control.detail,/можливий пропуск|Можливий пропуск/);
 assert.equal(result.overall,'warn');
 assert.equal(new Date(result.lastDue).toISOString(),'2026-10-28T03:17:00.000Z');
});

test('a real success at scheduled slot preserves green status even if deleted zero rows',()=>{
 const now=time('2026-10-28T05:20:00Z');
 const r=diagnose(both(relay('2026-10-28T04:50:00Z'),control('2026-10-28T03:17:45Z')),now);
 assert.equal(r.control.level,'ok');
 assert.equal(r.control.reason,'fresh');
 assert.match(r.control.detail,/видалено: 0/);
 assert.equal(r.relay.level,'ok');
});

test('no initial Control row cannot prove a missed Cron; migration time unknown',()=>{
 const now=time('2026-11-01T12:00:00Z');
 const r=diagnose(both(relay('2026-11-01T11:45:00Z'),
   {state:'not_connected',reason:'awaiting_first_run',data:null}),now);
 assert.equal(r.control.level,'missing');
 assert.equal(r.control.reason,'unknown');
 assert.match(r.control.detail,/Час її створення невідомий/);
 assert.equal(r.overall,'missing');
 const absent=diagnose(null,now);
 assert.equal(absent.control.level,'missing');
 assert.equal(absent.relay.level,'missing');
});

test('failed latest daily attempt overrides an older success and preserves warning',()=>{
 const now=time('2026-10-28T07:00:00Z');
 const previous=control('2026-10-27T03:18:00Z',{
 lastAttemptAt:seconds('2026-10-28T03:18:00Z'),lastAttemptOk:false});
 const r=diagnose(both(relay('2026-10-28T06:45:00Z'),{...previous,state:'failed'}),now);
 assert.equal(r.control.level,'warn');
 assert.equal(r.control.reason,'failed');
 assert.match(r.control.detail,/невдала/);
});

test('Relay heartbeat older than 90m, failed security pruning, and absent heartbeat are distinct',()=>{
 const now=time('2026-10-28T12:00:00Z'),good=control('2026-10-28T03:20:00Z');
 let r=diagnose(both(relay('2026-10-28T10:29:00Z'),good),now);
 assert.equal(r.relay.level,'warn');
 assert.equal(r.relay.reason,'stale');
 r=diagnose(both(relay('2026-10-28T11:45:00Z',false),good),now);
 assert.equal(r.relay.reason,'prune_unconfirmed');
 r=diagnose({state:'not_connected',controlRetention:good},now);
 assert.equal(r.relay.level,'missing');
 assert.equal(r.control.level,'ok');
});

test('incorrect and future timestamps never become a healthy confirmation',()=>{
 const now=time('2026-10-28T12:00:00Z');
 const c=control('2026-10-29T03:17:00Z');
 const r=diagnose(both(relay('2026-10-29T03:17:00Z'),c),now);
 assert.equal(r.control.level,'missing');
 assert.equal(r.relay.level,'missing');
 assert.equal(r.overall,'missing');
});

test('responsive UI reflects independent diagnostics and uses existing maintenance response',()=>{
 assert.doesNotThrow(()=>new Script(js));
 for(const id of ['cron-diagnostics','cron-overall-status','cron-relay-level',
   'cron-control-level','cron-relay-detail','cron-control-detail','cron-schedule-detail'])
  assert.equal((html.match(new RegExp('id="'+id+'"','g'))||[]).length,1,id);
 assert.match(html,/renderCronDiagnostics\(result\)/);
 assert.match(html,/showControlRetention\(result\?\.controlRetention,diagnostic.control\)/);
 assert.match(html,/renderCronDiagnostics\(result\)[\s\S]*?showControlRetention/);
 assert.match(html,/MaVka Server Control 0\.9\.1/);
 const segment=js.slice(begin,js.indexOf('function renderSourceHealth(',begin));
 assert.doesNotMatch(segment,/fetch\(|\bget\(|setInterval|setTimeout|sendBeacon/);
 const refresh=js.slice(js.indexOf('async function refresh('),js.indexOf('async function setPeriod('));
 assert.equal((refresh.match(/get\('\/api\//g)||[]).length,4);
 assert.match(refresh,/get\('\/api\/maintenance'\)/);
});
