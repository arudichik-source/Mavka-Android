// Account-wide Free-plan quota overview. Read-only; no billing guesses,
// direct browser credentials, public API, new Cron or persisted rows.
import { LIMITS, readD1Storage } from './analytics.js';
import { readBillingUsage } from './billing.js';

const isCount=x=>typeof x==='number' && Number.isFinite(x) && x>=0;
const safeCount=x=>isCount(x)?x:null;
let cached=null,cacheAt=0,cacheKey='',pending=null;

export async function readBuildStatus(env) {
  const id=env?.CF_ACCOUNT_ID, token=env?.CF_BUILDS_READ_TOKEN;
  if(typeof id!=='string'||!/^[a-f0-9]{32}$/i.test(id)||
     typeof token!=='string'||token.length<20)
    return {state:'not_configured',exhausted:null,refreshAt:null};
  try {
    const r=await fetch('https://api.cloudflare.com/client/v4/accounts/'+id+'/builds/account/limits',{
      method:'GET',redirect:'manual',
      headers:{Authorization:'Bearer '+token,Accept:'application/json'},
      signal:AbortSignal.timeout(8000)
    });
    if(r.status===401||r.status===403)return {state:'permission',exhausted:null,refreshAt:null};
    if(r.status===429)return {state:'rate_limit',exhausted:null,refreshAt:null};
    if(!r.ok||Number(r.headers.get('content-length'))>12000)
      return {state:'unavailable',exhausted:null,refreshAt:null};
    const raw=await r.text();
    if(raw.length>12000)return {state:'unavailable',exhausted:null,refreshAt:null};
    const data=JSON.parse(raw);
    if(data?.success!==true||typeof data?.result?.has_reached_build_minutes_limit!=='boolean')
      return {state:'no_reliable_data',exhausted:null,refreshAt:null};
    const date=data.result.build_minutes_refresh_on;
    return {state:'ready',exhausted:data.result.has_reached_build_minutes_limit,
      refreshAt:typeof date==='string'&&Number.isFinite(Date.parse(date))?
        new Date(date).toISOString():null};
  }catch{return {state:'unavailable',exhausted:null,refreshAt:null};}
}

async function extras(env) {
  const now=Date.now(),key=String(env.CF_ACCOUNT_ID||'')+':'+Boolean(env.CF_BUILDS_READ_TOKEN);
  if(cached&&key===cacheKey&&now-cacheAt<(cached.stale?60000:900000))return cached.data;
  if(pending&&key===cacheKey)return pending;
  cacheKey=key;
  pending=Promise.all([readD1Storage(env),readBuildStatus(env)]).then(([d1,builds])=>{
    const data={d1,builds},stale=d1.state!=='ready'||(builds.state!=='ready'&&builds.state!=='not_configured');
    cached={data,stale};cacheAt=Date.now();return data;
  }).finally(()=>{pending=null;});
  return pending;
}

function periodInfo(kind, now) {
  if(kind==='day'){
    const start=Date.UTC(now.getUTCFullYear(),now.getUTCMonth(),now.getUTCDate());
    return {start,end:start+86400000,minAge:3*3600000,label:'UTC-доба'};
  }
  if(kind==='month'){
    const start=Date.UTC(now.getUTCFullYear(),now.getUTCMonth(),1);
    const end=Date.UTC(now.getUTCFullYear(),now.getUTCMonth()+1,1);
    return {start,end,minAge:3*86400000,label:'UTC-місяць'};
  }
  return null;
}

function quota(id,label,value,limit,scope,kind,now,remark=''){
  const used=safeCount(value),max=isCount(limit)&&limit>0?limit:null;
  const ratio=used!==null&&max!==null?used/max:null;
  const level=ratio===null?'unknown':ratio>=1?'exceeded':ratio>=.95?'critical':
    ratio>=.85?'warning':ratio>=.70?'notice':'ok';
  let forecast=null;
  const period=periodInfo(kind,now);
  if(ratio!==null&&period){
    const elapsed=now.getTime()-period.start,duration=period.end-period.start;
    if(elapsed>=period.minAge&&elapsed>0&&elapsed<=duration){
      forecast=Math.round(used*duration/elapsed);
    }
  }
  return {id,label,used,limit:max,remaining:used===null||max===null?null:Math.max(0,max-used),
    percent:ratio===null?null:Math.round(ratio*1000)/10,level,scope,period:kind,
    projected:forecast,forecastRisk:forecast!==null&&max!==null&&forecast>=max,remark};
}

