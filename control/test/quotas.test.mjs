import test from 'node:test';
import assert from 'node:assert/strict';
import { classifyR2Operations, readD1Storage } from '../src/analytics.js';
import { readBuildStatus, quotaReport } from '../src/quotas.js';
import { html } from '../src/ui.js';

test('R2 Class A, B and free operations use explicit pricing categories',()=>{
 const rows=[
   {dimensions:{actionType:'PutObject'},sum:{requests:13}},
   {dimensions:{actionType:'GetObject'},sum:{requests:25}},
   {dimensions:{actionType:'DeleteObject'},sum:{requests:2}}
 ];
 assert.deepEqual(classifyR2Operations(rows),{
   classA:13,classB:25,freeOperations:2,unknownOperations:0,observedRequests:40,
   classificationComplete:true,scope:'account',period:'current_utc_month'
 });
 const unrecognized=classifyR2Operations([...rows,
   {dimensions:{actionType:'UNKNOWN_ACTION'},sum:{requests:2}}]);
 assert.equal(unrecognized.classA,null);
 assert.equal(unrecognized.classB,null);
 assert.equal(unrecognized.unknownOperations,2);
 assert.equal(classifyR2Operations(null),null);
 assert.equal(classifyR2Operations([{dimensions:{actionType:'GetObject'},sum:{requests:null}}]),null);
});

test('D1 storage aggregates only latest valid per-database snapshots without identifiers',async()=>{
 const old=globalThis.fetch;
 globalThis.fetch=async (url,opts)=>{
   assert.equal(url,'https://api.cloudflare.com/client/v4/graphql');
   const q=JSON.parse(opts.body).query;
   assert.match(q,/d1StorageAdaptiveGroups/);
   return Response.json({data:{viewer:{accounts:[{
      d1StorageAdaptiveGroups:[
       {dimensions:{databaseId:'db-A',datetime:'2026-11-12T09:40:00Z'},max:{databaseSizeBytes:400}},
       {dimensions:{databaseId:'db-A',datetime:'2026-11-12T08:40:00Z'},max:{databaseSizeBytes:200}},
       {dimensions:{databaseId:'db-B',datetime:'2026-11-12T09:38:00Z'},max:{databaseSizeBytes:900}}
      ]}]}}});
 };
 try{
   const data=await readD1Storage({CF_ACCOUNT_ID:'a'.repeat(32),
      CF_ANALYTICS_TOKEN:'test-only-valid-length-token'},new Date('2026-11-12T10:00:00Z'));
   assert.equal(data.state,'ready');
   assert.equal(data.bytes,1300);
   assert.equal(data.databaseCount,2);
   assert.equal(data.largestDatabaseBytes,900);
   assert.doesNotMatch(JSON.stringify(data),/db-A|db-B/);
 }finally{globalThis.fetch=old;}
});

test('Workers Builds status never invents remaining minutes; scoped token and redirect safety',async()=>{
 let calls=0;
 const e={CF_ACCOUNT_ID:'a'.repeat(32),CF_BUILDS_READ_TOKEN:'valid-test-builds-token-123456'};
 const old=globalThis.fetch;
 globalThis.fetch=async (url,opts)=>{
   calls++;
   assert.equal(url,'https://api.cloudflare.com/client/v4/accounts/'+e.CF_ACCOUNT_ID+'/builds/account/limits');
   assert.equal(opts.headers.Authorization,'Bearer '+e.CF_BUILDS_READ_TOKEN);
   assert.equal(opts.redirect,'manual');
   return Response.json({success:true,result:{
      has_reached_build_minutes_limit:false,build_minutes_refresh_on:'2026-12-01T00:00:00Z'}});
 };
 try {
   assert.equal((await readBuildStatus({})).state,'not_configured');
   assert.equal(calls,0);
   const b=await readBuildStatus(e);
   assert.deepEqual(b,{state:'ready',exhausted:false,refreshAt:'2026-12-01T00:00:00.000Z'});
   assert.equal(b.minutesUsed,undefined);
   globalThis.fetch=async()=>new Response(null,{status:403});
   assert.equal((await readBuildStatus(e)).state,'permission');
 }finally{globalThis.fetch=old;}
});

