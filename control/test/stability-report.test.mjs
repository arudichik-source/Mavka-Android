import test from 'node:test';
import assert from 'node:assert/strict';
import { Script } from 'node:vm';
import { html } from '../src/ui.js';

const marker='<script nonce="__CSP_NONCE__">';
const js=html.slice(html.indexOf(marker)+marker.length,html.indexOf('</script>'));
const start=js.indexOf('const STABILITY_PERIODS=');
const stop=js.indexOf('let currentStabilityReview=',start);
assert.ok(start>=0&&stop>start);
const whitelist={'source.workers':['Workers · аналітика Relay','workers'],
 'source.d1':['D1 · використання','d1'],
 'source.devices':['Relay · пристрої','relay']};
const safe=entries=>(entries||[]).filter(e=>
 typeof e?.code==='string'&&Object.hasOwn(whitelist,e.code));
const funcs=new Function('boundedEvents','EVENT_CATALOG',js.slice(start,stop)+
 '\nreturn {stabilityDailyObservations,stabilityReview,stabilityReportText};')(safe,whitelist);
const {stabilityDailyObservations,stabilityReview,stabilityReportText}=funcs;
const NOW=Date.parse('2026-10-28T12:00:00Z');
const sec=(day,hour)=>Math.floor(Date.parse(day+'T00:00:00Z')/1000+hour*3600);
const obs=(day,hour,worker,errors,read,write,bytes=0)=>({
 bucket_start:sec(day,hour),workers_requests:worker,workers_errors:errors,
 d1_rows_read:read,d1_rows_written:write,r2_payload_bytes:bytes});
const trend=(period,entries)=>({state:'ready',period,entries});
const sources=()=>[
 {name:'workers',level:'ok'},{name:'d1',level:'ok'},
 {name:'history',level:'ok'},{name:'trends',level:'ok'},
 {name:'control-retention',level:'ok'},
 {name:'r2-storage',level:'ok'},{name:'r2-ops',level:'ok'},
 {name:'r2-audit',level:'ok'},{name:'security',level:'ok'},
 {name:'devices',level:'ok'},{name:'cleanup',level:'ok'}];

test('missing history cannot be represented as zero usage, uptime or a valid comparison',()=>{
 const report=stabilityReview(null,[],[],'24h',NOW);
 assert.equal(report.sampling.status,'unavailable');
 assert.equal(report.sampling.samples,0);
 assert.ok(report.counters.every(c=>c.current===null&&c.change===null));
 assert.equal(report.comparison,false);
 assert.ok(report.groups.every(g=>g.status==='unknown'));
 const output=stabilityReportText(report,NOW);
 assert.match(output,/не uptime/);
 assert.match(output,/недостатньо сусідніх днів/);
});

test('one UTC day of valid samples gives peaks without pretending to have a comparison',()=>{
 const rows=[obs('2026-10-28',9,15,0,100,4,800),
  obs('2026-10-28',10,23,1,120,5,900),
  obs('2026-10-28',11,20,0,130,6,850)];
 const res=stabilityDailyObservations(trend('24h',rows),'24h',NOW);
 assert.equal(res.samples,3);
 assert.equal(res.days.length,1);
 assert.equal(res.days[0].peaks.workers_requests,23);
 assert.equal(res.days[0].peaks.d1_rows_read,130);
 assert.equal(res.days[0].r2Bytes,850,'R2 latest, not max');
 assert.equal(stabilityReview(trend('24h',rows),sources(),[],'24h',NOW).comparison,false);
});

test('consecutive UTC days compare maxima of observed cumulative counters, not daily totals',()=>{
 const rows=[obs('2026-10-27',21,30,3,250,15),
  obs('2026-10-27',22,40,4,300,17),
  obs('2026-10-28',9,25,0,200,6),
  obs('2026-10-28',11,50,2,350,10)];
 const report=stabilityReview(trend('7d',rows),sources(),[],'7d',NOW);
 assert.equal(report.comparison,true);
 assert.deepEqual(report.comparedDates,['2026-10-27','2026-10-28']);
 const worker=report.counters.find(c=>c.key==='workers_requests');
 assert.deepEqual([worker.previous,worker.current,worker.change,worker.percent],
  [40,50,10,25]);
 assert.equal(report.groups.find(g=>g.name==='Relay').status,'ok');
 assert.match(stabilityReportText(report,NOW),/пікових СПОСТЕРЕЖЕНИХ/i);
});

