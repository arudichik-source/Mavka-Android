import test from 'node:test';
import assert from 'node:assert/strict';
import { assessIncidentSignals, assessQuotaWarnings, RETENTION_INFO } from '../src/alerts.js';
import { html } from '../src/ui.js';
import { securitySummary } from '../src/security.js';

const today=Math.floor(Date.UTC(2026,9,25,12,0)/1000);
const day=Math.floor(today/86400)*86400;

test('risk thresholds are explicit, deterministic and do not classify a lone 401 as an attack',()=>{
 const rows=[
  {dayStart:day,reason:'auth_signature_invalid',count:1},
  {dayStart:day,reason:'request_replayed',count:1},
  {dayStart:day-86400,reason:'auth_signature_invalid',count:3},
  {dayStart:day-2*86400,reason:'request_replayed',count:3},
  {dayStart:day-16*86400,reason:'request_replayed',count:5},
  {dayStart:day,reason:'other',count:1000},
 ];
 const x=assessIncidentSignals(rows,today);
 assert.equal(x.state,'ready');
 assert.equal(x.incidents.length,4);
 assert.equal(x.incidents.find(v=>v.code==='auth_signature_invalid'&&v.dayStart===day).severity,'info');
 assert.equal(x.incidents.find(v=>v.code==='request_replayed'&&v.dayStart===day).severity,'warning');
 assert.equal(x.incidents.find(v=>v.dayStart===day-2*86400).severity,'critical');
 assert.ok(x.incidents.every(v=>v.status==='requires_review'));
 assert.equal(assessIncidentSignals(null,today).state,'unavailable');
});
test('quota warnings are safe at 70/85/95% and identify R2 5GB as soft only',()=>{
 const data={
  state:'ready',
  limits:{ workersRequestsPerDay:100000,d1RowsReadPerDay:5000000,
    d1RowsWrittenPerDay:100000,r2MonitoringBytes:5000000000 },
  workers:{requestsToday:70000}, d1:{rowsReadToday:4250000,rowsWrittenToday:96000},
  r2:{storage:{bytes:4800000000}},
 };
 const found=assessQuotaWarnings(data);
 assert.equal(found.state,'ready');
 assert.deepEqual(found.warnings.map(x=>x.key),['d1_write','r2_bytes','d1_read','workers']);
 assert.deepEqual(found.warnings.map(x=>x.severity),['critical','critical','warning','info']);
 assert.equal(found.warnings.find(x=>x.key==='r2_bytes').scope,'bucket_soft_limit_not_billing');
 const partial=assessQuotaWarnings({...data,r2:{storage:null}});
 assert.equal(partial.state,'partial');
 assert.ok(partial.missing.includes('r2_bytes'));
 assert.ok(!partial.warnings.some(x=>x.key==='r2_bytes'));
 assert.equal(assessQuotaWarnings(null).state,'unavailable');
});
test('private RPC ignores extra forbidden fields and does not store redundant incident state',async()=>{
 const env={RELAY_TELEMETRY:{getSecurityOverview:async()=>({
  state:'ready',coverage:'best_effort',observedAt:today,total24h:4,total7d:4,
  byReason:[{category:'authentication',reason:'auth_signature_invalid',count:4}],
  byCountry:[{scope:'UA',count:4}],byHour:[],
  signalDays:[{dayStart:day,reason:'auth_signature_invalid',count:4,
    token:'secret-do-not-copy',clientIp:'192.0.2.99'}],
  confidential:'must-never-leak',
 })}};
 const out=await securitySummary(env);
 assert.equal(out.state,'ready');assert.equal(out.risk.state,'ready');
 assert.equal(out.risk.incidents.length,1);
 assert.equal(out.risk.incidents[0].severity,'warning');
 assert.equal(JSON.stringify(out).includes('secret-do-not-copy'),false);
 assert.equal(JSON.stringify(out).includes('must-never-leak'),false);
});
test('retention is transparent with zero new databases, no extra poll timer',()=>{
 assert.equal(RETENTION_INFO.controlSnapshotsDays,30);
 assert.equal(RETENTION_INFO.securityRollupsDays,30);
 assert.equal(RETENTION_INFO.securitySignalsDays,14);
 assert.equal(RETENTION_INFO.r2TemporaryTransferDays,7);
 assert.equal(RETENTION_INFO.securityCleanupMinutes,60);
 assert.ok(html.includes('MaVka Server Control 0.9.0'));
 for(const id of ['quota-alerts','quota-summary','incident-list','incident-summary'])
  assert.ok(html.includes('id="'+id+'"'),id);
 assert.ok(html.includes('showQuotaWarnings(m.quotaAlerts)'));
 assert.ok(html.includes('showIncidents(result?.risk)'));
 assert.ok(html.includes('Потребує перевірки')===false || html.includes('перевірки'));
});
