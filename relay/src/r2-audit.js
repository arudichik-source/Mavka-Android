// Cost-bounded R2 temporary-object lifecycle monitoring.
// Every six hours, ONE list of at most 500 object METADATA records from
// `transfers/`. No object bodies, keys, filenames, IDs, etags or custom
// metadata are persisted, logged or exposed. Does not delete anything.
// A 7-day lifecycle may take another ~24h to expire objects; caution >8 days.
const PREFIX='transfers/';
const LIMIT=500;
const SIX_HOURS_IN_MINUTES=6*60;
const EIGHT_DAYS_SECONDS=8*86400;
const valid=n=>Number.isSafeInteger(n)&&n>=0;
// A five-minute Cron tick is represented by its UTC scheduled minute.
// Do NOT require the runtime's millisecond/second fields to be precisely
// zero: late or sub-minute scheduling metadata would silently skip R2.
// This still selects exactly one of the ordinary 5-minute ticks per 6h.
export const shouldAuditR2=ms=>
 Number.isSafeInteger(ms)&&ms>=0&&
 Math.floor(ms/60000)%SIX_HOURS_IN_MINUTES===0;

const failed=state=>({state,scanned:null,bytes:null,overdue:null,
 oldestAgeSeconds:null,truncated:null});
export async function scanR2Transfers(bucket,atSeconds){
 if(typeof bucket?.list!=='function')return failed('missing_binding');
 if(!valid(atSeconds))return failed('failed');
 try{
  // One Class A list only. No pagination by design; unknown counts remain
  // unknown if truncated, so users must not interpret sample as bucket total.
  const result=await bucket.list({prefix:PREFIX,limit:LIMIT});
  if(!Array.isArray(result?.objects)||result.objects.length>LIMIT ||
     typeof result.truncated!=='boolean')return failed('failed');
  let bytes=0,overdue=0,oldest=0;
  for(const item of result.objects){
   // R2 lists objects by metadata. Reject inconsistent provider responses.
   const size=item?.size;
   const uploaded=item?.uploaded instanceof Date?
     item.uploaded.getTime():typeof item?.uploaded==='string'?
     Date.parse(item.uploaded):NaN;
   if(!valid(size)||!Number.isFinite(uploaded)||
      uploaded<=0||uploaded>atSeconds*1000+300000) return failed('failed');
   const age=Math.max(0,Math.floor(atSeconds-uploaded/1000));
   bytes+=size;
   if(!Number.isSafeInteger(bytes))return failed('failed');
   oldest=Math.max(oldest,age);
   if(age>EIGHT_DAYS_SECONDS)overdue++;
  }
  return {state:'ready',scanned:result.objects.length,bytes,overdue,
   oldestAgeSeconds:oldest,truncated:result.truncated};
 }catch{return failed('failed')}
}
export async function recordR2Audit(db,audit,scheduledMs){
 if(!db?.prepare||!shouldAuditR2(scheduledMs))return false;
 const at=Math.floor(scheduledMs/1000);
 if(!['ready','failed','missing_binding'].includes(audit?.state))return false;
 const ok=audit.state==='ready';
 if(ok&&(!valid(audit.scanned)||audit.scanned>LIMIT||
   !valid(audit.bytes)||!valid(audit.overdue)||audit.overdue>audit.scanned||
   !valid(audit.oldestAgeSeconds)||typeof audit.truncated!=='boolean'))
   return false;
 try{
  // Singleton row is created by the SAME hourly cron's TTL checkpoint.
  // Update only fields added by migration 0005; never alter TTL data.
  const response=await db.prepare(`UPDATE relay_maintenance_status SET
   r2_audit_at=?,r2_audit_state=?,r2_sample_count=?,r2_sample_bytes=?,
   r2_overdue_count=?,r2_oldest_age_seconds=?,r2_sample_truncated=?
   WHERE id=1`).bind(at,audit.state,
   ok?audit.scanned:null,ok?audit.bytes:null,ok?audit.overdue:null,
   ok?audit.oldestAgeSeconds:null,ok?(audit.truncated?1:0):null).run();
  return response?.success!==false&&response?.meta?.changes===1;
 }catch{return false}
}
export function readR2AuditFromRow(row,now){
 const at=row?.r2_audit_at, state=row?.r2_audit_state;
 if(!valid(at)||!['ready','failed','missing_binding'].includes(state))
  return {state:'not_connected',reason:'no_r2_audit_yet'};
 if(at>now+300)return {state:'not_connected',reason:'invalid_timestamp'};
 const ageSeconds=Math.max(0,now-at);
 if(state!=='ready')return {state:'unavailable',reason:state==='missing_binding'?
   'r2_binding_missing':'r2_list_failed',auditedAt:at,ageSeconds};
 const data={
  auditedAt:at,ageSeconds,
  sampledObjects:row.r2_sample_count,sampledBytes:row.r2_sample_bytes,
  overdueObjects:row.r2_overdue_count,
  oldestAgeSeconds:row.r2_oldest_age_seconds,
  truncated:row.r2_sample_truncated===1,
  prefix:'transfers/',sampleLimit:LIMIT,lifecycleDays:7,warningAfterDays:8,
 };
 if(!valid(data.sampledObjects)||data.sampledObjects>LIMIT||
   !valid(data.sampledBytes)||!valid(data.overdueObjects)||
   data.overdueObjects>data.sampledObjects||
   !valid(data.oldestAgeSeconds)||
   ![0,1].includes(row.r2_sample_truncated))
   return {state:'not_connected',reason:'invalid_audit'};
 return {state:ageSeconds>8*3600?'stale':'ready',data,
  // "complete" only means the checked prefix is not truncated, NOT bucket-wide.
  scope:'temporary_prefix_sample',
  note:'No object content inspected. Counts and bytes cover only sampled transfers/ entries; a truncated scan is incomplete.'};
}
