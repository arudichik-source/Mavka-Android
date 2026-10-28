import test from 'node:test';
import assert from 'node:assert/strict';
import { Script } from 'node:vm';
import { html } from '../src/ui.js';

const marker='<script nonce="__CSP_NONCE__">';
const script=html.slice(html.indexOf(marker)+marker.length,html.indexOf('</script>'));
const healthStart=script.indexOf('function sourceHealthTime(');
const healthEnd=script.indexOf('function renderSourceHealth(',healthStart);
const evaluate=new Function('fmt','activePeriod',script.slice(healthStart,healthEnd)+
 '\nreturn telemetrySourceRows;')(n=>Number.isFinite(n)?String(n):'—','24h');
const start=script.indexOf("const EVENT_KEY='mavka-control.technical-events.v1'");
const end=script.indexOf('let technicalJournal=',start);
assert.ok(start>=0&&end>start,'event journal is embedded before UI handlers');
const {EVENT_CATALOG,safeEvent,boundedEvents,updateEventJournal,filterEventJournal}=
 new Function('telemetrySourceRows',script.slice(start,end)+
  '\nreturn {EVENT_CATALOG,safeEvent,boundedEvents,updateEventJournal,filterEventJournal};')(evaluate);
const NOW=Date.parse('2026-10-28T12:00:00Z');
const sec=m=>Math.floor((NOW-m*60000)/1000);
const iso=m=>new Date(NOW-m*60000).toISOString();
const fulfill=value=>({status:'fulfilled',value});
const ready=()=>[
 fulfill({state:'ready',at:iso(3),
  workers:{requestsToday:20,errorsToday:0},
  d1:{rowsReadToday:30,rowsWrittenToday:2},
  r2:{storage:{bytes:0,objects:0,observedAt:iso(12)},
   operations:{requestsThisMonth:5}},
  quotaAlerts:{state:'ready',warnings:[],missing:[]}}),
 fulfill({state:'ready',entries:[]}),
 fulfill({state:'ready',entries:[]}),
 fulfill({state:'ready',coverage:'best_effort',observedAt:sec(3),
  risk:{state:'ready',incidents:[]}}),
 fulfill({state:'ready',observedAt:sec(3),data:{mobileActive:0}}),
 fulfill({state:'ready',data:{checkedAt:sec(4),securityPruneSucceeded:true,
  r2Audit:{state:'ready',data:{auditedAt:sec(90),sampledObjects:0,
   sampledBytes:0,truncated:false,overdueObjects:0}}},
  controlRetention:{state:'ready',data:{lastAttemptAt:sec(120),
   lastSuccessAt:sec(120),deletedLastSuccess:0}}})
];
const empty=()=>({events:[],states:{}});

test('first valid green snapshot does not invent technical events',()=>{
 const j=updateEventJournal(empty(),ready(),NOW);
 assert.equal(j.events.length,0);
 assert.equal(Object.keys(j.states).length,15,'11 source plus 4 quota states');
 assert.ok(Object.values(j.states).every(x=>x==='ok'));
});

test('unknown API status is an observed warning, not an outage claim',()=>{
 const data=ready();data[4]=fulfill({state:'not_connected'});
 const j=updateEventJournal(empty(),data,NOW);
 assert.equal(j.events.length,1);
 assert.deepEqual(j.events[0],{code:'source.devices',at:NOW,
  severity:'warning',phase:'issue'});
 assert.equal(updateEventJournal(j,data,NOW+900000).events.length,1);
});

test('restoration creates a single information event after an issue',()=>{
 const data=ready();data[1]=fulfill({state:'not_bound',entries:[]});
 const first=updateEventJournal(empty(),data,NOW);
 const recovered=updateEventJournal(first,ready(),NOW+900000);
 assert.equal(recovered.events.length,2);
 assert.equal(recovered.events[0].code,'source.history');
 assert.equal(recovered.events[0].phase,'restored');
 assert.equal(recovered.events[0].severity,'info');
 assert.equal(updateEventJournal(recovered,ready(),NOW+900000).events.length,2);
});

test('failed Control Cron is critical and does not overwrite Relay freshness',()=>{
 const data=ready();data[5].value.controlRetention.state='failed';
 const j=updateEventJournal(empty(),data,NOW);
 assert.ok(j.events.some(e=>e.code==='source.control-retention'&&e.severity==='critical'));
 assert.ok(!j.events.some(e=>e.code==='source.cleanup'));
});

