// Server Control 0.5: resource-safe signal analysis with NO extra
// GraphQL requests, D1 writes, storage objects, timers, or new permissions.
// Derived exclusively from already-cached /api/summary & /api/security data.
// These are risk *signals*, not proof of an attack or a forensic log.
const SIGNAL_POLICY = Object.freeze({
  auth_signature_invalid: {label:'Невірний цифровий підпис', warn:3, critical:10},
  request_replayed: {label:'Повторний запит', warn:1, critical:3},
  auth_main_binding_invalid: {label:'Неправильна прив’язка головного пристрою', warn:1, critical:3},
  country_not_allowed: {label:'Запит з недозволеної країни', warn:5, critical:20},
  rate_limited: {label:'Перевищення частоти запитів', warn:30, critical:100},
});
const isCount = n => Number.isSafeInteger(n) && n >= 0;
export function assessIncidentSignals(rows, observedAt = Math.floor(Date.now()/1000)) {
  if (!Array.isArray(rows)) return {state:'unavailable', incidents:[],periodDays:14};
  const cutoff=observedAt-14*86400;
  const lastDay=Math.floor(observedAt/86400)*86400;
  const incidents=[];
  for(const row of rows.slice(0,90)){
    if(!row || !Object.hasOwn(SIGNAL_POLICY,row.reason) ||
      !isCount(row.dayStart) || !isCount(row.count) ||
      row.dayStart%86400!==0 || row.dayStart<cutoff-86400 ||
      row.dayStart>lastDay || row.count===0)continue;
    const policy=SIGNAL_POLICY[row.reason];
    const severity=row.count>=policy.critical?'critical':
      row.count>=policy.warn?'warning':'info';
    incidents.push({
      dayStart:row.dayStart, code:row.reason, label:policy.label,
      count:row.count, severity, status:'requires_review',
    });
  }
  incidents.sort((a,b)=>b.dayStart-a.dayStart ||
    ({critical:0,warning:1,info:2}[a.severity]-{critical:0,warning:1,info:2}[b.severity]) ||
    a.code.localeCompare(b.code));
  return {state:'ready',periodDays:14,incidents:incidents.slice(0,70),
    caveat:'Approximate daily group counts; may undercount. Events are not identified persons or proven intrusions.'};
}
const QUOTAS=Object.freeze([
 {key:'workers',label:'Workers · тільки Relay',path:['workers','requestsToday'],limit:'workersRequestsPerDay',scope:'one_worker_utc_day'},
 {key:'d1_read',label:'D1 · читання акаунта',path:['d1','rowsReadToday'],limit:'d1RowsReadPerDay',scope:'account_utc_day'},
 {key:'d1_write',label:'D1 · записи акаунта',path:['d1','rowsWrittenToday'],limit:'d1RowsWrittenPerDay',scope:'account_utc_day'},
 {key:'r2_bytes',label:'R2 · власний контрольний поріг 5 ГБ',path:['r2','storage','bytes'],limit:'r2MonitoringBytes',scope:'bucket_soft_limit_not_billing'},
]);
export function assessQuotaWarnings(data){
  if(!data || !data.limits || !['ready','partial'].includes(data.state))
    return {state:'unavailable',warnings:[],missing:QUOTAS.map(x=>x.key)};
  const warnings=[],missing=[];
  for(const source of QUOTAS){
    const n=source.path.reduce((v,key)=>v?.[key],data);
    const limit=data.limits[source.limit];
    if(!Number.isFinite(n) || n<0 || !Number.isFinite(limit)||limit<=0){
      missing.push(source.key);continue;
    }
    const ratio=n/limit;
    if(ratio>=0.70){
      const level=ratio>=0.95?'critical':ratio>=0.85?'warning':'info';
      warnings.push({
        key:source.key,label:source.label,used:n,threshold:limit,
        percent:Math.round(ratio*1000)/10,severity:level,scope:source.scope,
        status:'observed',
      });
    }
  }
  warnings.sort((a,b)=>b.percent-a.percent||a.key.localeCompare(b.key));
  return {state:missing.length?'partial':'ready',warnings,missing,
    caveat:'Quota alerts are informational. R2 current bytes are not GB-months; costs require Billing verification.'};
}
export const RETENTION_INFO = Object.freeze({
  controlSnapshotsDays:30,securityRollupsDays:30,securitySignalsDays:14,
  r2TemporaryTransferDays:7,relayCleanupMinutes:5,
  securityCleanupMinutes:60,
  principle:'No per-event alert rows or extra storage objects. Do not automatically delete undelivered attachments or revoked device records.',
});
