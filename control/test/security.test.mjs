import test from 'node:test';
import assert from 'node:assert/strict';
import { securitySummary } from '../src/security.js';
import { html } from '../src/ui.js';
const base={
 state:'ready', source:'relay_aggregate_d1',coverage:'best_effort',
 observedAt:1792844400,total24h:6,total7d:15,retentionDays:30,
 byReason:[{category:'authentication',reason:'auth_signature_invalid',count:4},
   {category:'replay',reason:'request_replayed',count:2}],
 byCountry:[{scope:'UA',count:6}],
 byHour:[{hourStart:1792843200,count:6}],
};
test('security dashboard accepts only minimal anonymized aggregate fields',async()=>{
 const env={RELAY_TELEMETRY:{getSecurityOverview:async()=>({
   ...base,deviceId:'SECRET_DEVICE_ID',clientIp:'192.0.2.10',
   signedPayload:'SENSITIVE_CONTENT',byReason:base.byReason.map(x=>({
     ...x,authorization:'HIDDEN_TOKEN',
   }))})}};
 const result=await securitySummary(env);
 assert.equal(result.state,'ready');
 assert.equal(result.total24h,6);
 assert.equal(result.total7d,15);
 assert.equal(result.byReason.length,2);
 assert.equal(result.coverage,'best_effort');
 const text=JSON.stringify(result);
 for(const token of ['SECRET_DEVICE_ID','192.0.2.10','SENSITIVE_CONTENT','HIDDEN_TOKEN'])
   assert.equal(text.includes(token),false);
 assert.ok(!Object.hasOwn(result,'deviceId'));
 assert.ok(!Object.hasOwn(result,'signedPayload'));
});
test('incomplete or malicious Relay telemetry never reaches admin console',async()=>{
 const scenarios=[
   {...base,byReason:[{category:'hacked',reason:'attacker-provided-token',count:5}]},
   {...base,byCountry:[{scope:'PL',count:4}]},
   {...base,byHour:[{hourStart:-1,count:10}]},
   {...base,total24h:-3},
   {...base,total7d:1,total24h:2},
   {...base,coverage:'exact'},
   {...base,byReason:Array.from({length:13},()=>base.byReason[0])},
 ];
 for(const [i,sample] of scenarios.entries()){
   // No cache is created for invalid results.
   const result=await securitySummary({RELAY_TELEMETRY:{getSecurityOverview:async()=>sample}},Date.now()+(i+1)*600000);
   assert.equal(result.state,'not_connected');
   assert.equal(result.data,null);
 }
});
test('missing optional D1 table and network/RPC errors cannot fabricate zeros',async()=>{
 assert.equal((await securitySummary({})).state,'not_connected');
 const response=await securitySummary({RELAY_TELEMETRY:{getSecurityOverview:async()=>({
   state:'not_connected',reason:'migration_or_database_unavailable'
 })}},Date.now()+3600000);
 assert.equal(response.total24h,undefined);
 assert.equal(response.state,'not_connected');
 const failure=await securitySummary({RELAY_TELEMETRY:{getSecurityOverview:async()=>{
   throw Error('sensitive Cloudflare internal error')
 }}},Date.now()+3600000);
 assert.equal(failure.state,'not_connected');
 assert.equal(JSON.stringify(failure).includes('sensitive'),false);
});
test('interactive UI provides anonymous 24h/7d security summaries and avoids raw log access',()=>{
 for(const id of ['security-day','security-week','security-status','security-reasons',
  'security-countries','security-hours','security-warning'])
   assert.ok(html.includes('id="'+id+'"'),id);
 assert.ok(html.includes('MaVka Server Control 0.4.0'));
 assert.ok(html.includes("showSecurity(results[3]"));
 assert.ok(!html.includes('/v1/devices/'));
});