export async function quotaReport(env,metrics,now=new Date()){
  const data=metrics && typeof metrics==='object'?metrics:{};
  const [extra,billing]=await Promise.all([extras(env),readBillingUsage(env)]);
  const {d1,builds}=extra;
  const workers=data.workers, db=data.d1, r2=data.r2;
  const accountR2=r2?.operations?.account;
  const rows=[
    quota('workers','Workers · запити всіх застосунків',
      workers?.accountRequestsToday,LIMITS.workersRequestsPerDay,'account','day',now),
    quota('d1_read','D1 · прочитані рядки',
      db?.rowsReadToday,LIMITS.d1RowsReadPerDay,'account','day',now),
    quota('d1_write','D1 · записані рядки',
      db?.rowsWrittenToday,LIMITS.d1RowsWrittenPerDay,'account','day',now),
    quota('d1_storage','D1 · сума спостережених БД (24 год)',
      d1.state==='ready'?d1.bytes:null,LIMITS.d1StorageBytesAccount,'observed_databases_24h','gauge',now,
      'За останніми доступними знімками БД за 24 години. Неактивні бази можуть бути відсутні: це не підтверджений загальний обсяг акаунта.'),
    quota('d1_largest','D1 · найбільша спостережена БД (24 год)',
      d1.state==='ready'?d1.largestDatabaseBytes:null,
      LIMITS.d1StorageBytesPerDatabase,'largest_observed_db_24h','gauge',now,
      'Серед баз, що потрапили до знімків останніх 24 годин; не обов’язково найбільша база акаунта.'),
    quota('r2_a','R2 · операції Class A (акаунт)',
      accountR2?.classA,LIMITS.r2ClassAOperationsPerMonth,'account','month',now,
      accountR2?.classificationComplete===false?
        'Є некласифіковані типи операцій; розбивка неповна. Клас зберігання Standard/Infrequent Access не перевірено.':
        'Клас зберігання Standard/Infrequent Access не перевірено; квота діє лише для Standard.'),
    quota('r2_b','R2 · операції Class B (акаунт)',
      accountR2?.classB,LIMITS.r2ClassBOperationsPerMonth,'account','month',now,
      accountR2?.classificationComplete===false?
        'Є некласифіковані типи операцій; розбивка неповна. Безкоштовна квота діє лише для Standard.':
        'Безкоштовна квота діє лише для Standard; клас зберігання акаунта не перевірено.'),
    quota('r2_gb_month','R2 Standard · зберігання, ГБ-місяці (платіжний період)',
      billing?.r2StandardGbMonths,LIMITS.r2StandardGbMonths,'account','billing_cycle',now,
      billing?.r2StandardGbMonths!==null&&billing?.r2StandardGbMonths!==undefined?
        'Фактичне споживання за доступними рядками Billable Usage API; дані оновлюються із затримкою.':
        'Немає однозначно визначених записів Standard GB-month у Billing API. Не підміняємо їх поточним обсягом R2.'),
    quota('builds_minutes','Workers Builds · хвилини збірки',
      null,LIMITS.workersBuildMinutesPerMonth,'account','month',now,
      'API повертає факт вичерпання та дату скидання, а не точну витрату хвилин.'),
  ];
  const alerts=rows.filter(x=>['notice','warning','critical','exceeded'].includes(x.level))
    .map(x=>({id:x.id,level:x.level,percent:x.percent,label:x.label}));
  if(builds.state==='ready'&&builds.exhausted===true)
    alerts.unshift({id:'builds_exhausted',level:'critical',percent:null,label:'Workers Builds · ліміт хвилин вичерпано'});
  return {
    version:2,source:'Cloudflare Analytics + optional Workers Builds and Billable Usage APIs',
    capturedAt:now.toISOString(),
    plan:'workers_free_reference_not_verified',
    rows,alerts,builds,billing,
    d1StorageState:d1.state,
    d1DatabaseCount:d1.state==='ready'?d1.databaseCount:null,
    r2Classification:accountR2?{
      complete:accountR2.classificationComplete,
      unclassifiedRequests:accountR2.unknownOperations,
      freeOperations:accountR2.freeOperations,
    }:null,
    availability:{
      workers:rows[0].used!==null,d1:rows[1].used!==null&&rows[2].used!==null,
      d1Storage:d1.state==='ready',
      r2Classes:rows[5].used!==null&&rows[6].used!==null,
      builds:builds.state==='ready',
      r2GbMonths:rows[7].used!==null,billing:billing.state==='ready',
    },
    staticLimits:[
      {label:'Workers · CPU/виклик',value:'10 мс',note:'Технічний ліміт, не лічильник використання'},
      {label:'Workers · Cron triggers',value:String(LIMITS.workersCronTriggersPerAccount),note:'Кількість налаштованих не зчитується'},
      {label:'Workers · застосунків',value:String(LIMITS.workersScriptsPerAccount),note:'Кількість налаштованих не зчитується'},
      {label:'D1 · кількість баз',value:String(LIMITS.d1DatabasesPerAccount),note:'Free; виміряні за добу бази не дорівнюють усім створеним'},
      {label:'Workers KV',value:'За тарифом KV',note:'Використання не підключено'},
      {label:'Queues / Durable Objects / VPC',value:'За тарифами сервісів',note:'Використання та рахунки не зчитуються'},
    ],
    caveats:[
      'Тарифи довідкові для Workers Free. Реальний план і рахунки не перевірено.',
      'Місячні операції зібрані за календарний місяць UTC, а не обов’язково за платіжний цикл.',
      'Дані Analytics можуть надходити із затримкою; прогноз лінійний і не гарантує витрати.',
      'R2 Free застосовується тільки до Standard; поточний GraphQL-звіт не розрізняє класи зберігання. Billing перевіряйте окремо.',
      'Розміри D1 — лише за базами з доступними знімками за останні 24 години; повний перелік БД не підтверджено.',
      'Billable Usage API показує нарахування за споживання із затримкою, не повний рахунок (без фіксованих підписок).',
      'R2 GB-month беремо лише з однозначно визначених записів Standard поточного платіжного циклу, ніколи не з поточного зайнятого обсягу.',
    ],
  };
}
