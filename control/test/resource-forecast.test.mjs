import test from 'node:test';
import assert from 'node:assert/strict';
import { Script } from 'node:vm';
import { html } from '../src/ui.js';

const start=html.indexOf('<script nonce="__CSP_NONCE__">');
const end=html.indexOf('</script>',start);
const script=html.slice(start+'<script nonce="__CSP_NONCE__">'.length,end);
const from=script.indexOf('function resourceUsageForecast(');
const to=script.indexOf('function renderUsageForecast(',from);
assert.ok(from>=0 && to>from);
const predict=new Function(script.slice(from,to)+'\nreturn resourceUsageForecast;')();
const NOW=Date.parse('2026-10-27T12:00:00Z');
const midnight=Date.parse('2026-10-27T00:00:00Z');
const sec=hour=>Math.floor((midnight+hour*3600000)/1000);
const row=(hour,w=0,r=0,wr=0,bytes=0)=>({
 bucket_start:sec(hour),workers_requests:w,d1_rows_read:r,d1_rows_written:wr,r2_payload_bytes:bytes
});
const summary=(workers=30,reads=30,writes=30,bytes=3200000000)=>({
 at:new Date(NOW-3*60000).toISOString(),
 workers:{requestsToday:workers},
 d1:{rowsReadToday:reads,rowsWrittenToday:writes},
 r2:{storage:{bytes,observedAt:new Date(NOW-10*60000).toISOString()}},
 limits:{workersRequestsPerDay:100,
  d1RowsReadPerDay:100,d1RowsWrittenPerDay:100,r2MonitoringBytes:5000000000}
});
const samples=()=>({state:'ready',entries:[
 row(9,10,10,10,3100000000),
 row(10.5,20,20,20,3150000000),
 row(11.75,30,30,30,3200000000)
]});
const keyed=entries=>Object.fromEntries(entries.map(x=>[x.name,x]));

test('fresh valid same-day samples yield four bounded forecasts and warn before limits',()=>{
 const result=keyed(predict(summary(),samples(),NOW));
 assert.deepEqual(Object.keys(result),['workers','d1-reads','d1-writes','r2']);
 for(const key of Object.keys(result)){
  assert.ok(result[key].estimate>result[key].actual,key);
  assert.ok(result[key].rate>0,key);
  assert.equal(result[key].status,'warn',key);
  assert.match(result[key].message,/70%/);
 }
 assert.equal(result.workers.projectionHours,12);
 assert.equal(result.r2.projectionHours,24);
 assert.match(result.r2.detail,/24 год/);
});

test('genuine zero counts and zero velocity project zero, not unavailable',()=>{
 const zeroRows={state:'ready',entries:[row(9),row(10.5),row(11.75)]};
 const r=keyed(predict(summary(0,0,0,0),zeroRows,NOW));
 for(const name of ['workers','d1-reads','d1-writes','r2']){
  assert.equal(r[name].estimate,0,name);
  assert.equal(r[name].rate,0,name);
  assert.equal(r[name].status,'ok',name);
 }
});

test('insufficient samples, short coverage, missing D1 and stale data never extrapolate',()=>{
 const onlyTwo={state:'ready',entries:[row(9,10),row(11.75,20)]};
 assert.equal(keyed(predict(summary(),onlyTwo,NOW)).workers.estimate,null);
 const short={state:'ready',entries:[row(11,10),row(11.5,20),row(11.75,30)]};
 assert.equal(keyed(predict(summary(),short,NOW)).workers.estimate,null);
 const stale={state:'ready',entries:[row(7,10),row(8,20),row(9,30)]};
 assert.equal(keyed(predict(summary(),stale,NOW)).workers.estimate,null);
 assert.equal(keyed(predict(summary(),{state:'not_bound'},NOW)).workers.estimate,null);
 const oldSummary=summary();
 oldSummary.at=new Date(NOW-3600000).toISOString();
 assert.equal(keyed(predict(oldSummary,samples(),NOW)).workers.estimate,null);
});

test('UTC counter reset and summary/trend disagreement are fail-closed',()=>{
 const reset={state:'ready',entries:[
  row(9,20,20,20),row(10.5,5,5,5),row(11.75,30,30,30)]};
 const r=keyed(predict(summary(),reset,NOW));
 for(const name of ['workers','d1-reads','d1-writes']){
  assert.equal(r[name].estimate,null,name);
  assert.match(r[name].message,/обнулення/);
 }
 const inconsistent=summary(20,20,20);
 const r2=keyed(predict(inconsistent,samples(),NOW));
 for(const name of ['workers','d1-reads','d1-writes'])
  assert.equal(r2[name].estimate,null,name);
});

test('R2 decrease or stale storage timestamp does not give growth forecast',()=>{
 const descending={state:'ready',entries:[
  row(9,10,10,10,3200000000),
  row(10.5,20,20,20,3150000000),
  row(11.75,30,30,30,3000000000)
 ]};
 const shrinking=keyed(predict(summary(30,30,30,3000000000),descending,NOW)).r2;
 assert.equal(shrinking.estimate,null);
 assert.match(shrinking.message,/зменшується/);
 const stale=summary();
 stale.r2.storage.observedAt=new Date(NOW-25*3600000).toISOString();
 assert.equal(keyed(predict(stale,samples(),NOW)).r2.estimate,null);
});

test('never use previous-day observations in today cumulative daily forecast',()=>{
 const cross={state:'ready',entries:[
  {bucket_start:Math.floor((midnight-7200000)/1000),workers_requests:80,
   d1_rows_read:80,d1_rows_written:80,r2_payload_bytes:3000000000},
  {bucket_start:Math.floor((midnight-3600000)/1000),workers_requests:95,
   d1_rows_read:95,d1_rows_written:95,r2_payload_bytes:3050000000},
  row(11.75,30,30,30,3200000000)
 ]};
 const r=keyed(predict(summary(),cross,NOW));
 for(const name of ['workers','d1-reads','d1-writes'])
  assert.equal(r[name].estimate,null,name);
});

test('existing refreshes reuse cached summary/trends without new API calls',()=>{
 const forecasting=script.slice(from,script.indexOf('function showR2Audit(',from));
 assert.doesNotMatch(forecasting,/\bfetch\s*\(|\bget\s*\(|sendBeacon|XMLHttpRequest|WebSocket/);
 assert.match(script,/renderUsageForecast\(sourceHealthResults\)/);
 assert.match(script,/900000/);
 assert.match(html,/href="#forecast"/);
 assert.match(html,/id="forecast-grid"/);
 assert.match(html,/MaVka Server Control 0\.9\.0/);
 assert.doesNotThrow(()=>new Script(script));
});
