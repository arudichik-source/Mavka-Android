// Minimal once-per-hour cleanup checkpoint from EXISTING deletion results.
// No extra table scans, no device IDs, keys, message contents or IPs.
// One D1 UPSERT/hour into the fixed id=1 row; missing migration is safe.
const okCount = value => Number.isSafeInteger(value) && value>=0 ? value : null;
const countFrom = result => okCount(result?.meta?.changes);
export const shouldWriteMaintenanceCheckpoint = scheduledTime =>
 Number.isFinite(scheduledTime)&&
 Math.floor(scheduledTime/60000)%60===0;
export async function recordMaintenanceCheckpoint(db,cleanupResults,security,scheduledMs) {
 if (!db?.prepare || !Array.isArray(cleanupResults) ||
     cleanupResults.length!==4 || !shouldWriteMaintenanceCheckpoint(scheduledMs))
   return false;
 if(cleanupResults.some(r=>r?.success===false)) return false;
 try {
   const deleted=cleanupResults.map(countFrom);
   const secOk=security?.success===true ? 1 : 0;
   const secDeleted=secOk ? okCount(security.deleted) : null;
   const at=Math.floor(scheduledMs/1000);
   await db.prepare(`INSERT INTO relay_maintenance_status
    (id,last_checked_at,deleted_messages,deleted_nonces,deleted_rate_windows,
     deleted_receipts,deleted_security_rollups,security_prune_ok)
    VALUES (1,?,?,?,?,?,?,?)
    ON CONFLICT(id) DO UPDATE SET
     last_checked_at=excluded.last_checked_at,
     deleted_messages=excluded.deleted_messages,
     deleted_nonces=excluded.deleted_nonces,
     deleted_rate_windows=excluded.deleted_rate_windows,
     deleted_receipts=excluded.deleted_receipts,
     deleted_security_rollups=excluded.deleted_security_rollups,
     security_prune_ok=excluded.security_prune_ok`)
     .bind(at,...deleted,secDeleted,secOk).run();
   return true;
 } catch { return false; }
}
const unavailable=reason=>({state:'not_connected',reason,data:null});
export async function readMaintenanceCheckpoint(db,now=Math.floor(Date.now()/1000)){
 if(!db?.prepare)return unavailable('db_not_bound');
 try{
   const row=await db.prepare(`SELECT last_checked_at,deleted_messages,
     deleted_nonces,deleted_rate_windows,deleted_receipts,
     deleted_security_rollups,security_prune_ok
     FROM relay_maintenance_status WHERE id=1`).first();
   if(!row)return unavailable('no_hourly_checkpoint_yet');
   const at=row.last_checked_at;
   if(!Number.isSafeInteger(at)||at<0||at>now+300)
      return unavailable('invalid_checkpoint');
   const age=now-at;
   const counters=['deleted_messages','deleted_nonces','deleted_rate_windows',
     'deleted_receipts','deleted_security_rollups'];
   const data={
     checkedAt:at,ageSeconds:age,
     sampledCronMinutes:60,relayCleanupCronMinutes:5,
     deletionCounts:Object.fromEntries(counters.map(k=>[
       k,okCount(row[k])])),
     securityPruneSucceeded:row.security_prune_ok===1,
   };
   return {state:age>5400?'stale':'ready',source:'relay_private_d1',
     data,note:'Numbers describe one hourly sampled cleanup, NOT the whole hour, not current pending expirations.'};
 }catch{return unavailable('table_or_database_unavailable');}
}
