import test from 'node:test';
import assert from 'node:assert/strict';
import { Script } from 'node:vm';
import { html } from '../src/ui.js';

const SCRIPT_MARKER='<script nonce="__CSP_NONCE__">';
const script=html.slice(html.indexOf(SCRIPT_MARKER)+SCRIPT_MARKER.length,html.indexOf('</script>'));

test('three independently collapsible telemetry panels retain real content and brief previews',()=>{
 const expectations=[
  ['trends',['period-24h','period-7d','period-30d','trend-metric','chart','chart-coverage']],
  ['security',['security-status','security-day','security-week','security-reasons','security-countries','security-hours']],
  ['devices',['devices-status','device-active','device-online','device-pending','device-revoked']]
 ];
 for(const [name,ids] of expectations){
  const opening='<details class="panel-disclosure" id="'+name+'-details" open>';
  const start=html.indexOf(opening);
  assert.ok(start>=0,'missing '+name+' disclosure');
  const end=html.indexOf('</details>',start);
  assert.ok(end>start,'missing '+name+' disclosure closing');
  const panel=html.slice(start,end);
  assert.ok(panel.includes('<summary aria-controls="'+name+'-content">'),'accessible summary for '+name);
  assert.ok(panel.includes('id="'+name+'-preview"'),'collapsed summary for '+name);
  assert.ok(panel.includes('id="'+name+'-content"'),'detail content for '+name);
  assert.ok(panel.includes('class="disclosure-toggle" aria-hidden="true"'));
  for(const id of ids)assert.ok(panel.includes('id="'+id+'"'),'content lost: '+id);
 }
 assert.match(html,/\.panel-disclosure:not\(\[open\]\) \.disclosure-toggle::after/);
 assert.match(html,/:focus-visible/);
});

test('existing history and in-page navigation remain intact and all element ids are unique',()=>{
 assert.ok(html.includes('id="history-details" open'));
 assert.ok(html.includes('id="history-content"'));
 assert.ok(html.includes('id="export-history"'));
 for(const id of ['trends','security','devices']){
  assert.ok(html.includes('href="#'+id+'"'));
  assert.ok(html.includes('id="'+id+'"'));
 }
 const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);
 assert.deepEqual([...new Set(ids)].sort(),ids.slice().sort(),'HTML element IDs must be unique');
 assert.equal((html.match(/<details\b/g)||[]).length,(html.match(/<\/details>/g)||[]).length);
 assert.equal((html.match(/<summary\b/g)||[]).length,(html.match(/<\/summary>/g)||[]).length);
});

test('collapse preferences stay independent and browser-only; blocked storage is tolerated',()=>{
 assert.ok(script.includes("'mavka-control.history-open.v1'"));
 for(const name of ['trends','security','devices'])assert.ok(script.includes("'"+name+"'"));
 assert.ok(script.includes("'mavka-control.disclosure.'+name+'.v1'"));
 assert.match(script,/localStorage\.getItem\(preferenceKey\)/);
 assert.match(script,/localStorage\.setItem\(preferenceKey,String\(details\.open\)\)/);
 assert.match(script,/details\.addEventListener\('toggle'/);
 assert.ok(script.includes("catch{/* Local storage may be disabled. */}"));
 const start=script.indexOf('// Independent native disclosures');
 const end=script.indexOf('// Fixed right-side local anchor menu;',start);
 const section=script.slice(start,end);
 assert.ok(start>=0&&end>start);
 assert.doesNotMatch(section,/fetch\(|get\(|sendBeacon|document\.cookie/);
 assert.ok(script.includes("updateDisclosurePreviews();"));
});

test('floating back-to-top control respects mobile layout, reduced motion and hidden state',()=>{
 assert.match(html,/<button id="back-to-top"[^>]*type="button"[^>]*aria-label="Повернутися нагору"[^>]*hidden>/);
 assert.match(html,/\.back-to-top\[hidden\]\{display:none\}/);
 assert.match(html,/\.back-to-top\{right:12px;bottom:72px\}/);
 assert.ok(script.includes("backToTop.hidden=window.scrollY<360"));
 assert.ok(script.includes("window.matchMedia('(prefers-reduced-motion: reduce)').matches"));
 assert.ok(script.includes("window.scrollTo({top:0,behavior:reduced?'auto':'smooth'})"));
 assert.ok(script.includes("window.addEventListener('scroll',updateBackToTop,{passive:true})"));
});

test('all script syntax and pre-existing read-only refresh routes stay unchanged',()=>{
 assert.doesNotThrow(()=>new Script(script));
 assert.ok(script.includes("let query='/api/history?period='"));
 assert.ok(script.includes("get('/api/trends?period='"));
 assert.ok(script.includes("get('/api/security')"));
 assert.ok(script.includes("get('/api/devices')"));
 assert.ok(script.includes("get('/api/maintenance')"));
 assert.ok(script.includes('900000'));
 assert.ok(html.includes('MaVka Server Control 0.9.1'));
});
