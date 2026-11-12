import test from 'node:test';
import assert from 'node:assert/strict';
import {summarizeBillingRecords,readBillingUsage} from '../src/billing.js';
import {quotaReport} from '../src/quotas.js';
import {html} from '../src/ui.js';

const base={BillingPeriodStart:'2026-11-01T00:00:00Z',BillingCurrency:'USD'};
const worker={...base,ServiceFamilyName:'Workers',ServiceName:'Workers Standard',BilledCost:0.25};
const r2={...base,ServiceFamilyName:'R2',ServiceName:'R2 Standard Storage',
  ConsumedUnit:'GB-months',ConsumedQuantity:2.5,BilledCost:0.125};

test('billing summary only publishes bounded costs and unambiguous Standard storage',()=>{
 const result=summarizeBillingRecords([worker,r2]);
 assert.deepEqual(result,{state:'ready',currency:'USD',totalCost:0.375,
   periodStart:base.BillingPeriodStart,r2StandardGbMonths:2.5,records:2});
 assert.equal(result.BillingAccountId,undefined);
 assert.equal(summarizeBillingRecords([{...r2,ServiceName:'R2 Storage'}]).r2StandardGbMonths,null);
 assert.equal(summarizeBillingRecords([{...r2,ServiceName:'R2 Infrequent Access Storage'}]).r2StandardGbMonths,null);
 assert.equal(summarizeBillingRecords([worker,{...r2,ServiceName:'R2 Storage'}]).r2StandardGbMonths,null);
 assert.equal(summarizeBillingRecords([{...worker,BilledCost:0}]).totalCost,0);
 assert.equal(summarizeBillingRecords([]).state,'no_reliable_data');
 assert.equal(summarizeBillingRecords([{...worker,BilledCost:null}]).state,'no_reliable_data');
 assert.equal(summarizeBillingRecords([worker,{...r2,BillingCurrency:'EUR'}]).state,'no_reliable_data');
 assert.equal(summarizeBillingRecords([worker,{...r2,BillingPeriodStart:'2026-10-01T00:00:00Z'}]).state,'no_reliable_data');
});

test('billing requests are optional, private, read-only, bounded and permission-safe',async()=>{
 const e={CF_ACCOUNT_ID:'c'.repeat(32),CF_BILLING_READ_TOKEN:'dummy-read-token-with-adequate-length'};
 const old=globalThis.fetch;
 let calls=0;
 try{
   globalThis.fetch=async(url,opts)=>{
     calls++;
     assert.equal(url,'https://api.cloudflare.com/client/v4/accounts/'+e.CF_ACCOUNT_ID+'/billable-usage');
     assert.equal(opts.method,'GET');
     assert.equal(opts.redirect,'manual');
     assert.equal(opts.headers.Authorization,'Bearer '+e.CF_BILLING_READ_TOKEN);
     return Response.json({success:true,result:[worker,r2]});
   };
   assert.equal((await readBillingUsage({})).state,'not_configured');
   assert.equal(calls,0);
   const result=await readBillingUsage(e);
   assert.equal(result.totalCost,0.375);
   assert.equal(result.r2StandardGbMonths,2.5);
   assert.equal(calls,1);
   assert.equal((await readBillingUsage(e)).state,'ready');
   assert.equal(calls,1); // per-isolate six-hour cache
   assert.doesNotMatch(JSON.stringify(result),/dummy-read-token|BillingAccountId|SubscriptionId/);
   globalThis.fetch=async()=>new Response(null,{status:403});
   assert.equal((await readBillingUsage({...e,CF_ACCOUNT_ID:'d'.repeat(32)})).state,'permission');
   globalThis.fetch=async()=>Response.json({success:true,result:[]});
   assert.equal((await readBillingUsage({...e,CF_ACCOUNT_ID:'e'.repeat(32)})).state,'no_reliable_data');
 }finally{globalThis.fetch=old;}
});

test('quota report uses trustworthy R2 GB-month billing rows but never invents Build minutes',async()=>{
 const old=globalThis.fetch;
 try{
   globalThis.fetch=async(url,opts)=>{
     if(url.endsWith('/billable-usage'))return Response.json({success:true,result:[worker,r2]});
     if(url.endsWith('/graphql'))return Response.json({data:{viewer:{accounts:[{
       d1StorageAdaptiveGroups:[{dimensions:{databaseId:'id',datetime:'2026-11-12T10:00:00Z'},
         max:{databaseSizeBytes:1000}}]}]}}});
     throw new Error('unexpected endpoint');
   };
   const env={CF_ACCOUNT_ID:'f'.repeat(32),
     CF_ANALYTICS_TOKEN:'dummy-analytics-token-with-adequate-length',
     CF_BILLING_READ_TOKEN:'dummy-billing-token-with-adequate-length'};
   const report=await quotaReport(env,{},new Date('2026-11-12T12:00:00Z'));
   assert.equal(report.rows.find(r=>r.id==='r2_gb_month').used,2.5);
   assert.equal(report.rows.find(r=>r.id==='builds_minutes').used,null);
   assert.equal(report.billing.totalCost,0.375);
   assert.equal(report.availability.billing,true);
   assert.equal(report.availability.r2GbMonths,true);
 }finally{globalThis.fetch=old;}
});

test('UI labels billing as optional metered charges only',()=>{
 assert.match(html,/id="quota-center-billing"/);
 assert.match(html,/CF_BILLING_READ_TOKEN/);
 assert.match(html,/Нараховано за споживання/);
 assert.match(html,/без фіксованих підписок/);
});