test('quota thresholds are observed only with legitimate measured limits',()=>{
 const data=ready();data[0].value.quotaAlerts={
  state:'ready',warnings:[{key:'workers',severity:'warning'},
   {key:'r2_bytes',severity:'critical'}],missing:[]};
 let j=updateEventJournal(empty(),data,NOW);
 assert.equal(j.events.filter(x=>x.code.startsWith('quota.')).length,2);
 assert.ok(j.events.some(x=>x.code==='quota.r2_bytes'&&x.severity==='critical'));
 const unknown=ready();unknown[0].value.quotaAlerts={
  state:'partial',warnings:[],missing:['workers']};
 j=updateEventJournal(j,unknown,NOW+900000);
 assert.ok(!j.events.some(x=>x.code==='quota.workers'&&x.phase==='restored'));
});

test('allowlisted daily risk aggregates keep day UTC, never fabricated hour',()=>{
 const data=ready();
 const day=Math.floor(NOW/86400000)*86400;
 data[3].value.risk={state:'ready',incidents:[
  {code:'request_replayed',severity:'critical',dayStart:day,count:3,deviceId:'SECRET'},
  {code:'raw_private_payload',severity:'critical',dayStart:day,count:500}
 ]};
 const first=updateEventJournal(empty(),data,NOW);
 assert.equal(first.events.length,1);
 assert.deepEqual(first.events[0],{code:'risk.request_replayed',at:day*1000,
  severity:'critical',phase:'signal'});
 const twice=updateEventJournal(first,data,NOW+60000);
 assert.equal(twice.events.length,1);
 assert.doesNotMatch(JSON.stringify(twice),/SECRET|raw_private_payload|deviceId/);
});

test('browser records are strict whitelist; no network text or arbitrary fields persist',()=>{
 const fake={code:'source.workers',at:NOW,severity:'warning',phase:'issue',
  raw:'authorization: secret',ip:'203.0.113.7',token:'1234'};
 assert.deepEqual(safeEvent(fake,NOW),{code:'source.workers',at:NOW,
  severity:'warning',phase:'issue'});
 assert.equal(safeEvent({...fake,code:'<script>alert(1)</script>'},NOW),null);
 assert.equal(safeEvent({...fake,at:NOW+3600000},NOW),null);
 assert.equal(safeEvent({...fake,at:NOW-15*86400000},NOW),null);
 assert.equal(safeEvent({...fake,phase:'signal'},NOW),null);
 assert.doesNotMatch(JSON.stringify(boundedEvents([fake],NOW)),/authorization|secret|token|203/);
});

test('14-day pruning, cap 120 and stable duplicate suppression',()=>{
 const rows=Array.from({length:150},(_,i)=>({code:'source.workers',
  at:NOW-i*60000,severity:'warning',phase:'issue'}));
 rows.push(rows[0],{code:'source.workers',at:NOW-20*86400000,
  severity:'critical',phase:'issue'});
 const kept=boundedEvents(rows,NOW);
 assert.equal(kept.length,120);
 assert.equal(new Set(kept.map(e=>e.code+e.at+e.phase)).size,120);
 assert.ok(kept.every(e=>e.at>=NOW-14*86400000));
});

test('severity, period and source selectors operate only on bounded local records',()=>{
 const rows=[
  {code:'quota.r2_bytes',at:NOW-60000,severity:'critical',phase:'issue'},
  {code:'source.d1',at:NOW-5*86400000,severity:'warning',phase:'issue'},
  {code:'source.workers',at:NOW-8*86400000,severity:'info',phase:'restored'}
 ];
 assert.equal(filterEventJournal(rows,{severity:'critical',period:'24h'},NOW).length,1);
 assert.equal(filterEventJournal(rows,{group:'d1',period:'7d'},NOW).length,1);
 assert.equal(filterEventJournal(rows,{group:'workers',period:'14d'},NOW).length,1);
 assert.equal(filterEventJournal(rows,{period:'24h'},NOW).length,1);
});

test('event client remains on same six reads without timers, extra APIs, or D1 writes',()=>{
 assert.doesNotThrow(()=>new Script(script));
 for(const id of ['technical-events','tech-events-list','tech-events-clear',
  'tech-filter-severity','tech-filter-period','tech-filter-group'])
  assert.equal(html.split('id="'+id+'"').length-1,1,id);
 const segment=script.slice(start,script.indexOf('// Conservative, browser-only projections',end));
 assert.doesNotMatch(segment,/\bfetch\s*\(|\bget\s*\(\s*['"]\/api|setInterval|setTimeout|WebSocket|sendBeacon/);
 assert.match(segment,/localStorage\.setItem\(EVENT_KEY,JSON\.stringify\(technicalJournal\)\)/);
 assert.match(segment,/technicalJournal\.events=\[\]/);
 const refresh=script.slice(script.indexOf('async function refresh('),script.indexOf('async function setPeriod('));
 assert.match(refresh,/ingestTechnicalEvents\(sourceHealthResults\)/);
 assert.equal((refresh.match(/get\('\/api\//g)||[]).length,4);
 assert.match(html,/MaVka Server Control 1\.0\.0/);
});
