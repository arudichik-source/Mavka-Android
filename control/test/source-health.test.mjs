import test from 'node:test';
import assert from 'node:assert/strict';
import { Script } from 'node:vm';
import { html } from '../src/ui.js';

const marker='<script nonce="__CSP_NONCE__">';
const script=html.slice(html.indexOf(marker)+marker.length,html.indexOf('</script>'));
const start=script.indexOf('function sourceHealthTime(');
const end=script.indexOf('function renderSourceHealth(',start);
assert.ok(start>=0&&end>start,'pure health status functions are present');
const evaluate=new Function('fmt','activePeriod',script.slice(start,end)+
 '\nreturn telemetrySourceRows;')(v=>Number.isFinite(v)?String(v):'—','24h');
const fulfilled=value=>({status:'fulfilled',value});
const unavailable=()=>({status:'rejected',reason:new Error('network failure')});
const NOW=Date.parse('2026-10-27T12:00:00Z');
const secs=minutes=>Math.floor((NOW-minutes*60000)/1000);
const iso=minutes=>new Date(NOW-minutes*60000).toISOString();
const complete=()=>[
 fulfilled({
  state:'ready',at:iso(3),
  workers:{requestsToday:0,errorsToday:0},
  d1:{rowsReadToday:0,rowsWrittenToday:0},
  r2:{storage:{bytes:0,objects:0,observedAt:iso(3)},
   operations:{requestsThisMonth:0}}
 }),
 fulfilled({state:'ready',entries:[]}),
 fulfilled({state:'ready',entries:[]}),
 fulfilled({state:'ready',coverage:'best_effort',observedAt:secs(3),total24h:0,total7d:0}),
 fulfilled({state:'ready',observedAt:secs(3),data:{mobileActive:0,mobileSeenRecently:0}}),
 fulfilled({state:'ready',data:{checkedAt:secs(3),securityPruneSucceeded:true,
  r2Audit:{state:'ready',data:{auditedAt:secs(60),sampledObjects:0,sampledBytes:0,
   truncated:false,overdueObjects:0}}},
  controlRetention:{state:'ready',data:{lastAttemptAt:secs(60),lastSuccessAt:secs(60),deletedLastSuccess:0}}})
];
const byName=rows=>Object.fromEntries(rows.map(x=>[x.name,x]));

test('all 11 existing telemetry feeds are represented without new network calls',()=>{
 const rows=evaluate(complete(),NOW);
 assert.equal(rows.length,11);
 assert.deepEqual(rows.map(x=>x.name),[
  'workers','d1','r2-storage','r2-ops','history','trends',
  'security','devices','cleanup','control-retention','r2-audit']);
 assert.ok(rows.every(x=>x.level==='ok'));
 const section=script.slice(start,script.indexOf('function showR2Audit(',end));
 assert.doesNotMatch(section,/\bfetch\s*\(|\bget\s*\(\s*['"]\/api|sendBeacon|WebSocket/);
 assert.match(html,/id="source-health-grid"/);
 assert.match(html,/id="source-health-summary"/);
 assert.match(html,/href="#source-health"/);
 assert.equal((html.match(/id="source-health"/g)||[]).length,1);
});
test('legitimate numeric zero is confirmed data, not a false unavailable signal',()=>{
 const r=byName(evaluate(complete(),NOW));
 for(const name of ['workers','d1','r2-storage','r2-ops','security','devices','control-retention','r2-audit'])
  assert.equal(r[name].level,'ok',name);
 assert.match(r.history.description,/0/);
 assert.match(r['r2-storage'].description,/знімок/i);
});
test('stale checkpoint, timestamps, truncated audit and failed prune are warnings',()=>{
 const data=complete();
 data[0].value.at=iso(35);
 data[0].value.r2.storage.observedAt=iso(26*60);
 data[3].value.observedAt=secs(33);
 data[4].value.observedAt=secs(36);
 data[5].value.state='stale';
 data[5].value.data.securityPruneSucceeded=false;
 data[5].value.data.r2Audit.state='stale';
 data[5].value.controlRetention.state='stale';
 data[5].value.data.r2Audit.data.truncated=true;
 data[5].value.data.r2Audit.data.overdueObjects=4;
 const r=byName(evaluate(data,NOW));
 for(const name of ['workers','d1','r2-storage','r2-ops','security','devices','cleanup','control-retention','r2-audit'])
  assert.equal(r[name].level,'warn',name);
 assert.equal(r.history.level,'ok');
 assert.equal(r.trends.level,'ok');
 assert.match(r['r2-audit'].description,/неповна вибірка/);
});
test('no data or disconnected services never masquerade as zero or healthy',()=>{
 const allMissing=byName(evaluate(Array.from({length:6},unavailable),NOW));
 assert.ok(Object.values(allMissing).every(x=>x.level==='missing'));
 const partial=complete();
 partial[0].value={
  state:'partial',at:iso(1),workers:null,d1:{rowsReadToday:0,rowsWrittenToday:0},
  r2:{storage:{bytes:null,objects:null},operations:null}
 };
 partial[1]=fulfilled({state:'not_bound',entries:[]});
 partial[2]=fulfilled({state:'database_unavailable',entries:[]});
 partial[3]=fulfilled({state:'not_connected'});
 partial[4]=fulfilled({state:'not_connected'});
 partial[5]=fulfilled({state:'not_connected'});
 const r=byName(evaluate(partial,NOW));
 assert.equal(r.d1.level,'ok');
 for(const name of ['workers','r2-storage','r2-ops','history','trends','security','devices','cleanup','control-retention','r2-audit'])
  assert.equal(r[name].level,'missing',name);
});
test('future or invalid remote timestamps never appear healthy or crash display',()=>{
 const data=complete();
 data[3].value.observedAt=secs(-300);
 data[4].value.observedAt=Number.MAX_SAFE_INTEGER;
 const r=byName(evaluate(data,NOW));
 assert.equal(r.security.level,'missing');
 assert.equal(r.devices.level,'missing');
 assert.doesNotThrow(()=>new Script(script));
});
test('full dashboard refresh and period changes reuse existing six API calls',()=>{
 assert.match(script,/sourceHealthResults=results;/);
 assert.match(script,/renderSourceHealth\(sourceHealthResults\)/);
 assert.match(script,/sourceHealthResults\[1\]=reads\[0\]/);
 assert.match(script,/sourceHealthResults\[2\]=reads\[1\]/);
 assert.match(script,/if\(data\.state!=='ready'\).*return data/);
 assert.match(script,/drawTrends\(t\);\s*return t;/);
 assert.match(script,/900000/);
 assert.match(html,/MaVka Server Control 1\.0\.0/);
});
