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
.security-hours{display:flex;align-items:end;gap:3px;height:55px;min-height:55px;
 border-bottom:1px solid var(--edge);margin-top:8px}
.security-hours>div{flex:1;min-width:3px;background:#57c8ad;border-radius:3px 3px 0 0}
.security-line{display:flex;gap:8px;align-items:center;justify-content:space-between;
 border-bottom:1px solid var(--edge);padding:5px 0;font-size:12px}
.security-line span{overflow-wrap:anywhere}.security-line b{flex-shrink:0;font-variant-numeric:tabular-nums}

@media(max-width:540px){.selector{width:100%;min-width:0}.chart-head{align-items:stretch}.tabs{width:100%}.tabs button{flex:1}}
.alert-item{border-bottom:1px solid var(--edge);padding:9px 0;display:flex;justify-content:space-between;gap:10px;align-items:flex-start}
.alert-item strong{font-size:12px;line-height:1.6}.alert-item small{display:block;color:var(--muted);font-size:11px}
.alert-level{flex-shrink:0;font-size:11px;font-weight:650;border-radius:7px;border:1px solid #375363;padding:4px 7px;color:#b6d9dc}
.alert-level.critical{color:#ffc3c3;border-color:#8b4b52}
.alert-level.warning{color:#f7d597;border-color:#725d39}
.retention-note{padding:12px;border:1px solid var(--edge);border-radius:10px;color:var(--muted);font-size:12px;line-height:1.55;margin-top:13px}

html{scroll-behavior:smooth}
.jump-target{scroll-margin-top:105px}
.quick-nav{position:fixed;right:16px;top:96px;z-index:8;max-width:calc(100vw - 24px)}
.quick-nav details{position:relative}
.quick-nav summary{list-style:none;cursor:pointer;display:flex;align-items:center;gap:8px;
 background:#1b3545;color:#e0fff7;border:1px solid #476b76;border-radius:11px;
 padding:10px 13px;font-size:12px;font-weight:650;box-shadow:0 8px 22px #050b14b3}
.quick-nav summary::-webkit-details-marker,.history-details summary::-webkit-details-marker{display:none}
.quick-nav summary::before{content:"☰";font-size:16px;font-weight:400}
.quick-nav details[open] summary{border-color:var(--accent);border-radius:11px 11px 0 0}
.quick-nav summary:focus-visible,.history-details summary:focus-visible,.quick-links a:focus-visible{outline:2px solid var(--accent);outline-offset:3px}
.quick-links{position:absolute;right:0;top:100%;width:min(275px,calc(100vw - 24px));
 max-height:min(73vh,520px);overflow-y:auto;display:grid;gap:3px;padding:9px;
 border:1px solid #476b76;background:#111e2e;border-radius:12px 0 12px 12px;
 box-shadow:0 16px 40px #050b15dd}
.quick-links a{display:block;padding:10px 11px;border-radius:8px;color:#c4d5e6;
 font-size:12px;line-height:1.4;text-decoration:none}
.quick-links a:hover,.quick-links a.active{color:#d7fff7;background:#214653;text-decoration:none}
.history-details summary{list-style:none;cursor:pointer;display:flex;justify-content:space-between;
 align-items:center;flex-wrap:wrap;gap:12px;padding:2px 0 14px}
.history-details summary::-webkit-details-marker{display:none}
.history-heading{display:grid;gap:5px;flex:1;min-width:180px}
.history-heading h2{margin:0}
.history-summary-end{display:flex;align-items:center;gap:12px;flex-wrap:wrap}
.history-toggle{font-size:12px;border:1px solid #3c596d;background:#213549;
 border-radius:9px;padding:8px 11px;color:#ddf7f1;white-space:nowrap}
.history-toggle::after{content:"Згорнути  ▴"}
.history-details:not([open]) .history-toggle::after{content:"Розгорнути  ▾"}
.history-details:not([open]) summary{padding-bottom:0}
.history-content{padding-top:10px;border-top:1px solid var(--edge)}
@media(max-width:750px){
 .quick-nav{top:auto;right:12px;bottom:14px}
 .quick-links{top:auto;bottom:calc(100% - 1px);border-radius:12px 12px 0 12px;max-height:62vh}
 .quick-nav details[open] summary{border-radius:0 0 11px 11px}
 .history-summary-end{width:100%;justify-content:space-between}
 .footer{padding-bottom:65px}
}
@media(prefers-reduced-motion:reduce){html{scroll-behavior:auto}}
</style>
</head>
<body>
<header><div class="shell head"><div class="brand"><div class="logo">M</div><div>
MaVka Server Control<div class="subtitle">Cloudflare · керування ресурсами</div></div></div>
<div class="toolbar"><span class="pill">Режим лише перегляду</span><button id="refresh" type="button">Оновити</button></div></div></header>
<nav class="quick-nav" aria-label="Швидкий перехід до розділів">
<details id="quick-nav"><summary>Розділи</summary>
<div class="quick-links">
<a href="#overview">Огляд сервера</a>
<a href="#resources">Квоти та сховище</a>
<a href="#trends">Графік ресурсів</a>
<a href="#history">Історія спостережень</a>
<a href="#risks">Ризики та ліміти</a>
<a href="#maintenance">Автоматичне очищення</a>
<a href="#r2-lifecycle">Контроль R2</a>
<a href="#security">Журнал безпеки</a>
<a href="#devices">Стан пристроїв</a>
</div></details></nav>
<main class="shell main">
<div class="topline jump-target" id="overview"><div><h1>Моніторинг інфраструктури</h1><p class="dim">Окремо від робочого сервера «Мавки». Усі суми — з API Cloudflare, без демоданих.</p></div><div class="status"><span id="dot" class="dot"></span><span id="state">Завантаження даних…</span></div></div>
<div id="notice" class="notice">Деякі квоти рахуються на рівні акаунта, інші — окремого сервісу. Нарахування перевіряйте у Cloudflare Billing.</div>
<div class="grid">
<article class="panel"><div class="metric-label">Workers · mavka-relay</div><div class="metric" id="requests">—</div><div class="metric-note">Виклики за поточну добу UTC. Не весь акаунт.</div></article>
<article class="panel"><div class="metric-label">Workers · системні помилки</div><div class="metric" id="errors">—</div><div class="metric-note">Помилки виконання, не HTTP 401/403.</div></article>
<article class="panel"><div class="metric-label">D1 · прочитані рядки</div><div class="metric" id="reads">—</div><div class="metric-note">Усі бази акаунта за добу UTC.</div></article>
<article class="panel"><div class="metric-label">D1 · записані рядки</div><div class="metric" id="writes">—</div><div class="metric-note">Усі бази акаунта за добу UTC.</div></article>
</div>
<div class="section columns jump-target" id="resources">
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
<section class="panel section jump-target" id="trends">
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
<section class="panel section jump-target" id="history">
<details class="history-details" id="history-details" open>
<summary aria-controls="history-content"><span class="history-heading">
<h2>Історія спостережень</h2>
<span class="dim" id="history-preview">Посторінковий перегляд · до 30 днів</span></span>
<span class="history-summary-end"><span class="status" id="history-state">База: перевіряємо…</span>
<span class="history-toggle" aria-hidden="true"></span></span></summary>
<div class="history-content" id="history-content">
<div class="table-scroll"><table><thead><tr><th>Час (UTC)</th><th>Relay запити</th><th>D1 читання</th><th>D1 запис</th><th>R2 байти</th></tr></thead><tbody id="rows"><tr><td colspan="5">Записів ще немає</td></tr></tbody></table></div>
<div style="margin-top:14px;display:flex;justify-content:flex-end;align-items:center;gap:10px;flex-wrap:wrap"><span id="history-export-status" class="dim" role="status" aria-live="polite"></span><button class="small-btn" id="export-history" type="button">Експорт історії CSV</button><button class="small-btn" id="more-history" type="button" hidden>Показати ще 50 записів</button></div>
</div></details></section>
<section class="panel section jump-target" id="risks" aria-label="Сигнали та контроль квот">
<div class="chart-head"><div><h2 style="margin:0 0 6px">Контроль ризиків і лімітів</h2>
<p class="dim">Оцінюємо лише підтверджені агрегати і поточні показники. Жодного додаткового фонового опитування.</p></div><span class="pill">На основі наявних знімків</span></div>
<div class="columns">
<div class="read-only"><h3>Попередження про ресурси</h3>
<p id="quota-summary" role="status">Завантаження показників…</p>
<div id="quota-alerts" aria-live="polite"></div></div>
<div class="read-only"><h3>Сигнали безпеки · останні 14 днів</h3>
<p id="incident-summary" role="status">Завантаження агрегованої історії…</p>
<div id="incident-list" aria-live="polite"></div>
<p style="margin-top:10px">Це виявлені за порогами сигнали, а не підтверджені атаки. Перевірка виконується під час оновлення панелі; окремі push/email сповіщення поки не налаштовані.</p></div>
</div>
<div class="retention-note"><strong>Автоматичне очищення й витрати.</strong>
 Історія показників Control — 30 днів; агрегати безпеки Relay — 30 днів;
 огляд сигналів — останні 14 днів без дублювання в новій базі.
 Тимчасові зашифровані частини R2 у <code>transfers/</code> — життєвий цикл 7 днів.
 Relay очищує чергу й прострочені службові записи кожні 5 хвилин,
 а старі агрегати безпеки перевіряє щогодини. Відкликані реєстрації пристроїв та
 неотримані документи автоматично через поріг R2 у 5 ГБ не видаляємо.</div>
</section>
<section class="panel section jump-target" id="maintenance" aria-label="Контроль автоматичного очищення">
<div class="chart-head"><div><h2 style="margin:0 0 6px">Автоматичне очищення · контроль роботи</h2>
<p class="dim">Показуємо тільки останній погодинний контрольний знімок Relay, без повторного читання таблиць документів.</p></div>
<span id="cleanup-pill" class="pill">Очікуємо стан</span></div>
<div class="columns">
<div class="read-only"><h3>Службове очищення Relay</h3>
<p id="cleanup-status" role="status" aria-live="polite">Отримуємо контрольну відмітку…</p>
<div class="stats" style="margin-top:13px">
<div class="small-card"><b id="cleanup-msg">—</b><div>Прострочені повідомлення, видалено під час контрольного запуску</div></div>
<div class="small-card"><b id="cleanup-nonces">—</b><div>Прострочені nonce, видалено під час запуску</div></div>
<div class="small-card"><b id="cleanup-receipts">—</b><div>Прострочені підтвердження, видалено під час запуску</div></div>
<div class="small-card"><b id="cleanup-rate">—</b><div>Старі ліміти частоти, видалено під час запуску</div></div>
</div>
<p id="cleanup-security" style="margin-top:10px">Стан очищення журналу безпеки — перевіряємо.</p></div>
<div class="read-only"><h3>Збереження та квоти</h3>
<p>Relay: повідомлення й службові TTL перевіряються кожні 5 хвилин.
Контрольна відмітка записується в один рядок D1 щогодини. Це не підрахунок за всю годину і не поточна кількість прострочених записів.</p>
<p style="margin-top:12px">Control: старіші за 30 днів знімки тепер видаляються автоматично о 03:17 UTC щодня, навіть якщо сторінка закрита.</p>
<p style="margin-top:12px">Журнал безпеки Relay: очищення щогодини (30 днів). Тимчасові файли R2 у <code>transfers/</code>: політика 7 днів. Основна черга й відкликані реєстрації не очищуються достроково заради квот.</p></div>
</div>
<div class="read-only jump-target" id="r2-lifecycle" style="margin-top:14px" aria-label="Контроль життєвого циклу R2">
<div class="chart-head" style="margin-bottom:10px"><div><h3>R2 · перевірка тимчасових частин</h3>
<p id="r2-audit-status" role="status" aria-live="polite">Перевіряємо результати останнього огляду…</p></div>
<span class="pill" id="r2-audit-pill">Немає знімка</span></div>
<div class="stats">
<div class="small-card"><b id="r2-audit-count">—</b><div>Перевірено об’єктів у transfers/</div></div>
<div class="small-card"><b id="r2-audit-bytes">—</b><div>Обсяг перевіреної частини, не всього R2</div></div>
<div class="small-card"><b id="r2-audit-overdue">—</b><div>Старші за 8 днів серед перевірених</div></div>
<div class="small-card"><b id="r2-audit-oldest">—</b><div>Найстаріший перевірений об’єкт</div></div>
</div>
<p id="r2-audit-warning" role="status" style="margin-top:10px">Підсумки огляду ще недоступні.</p>
<p style="margin-top:8px">Перевіряємо один список до 500 об’єктів кожні 6 годин, без скачування файлів.
Дані про обсяг — тільки для перевіреної частини префікса, не для всього бакета і не для обчислення вартості.
Правило Cloudflare R2 Lifecycle видаляє тимчасові об’єкти через 7 днів; видалення може затримуватися.
Файли не видаляємо вручну й не змінюємо чергу доставки.</p>
</div></section>
<section class="section columns jump-target" id="security" aria-label="Контроль безпеки">
<div class="read-only"><h3>Журнал безпеки Relay</h3>
<p id="security-status" role="status" aria-live="polite">Завантаження захищеної статистики…</p>
<div class="stats" style="margin-top:13px">
<div class="small-card"><b id="security-day">—</b><div>Спостережено відхилень за 24 год</div></div>
<div class="small-card"><b id="security-week">—</b><div>Спостережено відхилень за 7 днів</div></div>
</div>
<div id="security-warning" class="dim" style="margin-top:10px" role="status">Очікуємо перевірку.</div>
<div style="margin-top:14px"><h3 style="font-size:13px">Основні причини відхилень за 24 год</h3>
<div id="security-reasons" class="dim">Ще немає статистики.</div></div>
<div style="margin-top:13px"><h3 style="font-size:13px">Географія звернень</h3>
<div id="security-countries" class="dim">Ще немає статистики.</div></div>
<div style="margin-top:13px"><h3 style="font-size:13px">Активність за годинами UTC</h3>
<div id="security-hours" class="security-hours" aria-label="Погодинна статистика відхилень"></div></div>
<p style="margin-top:12px">Це приблизні агрегати з окремої таблиці D1 Relay, вони можуть занижувати кількість подій. Детальний журнал залишається у Cloudflare Observability. IP, ключі, ID, заголовки, підписи й повідомлення сюди не передаються.</p></div>
<div class="read-only jump-target" id="devices"><h3>Стан пристроїв Relay</h3>
<p id="devices-status">Перевіряємо приватний канал статистики…</p>
<div class="stats" style="margin-top:14px">
<div class="small-card"><b id="device-active">—</b><div>Підключені мобільні (активний статус)</div></div>
<div class="small-card"><b id="device-online">—</b><div>Зверталися протягом 30 хвилин</div></div>
<div class="small-card"><b id="device-pending">—</b><div>Очікують активації</div></div>
<div class="small-card"><b id="device-revoked">—</b><div>Відкликані</div></div>
</div><p class="dim" style="margin-top:11px">Це лише агреговані кількості, без ID, персональних даних, ключів, повідомлень і списків пристроїв. Активний статус не означає, що пристрій онлайн.</p></div>
</section>
<div class="footer">MaVka Server Control 0.7.1 · тільки читання · інтервал 15 хв · жодного доступу до текстів повідомлень, ключів або файлів. Показники можуть надходити із затримкою. Фактичні платежі та квоти перевіряйте в <a href="https://dash.cloudflare.com/" target="_blank" rel="noopener noreferrer">Cloudflare Dashboard</a>.</div>
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
const alertLabels={critical:'Критично',warning:'Попередження',info:'До відома'};
function alertRow(label,description,severity){
 const line=document.createElement('div');line.className='alert-item';
 const left=document.createElement('div'),a=document.createElement('strong'),b=document.createElement('small');
 a.textContent=label;b.textContent=description;left.append(a,b);
 const level=document.createElement('span');level.className='alert-level '+severity;
 level.textContent=alertLabels[severity]||'До відома';line.append(left,level);
 return line;
}
function showQuotaWarnings(info){
 const list=q('quota-alerts'),status=q('quota-summary');list.replaceChildren();
 if(!info||info.state==='unavailable'){
  status.textContent='Показники недоступні. Не сприймай це як нульове використання.';
  return;
 }
 const warnings=Array.isArray(info.warnings)?info.warnings:[];
 status.textContent=warnings.length?'Деякі показники наближаються до контрольних порогів.':
   'За доступними показниками перевищення порога 70% немає.';
 for(const w of warnings.slice(0,4)){
  const description=fmt(w.used)+' / '+fmt(w.threshold)+' · '+w.percent+'%'+
    (w.key==='r2_bytes'?' · це власний поріг, не платіжний ліміт':'');
  list.append(alertRow(w.label,description,w.severity));
 }
 if(info.missing?.length){
  const p=document.createElement('p');p.className='dim';p.style.marginTop='10px';
  p.textContent='Дані недоступні: '+info.missing.join(', ')+'. Не показуємо вигадані нулі.';
  list.append(p);
 }
}
function showIncidents(info){
 const list=q('incident-list'),status=q('incident-summary');list.replaceChildren();
 if(!info||info.state!=='ready'){
  status.textContent='Історія сигналів недоступна — перевіряємо з’єднання з Relay.';
  return;
 }
 const incidents=Array.isArray(info.incidents)?info.incidents:[];
 const visible=incidents.slice(0,35);
 const significant=incidents.filter(x=>x.severity==='critical'||x.severity==='warning').length;
 status.textContent='Збережені сигнали за 14 днів: '+fmt(incidents.length)+
  ', з них для перевірки: '+fmt(significant)+'. Дані орієнтовні.';
 if(!visible.length){
  const p=document.createElement('p');p.className='dim';p.style.marginTop='11px';
  p.textContent='За наявними агрегатами сигналів немає. Це не гарантує відсутності спроб доступу.';
  list.append(p);return;
 }
 for(const x of visible){
  const date=new Date(x.dayStart*1000).toISOString().slice(0,10);
  list.append(alertRow(x.label,date+' UTC · '+fmt(x.count)+' спостережень · потребує перевірки',x.severity));
 }
 if(incidents.length>visible.length){
  const tail=document.createElement('p');tail.className='dim';
  tail.textContent='Відображено перші 35 з '+fmt(incidents.length)+'. Решта входить у статистику.';
  list.append(tail);
 }
}
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
 showQuotaWarnings(m.quotaAlerts);
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
 // When history has only just begun, display the ACTUAL sampled time span
 // instead of compressing three observations into a 24-hour right corner.
 // The selected retention period stays unchanged and is labeled explicitly.
 const periodFrom=Number.isFinite(t.from)?t.from:values[0].t;
 const periodTo=Number.isFinite(t.to)?t.to:values.at(-1).t+1;
 const first=values[0].t,lastObservation=values.at(-1).t;
 const span=Math.max(0,lastObservation-first);
 const availableSpan=Math.max(1,periodTo-periodFrom);
 const zoomToSamples=span<availableSpan*0.5;
 const pad=Math.max(900,span*0.12);
 const domainStart=zoomToSamples?Math.max(periodFrom,first-pad):periodFrom;
 const domainEnd=zoomToSamples?Math.min(periodTo,lastObservation+pad):periodTo;
 const largest=Math.max(1,...valid.map(x=>x.v));
 const wholeNumber=activeMetric!=='r2_payload_bytes';
 const unit=wholeNumber?Math.max(1,Math.ceil(largest*1.06/4)):largest*1.06/4;
 const max=unit*4;
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
 const cap='Знімків: '+list.length+' · із реальними значеннями: '+valid.length+(zoomToSamples?' · вісь X наближено до наявних знімків (UTC)':'');
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
 if(data.state!=='ready'){appendRows([],reset);q('more-history').hidden=true;q('history-preview').textContent='Історія недоступна · '+activePeriod;return}
 appendRows(data.entries,reset);
 historyCursor=data.nextBefore;
 q('more-history').hidden=!historyCursor;
 updateHistoryPreview();
}
async function loadTrends(){
 const t=await get('/api/trends?period='+activePeriod);
 drawTrends(t);
}
let loading=false;
function showR2Audit(result){
 const pill=q('r2-audit-pill'),status=q('r2-audit-status'),warning=q('r2-audit-warning');
 if(!result?.data||!['ready','stale'].includes(result.state)){
  for(const id of ['r2-audit-count','r2-audit-overdue'])q(id).textContent='—';
  q('r2-audit-bytes').textContent='—';q('r2-audit-oldest').textContent='—';
  pill.textContent='Немає підтвердженого огляду';
  const reasons={
   no_r2_audit_yet:'Потрібне одноразове доповнення службової D1 (міграція 0005), а потім найближчий шестигодинний цикл.',
   r2_list_failed:'Не вдалося отримати список R2. Перевір прив’язку FILES і Workers Logs.',
   r2_binding_missing:'У Relay немає прив’язки приватного R2 FILES.',
   invalid_audit:'Результат аудиту некоректний. Не показуємо вигаданих нулів.'
  };
  status.textContent=reasons[result?.reason]||
   'Немає підключеного огляду тимчасових файлів R2.';
  warning.textContent='Огляд не підтверджено; це не означає, що R2 порожній.';
  return;
 }
 const d=result.data;
 q('r2-audit-count').textContent=fmt(d.sampledObjects);
 q('r2-audit-bytes').textContent=size(d.sampledBytes);
 q('r2-audit-overdue').textContent=fmt(d.overdueObjects);
 q('r2-audit-oldest').textContent=d.sampledObjects===0?'—':
  Math.floor(d.oldestAgeSeconds/86400)+' дн. '+Math.floor(d.oldestAgeSeconds%86400/3600)+' год';
 pill.textContent=result.state==='stale'?'Знімок застарів':'Перевірено';
 const date=new Date(d.auditedAt*1000).toLocaleString('uk-UA');
 status.textContent='Останній огляд: '+date+' · '+Math.floor(d.ageSeconds/3600)+
  ' год тому · перевірено лише transfers/';
 const messages=[];
 if(result.state==='stale')messages.push('Контрольний знімок старший за 8 годин. Перевір Cron Relay.');
 if(d.truncated)messages.push('Список обрізано до 500 об’єктів. Кількість і обсяг НЕ є повним підсумком навіть для transfers/.');
 if(d.overdueObjects>0)messages.push('У вибірці є '+fmt(d.overdueObjects)+
  ' об’єктів старше 8 днів. Перевір R2 Lifecycle. Затримка видалення не обов’язково є збоєм.');
 if(!messages.length)messages.push(
  d.sampledObjects===0?'Тимчасових об’єктів у перевіреному префіксі немає.':
   'У перевіреному списку об’єктів старше 8 днів не виявлено.');
 warning.textContent=messages.join(' ');
}
function showMaintenance(result){
 showR2Audit(result?.data?.r2Audit);
 const pill=q('cleanup-pill'),status=q('cleanup-status');
 const ids=['cleanup-msg','cleanup-nonces','cleanup-receipts','cleanup-rate'];
 if(!result?.data || !['ready','stale'].includes(result.state)){
  pill.textContent='Немає контрольного знімка';
  const reason=result?.reason;
  const descriptions={
   no_hourly_checkpoint_yet:'Службова таблиця доступна, але погодинний контрольний запис ще не сформовано. Перевір Cron Triggers і події MAVKA_RELAY_MAINTENANCE_V1.',
   table_or_database_unavailable:'Не вдалося прочитати контрольну таблицю Relay. Перевір D1 mavka-messages, міграцію 0004 і Workers Logs.',
   rpc_not_deployed:'Внутрішній RPC Relay ще недоступний. Перевір останнє розгортання обох Workers.',
   db_not_bound:'База D1 Relay недоступна. Перевір прив’язку DB до mavka-messages.',
  };
  status.textContent=descriptions[reason]||
   'Немає підтвердженого погодинного звіту. Перевір Cron Triggers та Workers Logs для Relay.';
  for(const id of ids)q(id).textContent='—';
  q('cleanup-security').textContent='Очищення журналу безпеки: даних немає.';
  return;
 }
 const d=result.data,at=new Date(d.checkedAt*1000).toLocaleString('uk-UA');
 const stale=result.state==='stale';
 pill.textContent=stale?'Увага · немає свіжого підтвердження':'Очищення підтверджено';
 status.textContent='Контрольний запуск: '+at+' · '+Math.floor(d.ageSeconds/60)+' хв тому'+
  (stale?' · знімок старіший за 90 хв, перевір Cron Triggers':' · дані актуальні');
 const counts=d.deletionCounts||{};
 for(const [id,key] of [['cleanup-msg','deleted_messages'],['cleanup-nonces','deleted_nonces'],
  ['cleanup-receipts','deleted_receipts'],['cleanup-rate','deleted_rate_windows']])
  setMetric(id,counts[key]);
 q('cleanup-security').textContent='Журнал безпеки: '+(d.securityPruneSucceeded
   ? 'плановий запуск очищення успішний'
   : 'результат очищення не підтверджено — перевір Workers Logs')+
   ' · видалено під час знімка: '+fmt(counts.deleted_security_rollups)+'.';
}
function showDevices(result){
 const status=q('devices-status');
 if(result?.state!=='ready'||!result.data){
  status.textContent='Джерело поки недоступне. Необхідний внутрішній Cloudflare Service Binding; статистика не замінюється нулями.';
  for(const id of ['device-active','device-online','device-pending','device-revoked'])q(id).textContent='—';
  return;
 }
 status.textContent='Внутрішній захищений RPC · стан на '+new Date(result.observedAt*1000).toLocaleString('uk-UA')+
 ' · активність за останні 30 хвилин';
 const d=result.data;
 setMetric('device-active',d.mobileActive);
 setMetric('device-online',d.mobileSeenRecently);
 setMetric('device-pending',d.mobilePending);
 setMetric('device-revoked',d.mobileRevoked);
}
function showSecurity(result){
 showIncidents(result?.risk);
 const status=q('security-status'), warning=q('security-warning');
 const reasons=q('security-reasons'),countries=q('security-countries'),hours=q('security-hours');
 const markUnavailable=()=>{
  q('security-day').textContent='—';q('security-week').textContent='—';
  reasons.textContent='Немає підключеної історії.';countries.textContent='—';hours.replaceChildren();
 };
 if(result?.state!=='ready'||result.coverage!=='best_effort'){
  status.textContent='Захищена телеметрія недоступна. Перевір, чи додано таблицю relay_security_rollups до D1 mavka-messages. Замість відсутніх даних нулі не показуються.';
  warning.textContent='Накопичення історії ще не підтверджено.';
  markUnavailable();return;
 }
 setMetric('security-day',result.total24h);
 setMetric('security-week',result.total7d);
 status.textContent='Внутрішній RPC · зведення на '+new Date(result.observedAt*1000).toLocaleString('uk-UA')+
  ' · приблизний облік, можливі пропущені події';
 const alertReasons=new Set(['auth_signature_invalid','request_replayed','auth_main_binding_invalid']);
 const suspect=result.byReason.filter(x=>alertReasons.has(x.reason)).reduce((n,x)=>n+x.count,0);
 warning.textContent=suspect>0
  ? 'Потребує перевірки: у зведенні є '+fmt(suspect)+' відхилень за підписом, прив’язкою або повтором. Це не доказ атаки.'
  : result.total24h>0?'Є відхилені запити. Перевірте причини нижче; частина подій могла не потрапити до агрегатів.'
  : 'У накопичених агрегатах немає відхилень за 24 год. Це не підтверджує відсутності сторонніх звернень.';
 reasons.replaceChildren();
 const titles={
  auth_headers_invalid:'Неправильні заголовки підпису',
  auth_signature_invalid:'Недійсний цифровий підпис',
  auth_timestamp_invalid:'Недійсний час підпису',
  auth_device_unavailable:'Невідомий або неактивний пристрій',
  country_not_allowed:'Країна поза дозволеною зоною',
  request_replayed:'Повторне використання запиту',
  rate_limited:'Обмеження частоти запитів',
  unauthorized:'Відмова автентифікації',
  native_clients_only:'Стороннє браузерне звернення',
  not_found:'Невідомий маршрут або ресурс',
  request_rejected:'Відхилений запит',
  service_unavailable:'Помилка сервісу',
 };
 for(const item of result.byReason){
  const line=document.createElement('div');line.className='security-line';
  const a=document.createElement('span'),b=document.createElement('b');
  a.textContent=titles[item.reason]||item.reason;
  b.textContent=fmt(item.count);line.append(a,b);reasons.append(line);
 }
 if(!result.byReason.length)reasons.textContent='За вибраний період агрегатів немає.';
 countries.replaceChildren();
 for(const item of result.byCountry){
  const line=document.createElement('div');line.className='security-line';
  const a=document.createElement('span'),b=document.createElement('b');
  a.textContent=item.scope==='UA'?'Україна':item.scope==='non_UA'?'Інші країни':'Країна невідома';
  b.textContent=fmt(item.count);line.append(a,b);countries.append(line);
 }
 if(!result.byCountry.length)countries.textContent='Немає збережених зведень.';
 hours.replaceChildren();
 const labels=new Map(result.byHour.map(x=>[x.hourStart,x.count]));
 const currentHour=Math.floor(result.observedAt/3600)*3600;
 const numbers=Array.from({length:24},(_,i)=>labels.get(currentHour-(23-i)*3600)||0);
 const maximum=Math.max(1,...numbers);
 for(let i=0;i<numbers.length;i++){
  const bar=document.createElement('div');bar.style.height=(numbers[i]/maximum*100)+'%';
  const stamp=new Date((currentHour-(23-i)*3600)*1000).toISOString().slice(0,16);
  bar.title=stamp+' UTC: '+fmt(numbers[i])+' збережених відхилень';
  hours.appendChild(bar);
 }
}
async function refresh(){
 if(loading)return;loading=true;q('refresh').disabled=true;state('Читаємо Cloudflare…');
 try {
  const results=await Promise.allSettled([get('/api/summary'),loadHistory(),loadTrends(),get('/api/security'),get('/api/devices'),get('/api/maintenance')]);
  if(results[0].status==='fulfilled')show(results[0].value);
  else {state('Не вдалося отримати аналітику',true);q('notice').className='notice warn';
    q('notice').textContent='Аналітика тимчасово недоступна. Авторизуйся знову через Cloudflare Access за потреби.'}
  showSecurity(results[3].status==='fulfilled'?results[3].value:null);
  showDevices(results[4].status==='fulfilled'?results[4].value:null);
  showMaintenance(results[5].status==='fulfilled'?results[5].value:null);
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
// Export only bounded, non-sensitive aggregated usage counters from this
// same-origin authenticated session. No Relay messages, IDs, keys or logs.
async function exportHistoryCsv(){
 const button=q('export-history'),status=q('history-export-status');
 if(button.disabled)return;
 button.disabled=true;status.textContent='Читаємо історію…';
 try{
  const data=[];let cursor=null,previous=Infinity,complete=false;
  for(let page=0;page<65;page++){
   const path='/api/history?period='+activePeriod+
    (cursor===null?'':'&before='+encodeURIComponent(cursor));
   const result=await get(path);
   if(result.state!=='ready'||!Array.isArray(result.entries))throw Error('unavailable');
   for(const row of result.entries){
    if(!Number.isFinite(row.bucket_start))throw Error('invalid');
    data.push(row);
   }
   if(result.nextBefore===null){complete=true;break}
   if(!Number.isSafeInteger(result.nextBefore)||result.nextBefore>=previous)
    throw Error('cursor');
   previous=result.nextBefore;cursor=result.nextBefore;
  }
  if(!complete)throw Error('limit');
  // Semicolon delimiter and Excel-compatible UTF-8 BOM; all cells are
  // generated ISO timestamps or safe numeric values, never user-supplied text.
  const cells=(row)=>[
   new Date(row.bucket_start*1000).toISOString().slice(0,16).replace('T',' '),
   row.workers_requests,row.workers_errors,row.d1_rows_read,
   row.d1_rows_written,row.r2_payload_bytes,row.r2_objects
  ].map(v=>Number.isFinite(v)?String(v):typeof v==='string'?v:'');
  const lines=['sep=;',
   'Час (UTC);Relay запити;Relay помилки;D1 читання;D1 запис;R2 байти;R2 об’єкти'];
  for(const row of data)lines.push(cells(row).join(';'));
  const blob=new Blob(['\\uFEFF'+lines.join('\\r\\n')+'\\r\\n'],{type:'text/csv;charset=utf-8;'});
  const url=URL.createObjectURL(blob),link=document.createElement('a');
  link.href=url;link.download='mavka-usage-'+activePeriod+'-'+
    new Date().toISOString().slice(0,10)+'.csv';
  document.body.appendChild(link);link.click();link.remove();
  // Revoke on the next user action rather than during the click dispatch.
  q('refresh').addEventListener('click',()=>URL.revokeObjectURL(url),{once:true});
  status.textContent='CSV сформовано: '+data.length+' записів.';
 }catch{
  status.textContent='Експорт недоступний. Перевір підключення та повтори спробу.';
 }finally{button.disabled=false}
}
q('export-history').addEventListener('click',exportHistoryCsv);
q('more-history').addEventListener('click',async ()=>{
 if(!historyCursor)return;
 const button=q('more-history');button.disabled=true;
 try{await loadHistory(historyCursor,false)}
 finally{button.disabled=false}
});

// Native details permit click/keyboard collapse without requiring JavaScript.
// Persist only a local UI preference; never send it to the Worker.
const historyDetails=q('history-details');
const historyPreferenceKey='mavka-control.history-open.v1';
try{if(localStorage.getItem(historyPreferenceKey)==='false')historyDetails.open=false}
catch{/* blocked localStorage: use default expanded state */}
historyDetails.addEventListener('toggle',()=>{
 try{localStorage.setItem(historyPreferenceKey,String(historyDetails.open))}
 catch{/* optional preference */}
});
function updateHistoryPreview(){
 const last=visibleRows[0]?.bucket_start;
 const stamp=Number.isSafeInteger(last)?
  ' · останній '+new Date(last*1000).toISOString().slice(0,16).replace('T',' ')+' UTC':'';
 q('history-preview').textContent='Завантажено записів: '+visibleRows.length+
  ' · '+activePeriod+stamp;
}
// Fixed right-side local anchor menu; no extra requests or trackers.
const quickNav=q('quick-nav');
const navLinks=[...quickNav.querySelectorAll('a[href^="#"]')];
const targets=navLinks.map(link=>q(link.getAttribute('href').slice(1)));
function markActiveNavigation(){
 let current=targets[0]?.id;
 const cutoff=Math.min(190,window.innerHeight*0.33);
 for(const section of targets)
  if(section&&section.getBoundingClientRect().top<=cutoff)current=section.id;
 for(const link of navLinks){
  const active=link.getAttribute('href')==='#'+current;
  link.classList.toggle('active',active);
  if(active)link.setAttribute('aria-current','location');
  else link.removeAttribute('aria-current');
 }
}
let navFramePending=false;
function scheduleNavHighlight(){
 if(navFramePending)return;
 navFramePending=true;
 requestAnimationFrame(()=>{navFramePending=false;markActiveNavigation()});
}
for(const link of navLinks)
 link.addEventListener('click',()=>{quickNav.open=false;scheduleNavHighlight()});
document.addEventListener('keydown',event=>{
 if(event.key==='Escape'&&quickNav.open){
  quickNav.open=false;quickNav.querySelector('summary').focus();
 }
});
document.addEventListener('pointerdown',event=>{
 if(quickNav.open&&!quickNav.contains(event.target))quickNav.open=false;
});
window.addEventListener('scroll',scheduleNavHighlight,{passive:true});
window.addEventListener('resize',scheduleNavHighlight);
scheduleNavHighlight();

q('refresh').addEventListener('click',refresh);
refresh();setInterval(()=>{if(!document.hidden)refresh()},900000);
})();
</script>
</body></html>`;
