import test from 'node:test';
import assert from 'node:assert/strict';
import {oracleTelemetry,sanitizeOracle} from '../src/oracle.js';
test('Oracle telemetry fails closed until a private VPC service and secret are configured',async()=>{
 assert.deepEqual(await oracleTelemetry({}),{state:'not_connected'});
 assert.deepEqual(await oracleTelemetry({ORACLE_MONITOR_KEY:'x'.repeat(48)}),{state:'not_connected'});
});
test('Private Oracle response strips unexpected secret and visitor data',async()=>{
 const x={state:'ready',checkedAt:'2026-11-11T12:00:00Z',cpuPercent:2,ramPercent:10,
 diskFreeGb:170,siteProbe:'online',siteRequests:{day:'2026-11-11',count:30,observed:30,complete:true,statuses:{'2xx':29,'3xx':0,'4xx':1,'5xx':0},visitorIP:'127.9.9.9'},apiRequests:{day:'2026-11-11',count:10,observed:10,complete:true,statuses:{'2xx':10,'3xx':0,'4xx':0,'5xx':0}},services:{'mavka-web.service':'active',other:'secret'},token:'sensitive'};
 const cleaned=sanitizeOracle(x);
 assert.equal(cleaned.siteRequests.count,30);
 assert.equal(cleaned.services['mavka-web.service'],'active');
 assert.doesNotMatch(JSON.stringify(cleaned),/127\.9\.9\.9|sensitive|other/);
});
test('private VPC is used without external network fetch',async()=>{
 let calls=0;
 const env={ORACLE_MONITOR_KEY:'q'.repeat(48),ORACLE_MONITOR_VPC:{fetch:async(url,opts)=>{
  calls++;assert.equal(url,'http://oracle-monitor.internal/v1/telemetry');
  assert.equal(opts.headers.Authorization,'Bearer '+'q'.repeat(48));
  return Response.json({state:'ready',siteProbe:'online',services:{}});
 }}};
 const out=await oracleTelemetry(env);
 assert.equal(calls,1);assert.equal(out.state,'ready');
});