test('nonconsecutive days and absent per-metric samples do not manufacture percentages',()=>{
 const rows=[obs('2026-10-20',9,3,0,4,0),
  obs('2026-10-28',11,9,0,14,1)];
 const report=stabilityReview(trend('30d',rows),sources(),[],'30d',NOW);
 assert.equal(report.comparison,false);
 assert.equal(report.counters[0].change,null);
 const sparse=[{bucket_start:sec('2026-10-27',8),workers_requests:8},
  {bucket_start:sec('2026-10-28',9),workers_requests:9}];
 const result=stabilityReview(trend('7d',sparse),sources(),[],'7d',NOW);
 assert.equal(result.counters.find(c=>c.key==='d1_rows_read').current,null);
 assert.equal(result.counters.find(c=>c.key==='d1_rows_read').change,null);
});

test('bad timestamps, future samples, wrong trend period and invalid values are ignored',()=>{
 const entries=[obs('2026-10-28',11,-3,0,10,2),
  obs('2026-10-30',11,99,1,1,1),{bucket_start:'2026-10-28'},
  obs('2026-10-28',10,20,2,30,4)];
 const good=stabilityDailyObservations(trend('24h',entries),'24h',NOW);
 assert.equal(good.samples,2);
 assert.equal(good.days[0].peaks.workers_requests,20);
 const wrong=stabilityDailyObservations(trend('7d',entries),'24h',NOW);
 assert.equal(wrong.status,'unavailable');
});

test('partial source availability stays explicit and never claims uptime',()=>{
 const rows=sources().filter(x=>x.name!=='cleanup')
  .map(x=>x.name==='devices'?{...x,level:'warn'}:x);
 const r=stabilityReview(null,rows,[],'24h',NOW);
 const relay=r.groups.find(g=>g.name==='Relay');
 assert.equal(relay.warn,1);
 assert.equal(relay.missing,1);
 assert.equal(relay.status,'warn');
 assert.equal(r.groups.find(g=>g.name==='Workers').status,'ok');
 assert.equal(r.sourceSnapshotOnly,true);
});

test('locally observed repeated issue transitions and recovery counts are grouped',()=>{
 const events=[
  {code:'source.devices',at:NOW-120000,severity:'warning',phase:'issue',ip:'SECRET'},
  {code:'source.devices',at:NOW-110000,severity:'info',phase:'restored'},
  {code:'source.devices',at:NOW-100000,severity:'warning',phase:'issue'},
  {code:'source.d1',at:NOW-50000,severity:'critical',phase:'issue'},
  {code:'source.workers',at:NOW-10*86400000,severity:'warning',phase:'issue'}
 ];
 const r=stabilityReview(null,sources(),events,'7d',NOW);
 assert.equal(r.counts.issues,3);
 assert.equal(r.counts.restored,1);
 assert.equal(r.recurring[0].code,'source.devices');
 assert.equal(r.recurring[0].count,2);
 const text=stabilityReportText(r,NOW);
 assert.doesNotMatch(text,/SECRET|203\.0\.113|token|ip:/);
});

test('30-day report explicitly limits local event window to 14 days',()=>{
 const r=stabilityReview(null,sources(),[
  {code:'source.d1',at:NOW-15*86400000,severity:'warning',phase:'issue'},
  {code:'source.d1',at:NOW-13*86400000,severity:'warning',phase:'issue'}
 ],'30d',NOW);
 assert.equal(r.eventCoverageDays,14);
 assert.equal(r.counts.issues,1);
});

test('stability UI reuses existing period control, no additional API, background jobs or storage',()=>{
 assert.doesNotThrow(()=>new Script(js));
 for(const id of ['stability','stability-sources','stability-period','stability-export',
  'stability-counters','stability-events','stability-recurring'])
  assert.equal(html.split('id="'+id+'"').length-1,1,id);
 assert.match(js,/if\(e\.target\.value!==activePeriod\)setPeriod\(e\.target\.value\)/);
 assert.match(js,/renderStabilityReview\(\)/);
 const portion=js.slice(start,js.indexOf('// Conservative, browser-only projections',start));
 assert.doesNotMatch(portion,/\bfetch\s*\(|\bget\s*\(\s*['"]\/api|setInterval|WebSocket|sendBeacon|localStorage\.setItem/);
 const refresh=js.slice(js.indexOf('async function refresh('),js.indexOf('async function setPeriod('));
 assert.equal((refresh.match(/get\('\/api\//g)||[]).length,6);
 assert.match(refresh,/get\('\/api\/limits'\)/);
  assert.match(refresh,/get\('\/api\/oracle'\)/);
 assert.match(html,/MaVka Server Control 1\.1\.0/);
});