test('quota endpoint never fabricates zero for unavailable Billing, D1 storage or R2 classes',async()=>{
 const mock={state:'ready',workers:{requestsToday:12,accountRequestsToday:86000},
   d1:{rowsReadToday:4200000,rowsWrittenToday:340},
   r2:{operations:{requestsThisMonth:12,account:null}}};
 const report=await quotaReport({},mock,new Date('2026-11-12T12:00:00Z'));
 assert.equal(report.rows.find(r=>r.id==='workers').level,'warning');
 assert.equal(report.rows.find(r=>r.id==='d1_storage').used,null);
 assert.equal(report.rows.find(r=>r.id==='r2_a').used,null);
 assert.equal(report.rows.find(r=>r.id==='r2_gb_month').used,null);
 assert.equal(report.rows.find(r=>r.id==='builds_minutes').used,null);
 assert.equal(report.availability.billing,false);
 assert.equal(report.builds.state,'not_configured');
 assert.ok(report.rows.find(r=>r.id==='workers').projected>=86000);
 assert.doesNotMatch(JSON.stringify(report),/CF_BUILDS_READ_TOKEN=.*secret/);
});

test('new quota UI is authenticated server data and starts without fabricated numbers',()=>{
 assert.match(html,/id="quota-center"/);
 assert.match(html,/id="quota-center-cards"/);
 assert.ok(html.includes("get('/api/limits')"));
 assert.match(html,/renderQuotaCenter\(results\[7\]/);
 assert.match(html,/Немає даних/);
 assert.match(html,/Часткові дані/);
 assert.match(html,/шкала не відображається/);
 assert.match(html,/Рівень використання невідомий/);
 assert.match(html,/fill\.style\.width=Number\.isFinite\(r\.percent\)\?/);
 assert.match(html,/Потрібна перевірка служб/);
 assert.doesNotMatch(html,/Класифікацію додамо після перевірки точних даних/);
});

test('R2 notes distinguish complete classification from unknown types; D1 storage stays observation-scoped',async()=>{
 const mk=complete=>({workers:{accountRequestsToday:20},d1:{rowsReadToday:2,rowsWrittenToday:3},
   r2:{operations:{account:{classA:complete?13:null,classB:complete?25:null,
     classificationComplete:complete,unknownOperations:complete?0:3,freeOperations:1}}}});
 const ready=await quotaReport({},mk(true),new Date('2026-11-12T12:00:00Z'));
 assert.match(ready.rows.find(r=>r.id==='r2_a').remark,/клас зберігання/i);
 assert.doesNotMatch(ready.rows.find(r=>r.id==='r2_a').remark,/некласифіковані/);
 assert.match(ready.rows.find(r=>r.id==='d1_storage').label,/24 год/);
 assert.match(ready.rows.find(r=>r.id==='d1_storage').remark,/Неактивні бази/);
 const unknown=await quotaReport({},mk(false),new Date('2026-11-12T12:00:00Z'));
 assert.equal(unknown.rows.find(r=>r.id==='r2_a').used,null);
 assert.match(unknown.rows.find(r=>r.id==='r2_a').remark,/некласифіковані/);
});

test('optional service exceptions cannot suppress the account-wide quota report',async()=>{
 const badEnv={get CF_ACCOUNT_ID(){throw new Error('simulated_optional_failure');}};
 const mock={state:'ready',workers:{accountRequestsToday:759},
   d1:{rowsReadToday:4148,rowsWrittenToday:23},
   r2:{operations:{account:{classA:592,classB:43,classificationComplete:true,
     unknownOperations:0,freeOperations:0}}}};
 const report=await quotaReport(badEnv,mock,new Date('2026-11-12T09:16:00Z'));
 assert.equal(report.rows.find(x=>x.id==='workers').used,759);
 assert.equal(report.rows.find(x=>x.id==='d1_read').used,4148);
 assert.equal(report.rows.find(x=>x.id==='r2_a').used,592);
 assert.equal(report.optionalSourcesPartial,true);
 assert.equal(report.builds.state,'unavailable');
 assert.equal(report.billing.state,'unavailable');
 assert.equal(report.rows.find(x=>x.id==='builds_minutes').used,null);
 assert.equal(report.rows.find(x=>x.id==='r2_gb_month').used,null);
 assert.doesNotMatch(JSON.stringify(report),/simulated_optional_failure/);
});

test('core quota report fallback avoids external API calls and labels optional metrics unavailable',async()=>{
 const old=globalThis.fetch;
 try {
   globalThis.fetch=()=>{throw Error('No optional HTTP requests expected');};
   const report=await quotaReport({},{
     workers:{accountRequestsToday:100},d1:{rowsReadToday:200,rowsWrittenToday:3},
   },new Date('2026-11-12T09:16:00Z'),{skipOptional:true});
   assert.equal(report.rows.find(x=>x.id==='workers').used,100);
   assert.equal(report.billing.state,'unavailable');
   assert.equal(report.builds.state,'unavailable');
   assert.equal(report.optionalSourcesPartial,true);
   assert.match(html,/Часткові дані · резервний режим/);
 }finally{globalThis.fetch=old;}
});
