// Inline browser-only script used by the nonce-protected Control dashboard.
export const technicalEventsClient = String.raw`// Browser-only technical event journal; static codes prevent storing raw telemetry.
const EVENT_KEY='mavka-control.technical-events.v1',EVENT_TTL=14*86400000,EVENT_CAP=120;
const EVENT_CATALOG=Object.freeze({
 'source.workers':['Workers · аналітика Relay','workers'],
 'source.d1':['D1 · використання','d1'],
 'source.r2-storage':['R2 · обсяг зберігання','r2'],
 'source.r2-ops':['R2 · операції','r2'],
 'source.history':['D1 · історія','d1'],
 'source.trends':['D1 · графік','d1'],
 'source.security':['Relay · зведення безпеки','relay'],
 'source.devices':['Relay · пристрої','relay'],
 'source.cleanup':['Relay · очищення','cron'],
 'source.control-retention':['D1 Control · Cron очищення','cron'],
 'source.r2-audit':['R2 · transfers/','r2'],
 'quota.workers':['Workers · контрольний поріг','workers'],
 'quota.d1_read':['D1 · поріг читання','d1'],
 'quota.d1_write':['D1 · поріг запису','d1'],
 'quota.r2_bytes':['R2 · власний поріг','r2'],
 'risk.auth_signature_invalid':['Relay · недійсні підписи','relay'],
 'risk.request_replayed':['Relay · повторні запити','relay'],
 'risk.auth_main_binding_invalid':['Relay · прив’язка пристрою','relay'],
 'risk.country_not_allowed':['Relay · країни поза дозволеною зоною','relay'],
 'risk.rate_limited':['Relay · обмеження запитів','relay']
});
function safeEvent(e,now=Date.now()){
 if(!e||typeof e!=='object'||!Object.hasOwn(EVENT_CATALOG,e.code)||
  !['critical','warning','info'].includes(e.severity)||
  !['issue','restored','signal'].includes(e.phase)||
  !Number.isSafeInteger(e.at)||e.at<now-EVENT_TTL||e.at>now+300000||
  (e.phase==='signal')!==e.code.startsWith('risk.')||
  (e.phase==='signal'&&e.at%86400000!==0))return null;
 return {code:e.code,at:e.at,severity:e.severity,phase:e.phase};
}
function boundedEvents(entries,now=Date.now()){
 const seen=new Set();
 return (Array.isArray(entries)?entries:[]).map(x=>safeEvent(x,now)).filter(x=>{
  if(!x)return false;
  const key=x.code+'|'+x.at+'|'+x.phase;
  if(seen.has(key))return false;seen.add(key);return true;
 }).sort((a,b)=>b.at-a.at||a.code.localeCompare(b.code)).slice(0,EVENT_CAP);
}
function technicalSignals(results,now=Date.now()){
 const answer=i=>results?.[i]?.status==='fulfilled'?results[i].value:null;
 const rows=telemetrySourceRows(results,now),maintenance=answer(5),out=[];
 for(const r of rows){
  const code='source.'+r.name;
  if(!Object.hasOwn(EVENT_CATALOG,code))continue;
  const serious=(r.name==='control-retention'&&maintenance?.controlRetention?.state==='failed')||
   (r.name==='cleanup'&&maintenance?.data?.securityPruneSucceeded===false);
  out.push({code,state:r.level==='ok'?'ok':serious?'critical':
   r.level==='missing'?'unknown':'warning'});
 }
 const quota=answer(0)?.quotaAlerts;
 if(quota&&['ready','partial'].includes(quota.state)){
  const warnings=new Map((Array.isArray(quota.warnings)?quota.warnings:[])
   .filter(w=>['info','warning','critical'].includes(w?.severity))
   .map(w=>[w.key,w.severity]));
  for(const key of ['workers','d1_read','d1_write','r2_bytes']){
   if(Array.isArray(quota.missing)&&quota.missing.includes(key))continue;
   out.push({code:'quota.'+key,state:warnings.get(key)||'ok'});
  }
 }
 return out;
}
function cleanEventStates(states){
 const result={};
 if(states&&typeof states==='object')
  for(const [code,state] of Object.entries(states)){
   if(Object.hasOwn(EVENT_CATALOG,code)&&!code.startsWith('risk.')&&
    ['ok','unknown','warning','critical','info'].includes(state))result[code]=state;
  }
 return result;
}
function updateEventJournal(journal,responses,now=Date.now()){
 const events=boundedEvents(journal?.events,now),states=cleanEventStates(journal?.states);
 for(const item of technicalSignals(responses,now)){
  const previous=states[item.code],state=item.state;
  if(previous!==state&&(previous!==undefined||state!=='ok'))
   events.push({code:item.code,at:now,phase:state==='ok'?'restored':'issue',
    severity:state==='ok'?'info':state==='critical'?'critical':
     state==='info'?'info':'warning'});
  states[item.code]=state;
 }
 const security=responses?.[3]?.status==='fulfilled'?responses[3].value:null;
 if(security?.risk?.state==='ready'&&Array.isArray(security.risk.incidents)){
  for(const item of security.risk.incidents.slice(0,70)){
   const code='risk.'+item?.code,at=item?.dayStart*1000;
   if(!Object.hasOwn(EVENT_CATALOG,code)||!Number.isSafeInteger(at)||
    at<now-EVENT_TTL||at>now+300000)continue;
   events.push({code,at,phase:'signal',severity:
    ['critical','warning','info'].includes(item.severity)?item.severity:'info'});
  }
 }
 return {events:boundedEvents(events,now),states};
}
function filterEventJournal(entries,{severity='all',period='7d',group='all'}={},now=Date.now()){
 const windows={'24h':86400000,'7d':7*86400000,'14d':EVENT_TTL};
 const cutoff=now-(windows[period]||windows['7d']);
 return boundedEvents(entries,now).filter(e=>e.at>=cutoff&&
  (severity==='all'||e.severity===severity)&&
  (group==='all'||EVENT_CATALOG[e.code][1]===group));
}
let technicalJournal={events:[],states:{}},technicalStoreAvailable=true;
try{
 const saved=JSON.parse(localStorage.getItem(EVENT_KEY)||'null');
 if(saved&&typeof saved==='object'){
  technicalJournal.events=boundedEvents(saved.events);
  technicalJournal.states=cleanEventStates(saved.states);
 }
}catch{technicalStoreAvailable=false}
function saveTechnicalJournal(){
 try{localStorage.setItem(EVENT_KEY,JSON.stringify(technicalJournal));
  technicalStoreAvailable=true}
 catch{technicalStoreAvailable=false}
}
function renderTechnicalEvents(){
 const root=q('tech-events-list');root.replaceChildren();
 const entries=filterEventJournal(technicalJournal.events,{
  severity:q('tech-filter-severity').value,
  period:q('tech-filter-period').value,
  group:q('tech-filter-group').value
 });
 const total=technicalJournal.events.length;
 q('tech-events-summary').textContent='Локально збережено: '+total+
  ' · за фільтрами: '+entries.length+
  ' · події фіксуються лише під час відкриття або оновлення панелі.';
 const badge=q('tech-events-status');
 badge.className='source-health-status '+(total?'warn':'ok');
 badge.textContent=total?'Історія: '+total:'Подій не зафіксовано';
 q('tech-events-storage').textContent=(technicalStoreAvailable?
  'Лише цей браузер. ':'Збереження браузера недоступне — історія тільки до закриття вкладки. ')+
  'Максимум 120 подій за 14 днів. Автоочищення під час відкриття/оновлення; це не Cloudflare Events. Нових серверних записів немає.';
 if(!entries.length){
  const empty=document.createElement('p');empty.className='event-empty';
  empty.textContent=total?'За вибраними фільтрами записів немає.':
   'Локальних подій не зафіксовано. Це не підтверджує відсутність збоїв, поки панель була закрита.';
  root.appendChild(empty);return;
 }
 for(const entry of entries){
  const card=document.createElement('article');card.className='event-entry';
  const heading=document.createElement('div');heading.className='event-entry-head';
  const title=document.createElement('strong');title.className='event-entry-title';
  title.textContent=EVENT_CATALOG[entry.code][0];
  const badge=document.createElement('span');
  badge.className='source-health-status '+(entry.severity==='critical'?'missing':
   entry.severity==='warning'?'warn':'ok');
  badge.textContent=entry.severity==='critical'?'Критична':
   entry.severity==='warning'?'Попередження':'Інформаційна';
  const stamp=document.createElement('div');stamp.className='event-entry-meta';
  const utc=new Date(entry.at).toISOString();
  stamp.textContent=(entry.phase==='signal'?'День UTC: '+utc.slice(0,10):
   'Спостерігали: '+utc.slice(0,16).replace('T',' ')+' UTC')+
   ' · '+EVENT_CATALOG[entry.code][1].toUpperCase();
  const detail=document.createElement('p');detail.className='event-entry-message';
  detail.textContent=entry.phase==='restored'?'Під час наступного спостереження показник повернувся до норми.':
   entry.phase==='signal'?'Добовий пороговий сигнал зі зведених агрегатів, не підтверджена атака.':
   entry.code.startsWith('quota.')?'Досягнуто контрольного порогу; це не платіжний рахунок Cloudflare.':
   'Стан потребував перевірки або дані джерела не підтверджені; це не доказ простою.';
  heading.append(title,badge);card.append(heading,stamp,detail);root.appendChild(card);
 }
}
function ingestTechnicalEvents(responses){
 technicalJournal=updateEventJournal(technicalJournal,responses);
 saveTechnicalJournal();renderTechnicalEvents();
}
for(const id of ['tech-filter-severity','tech-filter-period','tech-filter-group'])
 q(id).addEventListener('change',renderTechnicalEvents);
q('tech-events-clear').addEventListener('click',()=>{
 technicalJournal.events=[];saveTechnicalJournal();renderTechnicalEvents();
});
renderTechnicalEvents();
`;
