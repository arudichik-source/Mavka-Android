// Script injected in same CSP nonce-protected inline UI; no external imports.
export const stabilityClient=String.raw`// Read-only stability review. Observed sampled counters are NOT uptime nor daily totals.
const STABILITY_PERIODS=Object.freeze({'24h':86400000,'7d':7*86400000,'30d':30*86400000});
const STABILITY_METRICS=Object.freeze([
 {key:'workers_requests',label:'Workers · запити Relay'},
 {key:'workers_errors',label:'Workers · помилки виконання'},
 {key:'d1_rows_read',label:'D1 · прочитані рядки'},
 {key:'d1_rows_written',label:'D1 · записані рядки'}
]);
const STABILITY_SOURCES=Object.freeze({
 'Workers':['workers'],D1:['d1','history','trends','control-retention'],
 R2:['r2-storage','r2-ops','r2-audit'],
 Relay:['security','devices','cleanup']
});
function stabilityDailyObservations(trends,period='24h',now=Date.now()){
 if(trends?.state!=='ready'||trends.period!==period||!Array.isArray(trends.entries))
  return {status:'unavailable',days:[],samples:0,spanHours:0,coverageDays:0};
 const cutoff=now-(STABILITY_PERIODS[period]||STABILITY_PERIODS['24h']);
 const observations=trends.entries.filter(r=>
  Number.isSafeInteger(r?.bucket_start)&&r.bucket_start*1000>=cutoff&&
  r.bucket_start*1000<=now+300000)
  .sort((a,b)=>a.bucket_start-b.bucket_start).slice(0,240);
 const byDay=new Map();
 for(const r of observations){
  const stamp=r.bucket_start*1000,day=new Date(stamp).toISOString().slice(0,10);
  if(!byDay.has(day))byDay.set(day,{day,samples:0,lastAt:0,peaks:{},r2Bytes:null});
  const dest=byDay.get(day);dest.samples++;
  for(const m of STABILITY_METRICS){
   const val=r[m.key];
   if(typeof val==='number'&&Number.isFinite(val)&&val>=0)
    dest.peaks[m.key]=Math.max(dest.peaks[m.key]??0,val);
  }
  if(typeof r.r2_payload_bytes==='number'&&Number.isFinite(r.r2_payload_bytes)&&r.r2_payload_bytes>=0&&stamp>=dest.lastAt)
   dest.r2Bytes=r.r2_payload_bytes;
  dest.lastAt=Math.max(dest.lastAt,stamp);
 }
 const days=[...byDay.values()].sort((a,b)=>a.day.localeCompare(b.day));
 const spanHours=observations.length>1?
  (observations.at(-1).bucket_start-observations[0].bucket_start)/3600:0;
 return {status:observations.length?'sampled':'empty',days,samples:observations.length,
  spanHours,coverageDays:days.length};
}
function stabilityReview(trends,sources,events,period='24h',now=Date.now()){
 const sampling=stabilityDailyObservations(trends,period,now);
 const sourceRows=Array.isArray(sources)?sources:[];
 const groups=Object.entries(STABILITY_SOURCES).map(([name,ids])=>{
  const found=sourceRows.filter(s=>ids.includes(s.name));
  const ok=found.filter(s=>s.level==='ok').length;
  const warn=found.filter(s=>s.level==='warn').length;
  return {name,ok,warn,missing:ids.length-ok-warn,total:ids.length,
   status:warn?'warn':ok===ids.length?'ok':'unknown'};
 });
 const days=sampling.days,latest=days.at(-1),previous=days.at(-2);
 const adjacent=Boolean(latest&&previous&&
  Date.parse(latest.day+'T00:00:00Z')-Date.parse(previous.day+'T00:00:00Z')===86400000);
 const counters=STABILITY_METRICS.map(m=>{
  const current=latest?.peaks[m.key],prior=adjacent?previous.peaks[m.key]:undefined;
  const validCurrent=typeof current==='number'&&Number.isFinite(current);
  const validPrevious=typeof prior==='number'&&Number.isFinite(prior);
  return {key:m.key,label:m.label,current:validCurrent?current:null,
   previous:validPrevious?prior:null,
   change:validCurrent&&validPrevious?current-prior:null,
   percent:validCurrent&&validPrevious&&prior>0?(current-prior)/prior*100:null};
 });
 const eventWindow=period==='24h'?86400000:period==='7d'?7*86400000:14*86400000;
 const allowed=boundedEvents(events,now).filter(e=>e.at>=now-eventWindow);
 const counts={critical:0,warning:0,info:0,issues:0,restored:0,signals:0};
 const repeated=new Map();
 for(const e of allowed){
  counts[e.severity]++;
  if(e.phase==='issue'){
   counts.issues++;
   repeated.set(e.code,(repeated.get(e.code)||0)+1);
  }else if(e.phase==='restored')counts.restored++;
  else counts.signals++;
 }
 const recurring=[...repeated.entries()].filter(([_,count])=>count>=2)
  .sort((a,b)=>b[1]-a[1]||a[0].localeCompare(b[0])).slice(0,5)
  .map(([code,count])=>({code,count,label:EVENT_CATALOG[code]?.[0]||'Невідомий сигнал'}));
 return {period,groups,sampling,counters,counts,recurring,
  eventCoverageDays:period==='30d'?14:period==='7d'?7:1,
  comparison:adjacent,comparedDates:adjacent?[previous.day,latest.day]:[],
  sourceSnapshotOnly:true};
}
function stabilityReportText(report,now=Date.now()){
 const lines=[
  'MaVka Server Control — діагностичне зведення',
  'Створено UTC: '+new Date(now).toISOString(),
  'Період вибірки: '+report.period,
  'Обмеження: періодичні знімки під час відкриття консолі, не uptime і не точні добові підсумки.',
  'Джерела: останні отримані стани, не доказ безперервної роботи.',
  ...report.groups.map(g=>g.name+': підтверджено '+g.ok+'/'+g.total+
    '; уваги '+g.warn+'; невідомо '+g.missing),
  'Спостережень: '+report.sampling.samples+'; днів із даними: '+
   report.sampling.coverageDays+'; охоплення між крайніми знімками (год): '+
   report.sampling.spanHours.toFixed(1),
  'Порівняння пікових СПОСТЕРЕЖЕНИХ значень UTC (не добові totals): '+
   (report.comparison?report.comparedDates.join(' ↔ '):'недостатньо сусідніх днів'),
  ...report.counters.map(c=>c.label+': останній '+(c.current??'немає')+
   '; попередній '+(c.previous??'немає')+
   '; різниця '+(c.change??'недоступна')),
  'Локальний журнал (макс. 14 днів): проблеми '+report.counts.issues+
   '; відновлення '+report.counts.restored+'; добові сигнали '+report.counts.signals,
  'Повторювані переходи у стан проблеми: '+
   (report.recurring.length?report.recurring.map(e=>e.label+' ×'+e.count).join('; '):'не виявлено'),
  'Відсутність подій не означає відсутність збоїв, коли панель була закрита.',
  'Перевірка окремих викликів Cron і запитів — у Cloudflare Events; конфіденційні дані не включено.'
 ];
 return lines.join('\n')+'\n';
}
let currentStabilityReview=null;
function renderStabilityReview(){
 const responses=sourceHealthResults;
 const rows=responses?telemetrySourceRows(responses):[];
 const trend=responses?.[2]?.status==='fulfilled'?responses[2].value:null;
 const report=stabilityReview(trend,rows,technicalJournal.events,activePeriod);
 currentStabilityReview=report;
 q('stability-period').value=activePeriod;
 const sources=q('stability-sources');sources.replaceChildren();
 for(const g of report.groups){
  const div=document.createElement('article');div.className='stability-mini';
  const title=document.createElement('strong');title.textContent=g.name;
  const pill=document.createElement('span');pill.className='source-health-status '+
   (g.status==='ok'?'ok':g.status==='warn'?'warn':'missing');
  pill.textContent=g.status==='ok'?'Дані отримано':g.status==='warn'?'Перевірити':'Не підтверджено';
  const detail=document.createElement('p');
  detail.textContent='Дані: '+g.ok+'/'+g.total+' · уваги: '+g.warn+
   ' · невідомо: '+g.missing;
  div.append(title,pill,detail);sources.appendChild(div);
 }
 q('stability-coverage').textContent=report.sampling.status==='unavailable'
  ?'Історія за вибраний період недоступна. Порівняння не виконується.'
  :'Реальних знімків: '+report.sampling.samples+' · днів зі знімками: '+
   report.sampling.coverageDays+' · між крайніми знімками: '+
   report.sampling.spanHours.toFixed(1)+' год. Пропущені години не заповнюються.';
 const tbody=q('stability-counters');tbody.replaceChildren();
 for(const c of report.counters){
  const tr=document.createElement('tr');
  const values=[c.label,c.current===null?'—':fmt(c.current),
   c.previous===null?'—':fmt(c.previous),
   c.change===null?'Недостатньо даних':(c.change>0?'+':'')+fmt(c.change)+
    (c.percent===null?'':' ('+c.percent.toFixed(1)+'%)')];
  for(const value of values){
   const td=document.createElement('td');td.textContent=value;tr.appendChild(td);
  }
  tbody.appendChild(tr);
 }
 q('stability-comparison-note').textContent=report.comparison
  ?'Порівняно '+report.comparedDates.join(' та ')+
   ' UTC. Це максимуми серед збережених добових лічильників, не точні добові витрати. Неповні дні можуть спотворювати різницю.'
  :'Порівняння недоступне: потрібні знімки двох сусідніх календарних днів UTC. Нулі не домислюються.';
 q('stability-events').textContent='Локально зафіксовано: '+report.counts.issues+
  ' проблемних станів · '+report.counts.restored+' відновлень · '+
  report.counts.signals+' добових сигналів. Для 30-денного звіту локальна історія обмежена 14 днями.';
 const repeat=q('stability-recurring');repeat.replaceChildren();
 if(!report.recurring.length){
  repeat.textContent='Повторюваних переходів у проблемний стан у доступному локальному журналі не знайдено.';
 }else for(const item of report.recurring){
  const div=document.createElement('div');div.className='stability-repeat';
  div.textContent=item.label+' · '+item.count+' повторних спостережень';
  repeat.appendChild(div);
 }
 q('stability-status').textContent=report.sampling.status==='sampled'?
  'Вибірка доступна':'Неповна історія';
}
q('stability-period').addEventListener('change',e=>{
 if(e.target.value!==activePeriod)setPeriod(e.target.value);
});
q('stability-export').addEventListener('click',()=>{
 if(!currentStabilityReview)return;
 const body=stabilityReportText(currentStabilityReview);
 const blob=new Blob([body],{type:'text/plain;charset=utf-8'});
 const url=URL.createObjectURL(blob);
 const a=document.createElement('a');
 a.href=url;a.download='mavka-control-stability-'+activePeriod+'.txt';
 a.click();URL.revokeObjectURL(url);
 q('stability-export-status').textContent='Сформовано локальний TXT-звіт без персональних даних.';
});
`;
