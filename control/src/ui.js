// No external fonts, scripts, tracking, images, dependencies or CDNs.
export const html = `<!doctype html>
<html lang="uk">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="dark">
<title>MaVka Server Control</title>
<style nonce="__CSP_NONCE__">
:root{color-scheme:dark;--bg:#0b101b;--card:#151d2b;--edge:#273246;--muted:#94a6be;--bright:#f4f7fc;--accent:#59d9c4;--warning:#f9c46b;--red:#ff8d91;--blue:#91b4fa}
*{box-sizing:border-box}html{font-family:system-ui,-apple-system,"Segoe UI",sans-serif;font-size:15px}
body{margin:0;min-height:100vh;background:radial-gradient(ellipse at 8% 0%,#192b39 0,transparent 42%),var(--bg);color:var(--bright)}
.shell{width:min(1220px,calc(100% - 32px));margin:auto}
header{border-bottom:1px solid var(--edge);background:#0d1421c7;position:sticky;top:0;backdrop-filter:blur(14px);z-index:3}
.head{display:flex;align-items:center;justify-content:space-between;gap:15px;padding:17px 0}
.brand{display:flex;align-items:center;gap:11px;font-size:17px;font-weight:800;letter-spacing:.25px}
.logo{width:34px;height:34px;border-radius:11px;display:grid;place-items:center;background:linear-gradient(145deg,#146e68,#59d9c4);color:#071817;font-weight:900}
.subtitle{color:var(--muted);font-size:12px;font-weight:450;margin-top:4px}
.pill{padding:7px 11px;border:1px solid var(--edge);border-radius:40px;font-size:12px;color:var(--muted)}
.toolbar{display:flex;align-items:center;gap:10px;flex-wrap:wrap}
button{font:inherit;background:#223649;color:var(--bright);border:1px solid #3c596d;border-radius:10px;cursor:pointer;padding:10px 15px}
button:hover{border-color:var(--accent)}button:disabled{opacity:.5;cursor:default}
.main{padding-top:31px;padding-bottom:40px}
.topline{display:flex;justify-content:space-between;gap:15px;align-items:flex-end;flex-wrap:wrap;margin-bottom:23px}
h1{font-size:clamp(25px,4vw,36px);margin:0 0 7px;font-weight:750;letter-spacing:-.8px}
h2{font-size:17px;margin:0 0 15px;letter-spacing:-.1px}
p{line-height:1.5}.dim{color:var(--muted);font-size:13px;margin:0}
.notice{padding:13px 17px;border:1px solid #3b5360;background:#142731;border-radius:12px;margin-bottom:18px;font-size:13px;line-height:1.5;color:#cae7e3}
.notice.warn{border-color:#5a4c30;background:#29251c;color:#ffe0a6}
.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px}
.panel{border:1px solid var(--edge);border-radius:16px;padding:19px;background:var(--card)}
.metric-label{font-size:13px;color:var(--muted);min-height:20px}
.metric{font-size:clamp(23px,3vw,32px);font-variant-numeric:tabular-nums;font-weight:780;margin:11px 0 7px;letter-spacing:-1px}
.metric-note{font-size:11px;color:var(--muted);line-height:1.45}
.section{margin-top:26px}
.columns{display:grid;grid-template-columns:1.18fr 1fr;gap:14px}
.line{display:flex;align-items:center;justify-content:space-between;gap:13px;margin:15px 0 9px}
.line .name{font-size:13px}.line .info{font-size:12px;color:var(--muted);font-variant-numeric:tabular-nums;text-align:right}
.bar{height:9px;background:#283449;border-radius:99px;overflow:hidden}
.fill{height:100%;width:0;background:linear-gradient(90deg,#388f9b,var(--accent));transition:width .25s}
.fill.warn{background:var(--warning)}.fill.danger{background:var(--red)}
.stats{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:14px}
.small-card{border:1px solid #2d3b51;background:#101826;padding:13px;border-radius:11px}
.small-card b{font-size:19px;font-variant-numeric:tabular-nums}.small-card div{color:var(--muted);font-size:11px;margin-top:4px}
.status{display:flex;align-items:center;gap:9px;font-size:13px;color:var(--muted)}
.dot{width:8px;height:8px;border-radius:50%;background:var(--warning);display:inline-block}.dot.ok{background:var(--accent)}.dot.err{background:var(--red)}
table{width:100%;border-collapse:collapse;text-align:left;font-size:12px}
th{color:var(--muted);font-weight:500;padding:12px 10px;border-bottom:1px solid var(--edge)}
td{padding:12px 10px;border-bottom:1px solid #263247;font-variant-numeric:tabular-nums}
.table-scroll{overflow-x:auto}
a{color:#9cdfe0;text-decoration:none}a:hover{text-decoration:underline}
.footer{margin-top:27px;border-top:1px solid var(--edge);padding:17px 0;color:var(--muted);font-size:12px;line-height:1.5}
@media(max-width:950px){.grid{grid-template-columns:repeat(2,minmax(0,1fr))}.columns{grid-template-columns:1fr}}
@media(max-width:540px){.head{align-items:flex-start}.brand{font-size:14px}.pill{display:none}.grid{grid-template-columns:1fr}.panel{padding:16px}.toolbar button{padding:9px}.main{padding-top:20px}}

.chart-head{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;margin-bottom:17px}
.selector{border:1px solid var(--edge);border-radius:9px;background:#0e1724;color:var(--bright);padding:9px 10px;min-width:190px;font:inherit}
.tabs{display:flex;gap:4px;padding:3px;background:#0e1724;border:1px solid var(--edge);border-radius:12px}
.tabs button{background:transparent;border:0;color:var(--muted);padding:8px 13px;min-width:57px;font-size:13px}
.tabs button.active{background:#254758;color:#d8fff6;box-shadow:0 1px 6px #040e15}
.chart-box{min-height:250px;position:relative;background:#0e1724;border:1px solid #27364c;border-radius:12px;padding:10px}
.chart-box svg{width:100%;height:auto;display:block;min-height:220px}
.chart-grid{stroke:#314259;stroke-dasharray:4 7;stroke-width:1}
.chart-line{fill:none;stroke:#59d9c4;stroke-width:2.8;stroke-linejoin:round;stroke-linecap:round}
.chart-point{fill:#59d9c4}
.chart-tick{fill:#92a7bf;font:13px system-ui,sans-serif}
.chart-empty{display:grid;place-items:center;min-height:220px;text-align:center;color:var(--muted);font-size:13px;padding:18px}
.chart-meta{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap;margin-top:13px;color:var(--muted);font-size:12px}
.small-btn{padding:8px 13px;font-size:12px}
.read-only{border:1px solid var(--edge);padding:13px 16px;border-radius:11px;background:#0e1724}
.read-only h3{margin:0 0 8px;font-size:15px}.read-only p{margin:0;color:var(--muted);font-size:12px}
@media(max-width:540px){.selector{width:100%;min-width:0}.chart-head{align-items:stretch}.tabs{width:100%}.tabs button{flex:1}}
</style>
</head>
<body>
<header><div class="shell head"><div class="brand"><div class="logo">M</div><div>
MaVka Server Control<div class="subtitle">Cloudflare · керування ресурсами</div></div></div>
<div class="toolbar"><span class="pill">Режим лише перегляду</span><button id="refresh" type="button">Оновити</button></div></div></header>
<main class="shell main">
<div class="topline"><div><h1>Моніторинг інфраструктури</h1><p class="dim">Окремо від робочого сервера «Мавки». Усі суми — з API Cloudflare, без демоданих.</p></div><div class="status"><span id="dot" class="dot"></span><span id="state">Завантаження даних…</span></div></div>
<div id="notice" class="notice">Деякі квоти рахуються на рівні акаунта, інші — окремого сервісу. Нарахування перевіряйте у Cloudflare Billing.</div>
<div class="grid">
<article class="panel"><div class="metric-label">Workers · mavka-relay</div><div class="metric" id="requests">—</div><div class="metric-note">Виклики за поточну добу UTC. Не весь акаунт.</div></article>
<article class="panel"><div class="metric-label">Workers · системні помилки</div><div class="metric" id="errors">—</div><div class="metric-note">Помилки виконання, не HTTP 401/403.</div></article>
<article class="panel"><div class="metric-label">D1 · прочитані рядки</div><div class="metric" id="reads">—</div><div class="metric-note">Усі бази акаунта за добу UTC.</div></article>
<article class="panel"><div class="metric-label">D1 · записані рядки</div><div class="metric" id="writes">—</div><div class="metric-note">Усі бази акаунта за добу UTC.</div></article>
</div>
<div class="section columns">
<section class="panel">
<h2>Використання безкоштовних квот</h2>
<p class="dim">Пороги 70%, 85%, 95%. «—» означає, що дані ще не надійшли.</p>
<div class="line"><span class="name">Workers · лише Relay</span><span class="info" id="wr-limit">— / 100 000</span></div><div class="bar"><div class="fill" id="bar-wr"></div></div>
<div class="line"><span class="name">D1 · читання</span><span class="info" id="dr-limit">— / 5 млн</span></div><div class="bar"><div class="fill" id="bar-dr"></div></div>
<div class="line"><span class="name">D1 · запис</span><span class="info" id="dw-limit">— / 100 000</span></div><div class="bar"><div class="fill" id="bar-dw"></div></div>
<div class="line"><span class="name">R2 · зайнято / 5 ГБ (контрольний поріг)</span><span class="info" id="r2-limit">—</span></div><div class="bar"><div class="fill" id="bar-r2"></div></div>
<p class="dim" style="margin-top:17px">Власний поріг R2 — 5 ГБ: попередження при 70%, 85% і 95%. Це не блокує завантаження. Cloudflare рахує оплату за ГБ-місяці, а не за поточний обсяг. Workers показує лише один застосунок.</p>
</section>
<section class="panel">
<h2>R2 · mavka-private-files</h2>
<p class="dim">Зберігаємо зашифровані вкладення; панель не читає їхній вміст.</p>
<div class="stats">
<div class="small-card"><b id="r2-size">—</b><div>Збережено даних зараз</div></div>
<div class="small-card"><b id="r2-objects">—</b><div>Об'єктів у сховищі</div></div>
<div class="small-card"><b id="r2-ops">—</b><div>Операцій за місяць*</div></div>
<div class="small-card"><b>—</b><div>Залишок Class A / B*</div></div>
</div><p class="dim" style="margin-top:13px">* Кількість операцій не є розбивкою на платіжні класи A/B. Класифікацію додамо після перевірки точних даних.</p>
<p class="dim" id="updated" style="margin-top:15px">Остання перевірка: —</p>
</section>
</div>
<section class="panel section">
<div class="chart-head"><div><h2 style="margin:0 0 5px">Графік використання ресурсів</h2><p class="dim">Реальні знімки D1 за вибраний період. Пропуски не заповнюються вигаданими даними.</p></div>
<div class="tabs" role="group" aria-label="Період графіка"><button class="active" id="period-24h" type="button" aria-pressed="true">24 год</button><button id="period-7d" type="button" aria-pressed="false">7 днів</button><button id="period-30d" type="button" aria-pressed="false">30 днів</button></div></div>
<div class="chart-head"><label for="trend-metric" class="dim">Показник для графіка</label>
<select id="trend-metric" class="selector">
<option value="workers_requests">Запити Relay · з початку доби</option>
<option value="workers_errors">Помилки виконання Relay</option>
<option value="d1_rows_read">D1 · прочитані рядки</option>
<option value="d1_rows_written">D1 · записані рядки</option>
<option value="r2_payload_bytes">R2 · зайнятий обсяг</option>
<option value="r2_objects">R2 · кількість об'єктів</option>
</select></div>
<div id="chart" class="chart-box" role="img" aria-label="Графік реальних вимірювань"><div class="chart-empty">Завантаження історії…</div></div>
<div class="chart-meta"><span id="chart-coverage">Немає даних</span><span>Лічильники Workers і D1 обнуляються кожної доби UTC; записи — лише під час відкриття панелі.</span></div>
</section>
<section class="panel section">
<div class="topline" style="margin-bottom:12px"><div><h2 style="margin:0">Історія спостережень</h2><p class="dim">Посторінковий перегляд збережених даних за вибраний період — до 30 днів.</p></div><span class="status" id="history-state">База: перевіряємо…</span></div>
<div class="table-scroll"><table><thead><tr><th>Час (UTC)</th><th>Relay запити</th><th>D1 читання</th><th>D1 запис</th><th>R2 байти</th></tr></thead><tbody id="rows"><tr><td colspan="5">Записів ще немає</td></tr></tbody></table></div>
<div style="margin-top:14px;display:flex;justify-content:flex-end"><button class="small-btn" id="more-history" type="button" hidden>Показати ще 50 записів</button></div>
</section>
<section class="section columns" aria-label="Контроль безпеки">
<div class="read-only"><h3>Журнал безпеки Relay</h3><p id="security-status">Джерело агрегованих подій ще не підключене. Наявні записи MAVKA_RELAY_SECURITY_V1 можна переглядати в Cloudflare Observability. Тут не відображаємо IP, ключі, підписи або вміст повідомлень.</p></div>
<div class="read-only"><h3>Стан пристроїв</h3><p>Безпечний канал статистики пристроїв поки не підключено. Консоль не отримує доступу до приватних таблиць або вмісту повідомлень Relay.</p></div>
</section>
<div class="footer">MaVka Server Control 0.2.0 · тільки читання · інтервал 15 хв · жодного доступу до текстів повідомлень, ключів або файлів. Показники можуть надходити із затримкою. Фактичні платежі та квоти перевіряйте в <a href="https://dash.cloudflare.com/" target="_blank" rel="noopener noreferrer">Cloudflare Dashboard</a>.</div>
</main>
<script nonce="__CSP_NONCE__">
(function(){
'use strict';
const q = id => document.getElementById(id);
const fmt = value => Number.isFinite(value) ? new Intl.NumberFormat('uk-UA',{maximumFractionDigits:0}).format(value) : '—';
const size = n => Number.isFinite(n) ? (n/1e6).toLocaleString('uk-UA',{maximumFractionDigits:2})+' МБ' : '—';
function progress(id,used,limit){
  const el=q(id);if(!Number.isFinite(used)||!Number.isFinite(limit)||limit<=0){el.style.width='0%';return}
  const ratio=used/limit;el.style.width=(Math.min(ratio*100,100))+'%';
  el.className='fill'+(ratio>=.95?' danger':ratio>=.85?' warn':'');
}
async function get(path){
 const res=await fetch(path,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
 if(!res.ok)throw Error('HTTP '+res.status);
 return res.json();
}
function state(v,bad=false){q('state').textContent=v;q('dot').className='dot'+(bad?' err':v.includes('Готово')?' ok':'')}
function setMetric(id,v){q(id).textContent=fmt(v)}
function show(m){
 const wr=m.workers||{},d=m.d1||{},r=m.r2||{},st=r.storage||{},op=r.operations||{},l=m.limits||{};
 setMetric('requests',wr.requestsToday);setMetric('errors',wr.errorsToday);
 setMetric('reads',d.rowsReadToday);setMetric('writes',d.rowsWrittenToday);
 q('r2-size').textContent=size(st.bytes);setMetric('r2-objects',st.objects);setMetric('r2-ops',op.requestsThisMonth);
 q('wr-limit').textContent=fmt(wr.requestsToday)+' / '+fmt(l.workersRequestsPerDay);
 q('dr-limit').textContent=fmt(d.rowsReadToday)+' / '+fmt(l.d1RowsReadPerDay);
 q('dw-limit').textContent=fmt(d.rowsWrittenToday)+' / '+fmt(l.d1RowsWrittenPerDay);
 q('r2-limit').textContent=Number.isFinite(st.bytes)?size(st.bytes)+' / '+(Number.isFinite(l.r2MonitoringBytes)?(l.r2MonitoringBytes/1e9)+' ГБ':'5 ГБ'):'—';
 progress('bar-wr',wr.requestsToday,l.workersRequestsPerDay);
 progress('bar-dr',d.rowsReadToday,l.d1RowsReadPerDay);
 progress('bar-dw',d.rowsWrittenToday,l.d1RowsWrittenPerDay);
 progress('bar-r2',st.bytes,l.r2MonitoringBytes);
 q('updated').textContent='Отримано: '+new Date(m.at).toLocaleString('uk-UA')+' · дані можуть надходити із затримкою';
 const missing=(m.errors||[]).join(', ');
 const r2ratio=Number.isFinite(st.bytes)&&Number.isFinite(l.r2MonitoringBytes)&&l.r2MonitoringBytes>0 ? st.bytes/l.r2MonitoringBytes : null;
 const detail=m.diagnostics?Object.entries(m.diagnostics)
   .filter(([key,v])=>v!=='ok').map(([key,v])=>key+': '+v).join('; '):'';
 q('notice').className='notice'+(m.state!=='ready'||r2ratio>=0.7?' warn':'');
 q('notice').textContent=m.state==='unconfigured'?
 'Аналітику не підключено: для справжніх показників потрібні CF_ACCOUNT_ID і захищений CF_ANALYTICS_TOKEN.':
 m.state==='ready'?(r2ratio>=0.95?'УВАГА: R2 досяг 95% власного порогу 5 ГБ. ':r2ratio>=0.85?'R2 перевищив 85% власного порогу 5 ГБ. ':r2ratio>=0.70?'R2 перевищив 70% власного порогу 5 ГБ. ':'')+'Аналітику підключено. Поріг R2 — моніторинговий, не заборона запису; перевіряйте Billing.':
 'Частина показників недоступна ('+missing+'). '+(detail?'Причини: '+detail+'. ':'')+'Це не нульове споживання; не розширюйте дозволи токена без перевірки.';
 state(m.state==='ready'?'Готово · API Cloudflare':m.state==='partial'?'Часткові дані':'Потрібне підключення',m.state==='unavailable');
}

const chartNS='http://www.w3.org/2000/svg';
let activePeriod='24h',activeMetric='workers_requests',trendEntries=[],historyCursor=null,visibleRows=[];
const fmtCompact=v=>Number.isFinite(v)?new Intl.NumberFormat('uk-UA',{notation:'compact',maximumFractionDigits:1}).format(v):'—';
function node(tag,attrs,parent){
 const el=document.createElementNS(chartNS,tag);
 for(const [name,value] of Object.entries(attrs||{}))el.setAttribute(name,String(value));
 if(parent)parent.appendChild(el);
 return el;
}
function drawTrends(t){
 const parent=q('chart');parent.replaceChildren();
 const list=(t?.entries||[]).filter(x=>Number.isFinite(x.bucket_start));
 trendEntries=list;
 if(t?.state!=='ready'||!list.length){
   const div=document.createElement('div');div.className='chart-empty';
   div.textContent=t?.state==='ready'?'За цей період ще немає збережених вимірювань.':
     'Історія D1 тимчасово недоступна.';
   parent.appendChild(div);q('chart-coverage').textContent='Знімків: 0';return;
 }
 const W=920,H=258,L=68,R=23,T=22,B=41,w=W-L-R,h=H-T-B;
 const values=list.map(x=>({t:x.bucket_start,v:Number.isFinite(x[activeMetric])?x[activeMetric]:null}));
 const valid=values.filter(x=>x.v!==null);
 if(!valid.length){
   const div=document.createElement('div');div.className='chart-empty';
   div.textContent='Немає достовірних значень цього показника.';parent.appendChild(div);
   q('chart-coverage').textContent='Знімків: '+list.length;return;
 }
 const domainStart=Number.isFinite(t.from)?t.from:values[0].t;
 const domainEnd=Number.isFinite(t.to)?t.to:values.at(-1).t+1;
 const max=Math.max(1,...valid.map(x=>x.v))*1.08;
 const X=p=>L+(p-domainStart)/Math.max(1,domainEnd-domainStart)*w;
 const Y=v=>T+(1-v/max)*h;
 const svg=node('svg',{viewBox:'0 0 '+W+' '+H,'aria-hidden':'true'},parent);
 for(let i=0;i<=4;i++){
  const val=max*i/4,yy=Y(val);
  node('line',{x1:L,x2:W-R,y1:yy,y2:yy,class:'chart-grid'},svg);
  const tx=node('text',{x:L-12,y:yy+5,'text-anchor':'end',class:'chart-tick'},svg);
  tx.textContent=activeMetric==='r2_payload_bytes'?fmtCompact(val/1e6)+' МБ':fmtCompact(val);
 }
 for(let i=0;i<=4;i++){
  const pos=domainStart+(domainEnd-domainStart)*i/4, xx=X(pos);
  const d=new Date(pos*1000);
  const tx=node('text',{x:xx,y:H-12,'text-anchor':i===0?'start':i===4?'end':'middle',class:'chart-tick'},svg);
  tx.textContent=activePeriod==='24h'?d.toISOString().slice(11,16):d.toISOString().slice(5,10);
 }
 const gap=t.intervalSeconds||900;
 let segments=[],seg=[],last=null;
 for(const row of values){
   if(row.v===null||(last!==null&&row.t-last>gap*1.6)){
     if(seg.length)segments.push(seg);
     seg=[];
   }
   if(row.v!==null)seg.push([X(row.t),Y(row.v),row]);
   last=row.t;
 }
 if(seg.length)segments.push(seg);
 for(const group of segments){
   if(group.length>1) node('polyline',{points:group.map(p=>p[0]+','+p[1]).join(' '),class:'chart-line'},svg);
   for(const [x,y,data] of group){
     const c=node('circle',{cx:x,cy:y,r:3.6,class:'chart-point'},svg);
     const title=node('title',{},c);
     title.textContent=new Date(data.t*1000).toISOString().slice(0,16).replace('T',' ')+' UTC: '+(activeMetric==='r2_payload_bytes'?size(data.v):fmt(data.v));
   }
 }
 const cap='Знімків: '+list.length+' · із реальними значеннями: '+valid.length;
 q('chart-coverage').textContent=cap;
 parent.setAttribute('aria-label',cap+' за '+activePeriod);
}
function appendRows(entries,reset){
 const body=q('rows');
 if(reset){body.replaceChildren();visibleRows=[];}
 const incoming=entries||[];
 if(!incoming.length&&!visibleRows.length){
   const tr=document.createElement('tr'),td=document.createElement('td');
   td.colSpan=5;td.textContent='Збережених записів у цьому періоді немає';tr.appendChild(td);body.appendChild(tr);
   return;
 }
 for(const s of incoming){
  const tr=document.createElement('tr'),vals=[new Date(s.bucket_start*1000).toISOString().slice(0,16).replace('T',' '),
  fmt(s.workers_requests),fmt(s.d1_rows_read),fmt(s.d1_rows_written),size(s.r2_payload_bytes)];
  for(const value of vals){const td=document.createElement('td');td.textContent=value;tr.appendChild(td)}
  body.appendChild(tr);visibleRows.push(s);
 }
}
async function loadHistory(before=null,reset=true){
 let query='/api/history?period='+activePeriod;
 if(before!==null)query+='&before='+encodeURIComponent(before);
 const data=await get(query);
 q('history-state').textContent=data.state==='ready'?'D1 підключена · '+activePeriod:
 data.state==='not_bound'?'D1 не підключена':'Не вдалося прочитати історію';
 if(data.state!=='ready'){appendRows([],reset);q('more-history').hidden=true;return}
 appendRows(data.entries,reset);
 historyCursor=data.nextBefore;
 q('more-history').hidden=!historyCursor;
}
async function loadTrends(){
 const t=await get('/api/trends?period='+activePeriod);
 drawTrends(t);
}
let loading=false;
async function refresh(){
 if(loading)return;loading=true;q('refresh').disabled=true;state('Читаємо Cloudflare…');
 try {
  const results=await Promise.allSettled([get('/api/summary'),loadHistory(),loadTrends(),get('/api/security')]);
  if(results[0].status==='fulfilled')show(results[0].value);
  else {state('Не вдалося отримати аналітику',true);q('notice').className='notice warn';
    q('notice').textContent='Аналітика тимчасово недоступна. Авторизуйся знову через Cloudflare Access за потреби.'}
  if(results[3].status==='fulfilled'&&results[3].value.state==='not_connected')
    q('security-status').textContent='Не підключено. Події MAVKA_RELAY_SECURITY_V1 зберігаються в Cloudflare Observability; панель не читає приватні логи, IP-адреси, ключі й повідомлення. Для підключення потрібен окремий обмежений канал телеметрії.';
 } finally {loading=false;q('refresh').disabled=false}
}
async function setPeriod(period){
 if(!['24h','7d','30d'].includes(period))return;
 activePeriod=period;
 for(const name of ['24h','7d','30d']){
  const el=q('period-'+name),sel=name===period;
  el.classList.toggle('active',sel);el.setAttribute('aria-pressed',String(sel));
 }
 await Promise.allSettled([loadHistory(),loadTrends()]);
}
for(const period of ['24h','7d','30d'])
 q('period-'+period).addEventListener('click',()=>setPeriod(period));
q('trend-metric').addEventListener('change',event=>{
 activeMetric=event.target.value;
 // Cache latest server history only; do not generate fabricated points.
 drawTrends(lastTrends);
});
let lastTrends={state:'ready',entries:[]};
const originalDraw=drawTrends;
// Store raw observations for metric switching without additional server calls.
drawTrends=function(t){lastTrends=t;originalDraw(t)};
q('more-history').addEventListener('click',async ()=>{
 if(!historyCursor)return;
 const button=q('more-history');button.disabled=true;
 try{await loadHistory(historyCursor,false)}
 finally{button.disabled=false}
});
q('refresh').addEventListener('click',refresh);
refresh();setInterval(()=>{if(!document.hidden)refresh()},900000);
})();
</script>
</body></html>`;
