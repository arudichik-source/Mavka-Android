// Optional read-only Billable Usage API integration. Never expose raw billing rows,
// account identifiers, subscription IDs, payment details or authentication material.
const empty=state=>({state,currency:null,totalCost:null,periodStart:null,
  r2StandardGbMonths:null,records:null});
const money=x=>typeof x==='number'&&Number.isFinite(x)&&Math.abs(x)<=1e12;
const quantity=x=>typeof x==='number'&&Number.isFinite(x)&&x>=0&&x<=1e12;
const round=x=>Math.round(x*1e6)/1e6;

export function summarizeBillingRecords(rows){
  if(!Array.isArray(rows)||rows.length<1||rows.length>2000)return empty('no_reliable_data');
  let currency=null,periodStart=null,total=0,standard=0,standardFound=false,r2StorageUnknown=false;
  for(const x of rows){
    if(!x||typeof x!=='object'||!money(x.BilledCost)||
       typeof x.BillingCurrency!=='string'||!/^[A-Z]{3}$/.test(x.BillingCurrency)||
       typeof x.BillingPeriodStart!=='string'||!/^\d{4}-\d\d-\d\dT/.test(x.BillingPeriodStart))
      return empty('no_reliable_data');
    if(currency!==null&&currency!==x.BillingCurrency)return empty('no_reliable_data');
    if(periodStart!==null&&periodStart!==x.BillingPeriodStart)return empty('no_reliable_data');
    currency=x.BillingCurrency;periodStart=x.BillingPeriodStart;
    total+=x.BilledCost;
    const family=x.ServiceFamilyName,name=x.ServiceName;
    if(family==='R2'&&typeof name==='string'&&/storage/i.test(name)){
      const units=x.ConsumedUnit,amount=x.ConsumedQuantity;
      // Billing descriptions vary; never infer Standard from a generic R2 label.
      if(/infrequent/i.test(name))continue;
      if(!/standard/i.test(name)||!/^(GB-months?|GB·months?)$/i.test(units||'')||
         !quantity(amount)){r2StorageUnknown=true;continue;}
      standardFound=true;standard+=amount;
    }
  }
  if(!money(total)||!quantity(standard))return empty('no_reliable_data');
  return {state:'ready',currency,totalCost:round(total),periodStart,
    r2StandardGbMonths:standardFound&&!r2StorageUnknown?round(standard):null,
    records:rows.length};
}

let cached=null,at=0,key='',pending=null;
export async function readBillingUsage(env){
  const id=env?.CF_ACCOUNT_ID,token=env?.CF_BILLING_READ_TOKEN;
  if(typeof id!=='string'||!/^[a-f0-9]{32}$/i.test(id)||
     typeof token!=='string'||token.length<20)return empty('not_configured');
  const currentKey=id+':configured';
  const now=Date.now();
  if(cached&&key===currentKey&&now-at<(cached.state==='ready'?21600000:900000))return cached;
  if(pending&&key===currentKey)return pending;
  key=currentKey;
  pending=(async()=>{
    try{
      const resp=await fetch('https://api.cloudflare.com/client/v4/accounts/'+id+'/billable-usage',{
        method:'GET',redirect:'manual',
        headers:{Authorization:'Bearer '+token,Accept:'application/json'},
        signal:AbortSignal.timeout(8000)});
      if(resp.status===401||resp.status===403)return empty('permission');
      if(resp.status===429)return empty('rate_limit');
      if(!resp.ok||Number(resp.headers.get('content-length'))>250000)
        return empty('unavailable');
      const raw=await resp.text();
      if(raw.length>250000)return empty('unavailable');
      const body=JSON.parse(raw);
      if(body?.success!==true)return empty('unavailable');
      return summarizeBillingRecords(body.result);
    }catch{return empty('unavailable');}
  })().then(data=>{cached=data;at=Date.now();return data;})
    .finally(()=>{pending=null;});
  return pending;
}
