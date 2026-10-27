// Tiny, privacy-safe health checkpoint for the separate mavka-control D1.
// Optional migration 0002 creates one singleton row, not an event history.
// Always run the expiry deletion FIRST; status persistence must never block it.
const count=n=>Number.isSafeInteger(n)&&n>=0;
const notConnected=reason=>({state:'not_connected',reason,data:null});
const THIRTY_SIX_HOURS=36*3600;
export async function readControlRetention(db,now=Date.now()){
 if(!db?.prepare)return notConnected('db_not_bound');
 try{
  const row=await db.prepare(`SELECT last_attempt_at, last_attempt_ok,
    last_success_at, last_deleted FROM control_retention_status WHERE id = 1`).first();
  if(!row)return notConnected('awaiting_first_run');
  const seconds=Math.floor(now/1000);
  const attempt=row.last_attempt_at,success=row.last_success_at;
  if(!count(seconds)||!Number.isSafeInteger(attempt)||attempt<=0||
     attempt>seconds+300||![0,1].includes(row.last_attempt_ok)||
     !(success===null||(Number.isSafeInteger(success)&&success>0&&
       success<=attempt&&success<=seconds+300))||
     !(row.last_deleted===null||count(row.last_deleted))||
     (row.last_attempt_ok===1&&success!==attempt))
   return notConnected('invalid_checkpoint');
  const ageSeconds=success===null?null:Math.max(0,seconds-success);
  const data={
   lastAttemptAt:attempt,lastAttemptOk:row.last_attempt_ok===1,
   lastSuccessAt:success,deletedLastSuccess:row.last_deleted,
   ageSeconds,scheduleUtc:'03:17',retentionDays:30,
  };
  // An unsuccessful latest cron takes precedence over an older success.
  const state=row.last_attempt_ok===0?'failed':
   ageSeconds===null?'failed':ageSeconds>THIRTY_SIX_HOURS?'stale':'ready';
  return {state,source:'control_d1_singleton',data};
 }catch{return notConnected('status_unavailable')}
}
export async function recordControlRetention(db,outcome,now=Date.now()){
 if(!db?.prepare)return {state:'not_bound'};
 const seconds=Math.floor(now/1000),success=outcome?.state==='ready';
 if(!Number.isSafeInteger(seconds)||seconds<=0||
    (success&&outcome.deleted!==null&&!count(outcome.deleted)))
  return {state:'unavailable'};
 try{
  const result=await db.prepare(`INSERT INTO control_retention_status
   (id,last_attempt_at,last_attempt_ok,last_success_at,last_deleted)
   VALUES (1,?,?,?,?)
   ON CONFLICT(id) DO UPDATE SET
    last_attempt_at=excluded.last_attempt_at,
    last_attempt_ok=excluded.last_attempt_ok,
    last_success_at=CASE WHEN excluded.last_attempt_ok=1
      THEN excluded.last_success_at ELSE control_retention_status.last_success_at END,
    last_deleted=CASE WHEN excluded.last_attempt_ok=1
      THEN excluded.last_deleted ELSE control_retention_status.last_deleted END`)
   .bind(seconds,success?1:0,success?seconds:null,success?outcome.deleted:null).run();
  if(result?.success===false)return {state:'unavailable'};
  return {state:'recorded'};
 }catch{return {state:'unavailable'}}
}
