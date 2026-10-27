import test from 'node:test';
import assert from 'node:assert/strict';
import { Script } from 'node:vm';
import { html } from '../src/ui.js';

const SECTION_IDS=[
 'overview','resources','source-health','forecast','trends','history','risks',
 'maintenance','r2-lifecycle','security','devices'
];

test('a11y: history native disclosure retains status, summary and export on expand',()=>{
 const start=html.indexOf('<section class="panel section jump-target" id="history">');
 const end=html.indexOf('</section>',start);
 assert.ok(start>=0&&end>start);
 const panel=html.slice(start,end);
 assert.match(panel,/<details class="history-details" id="history-details" open>/);
 assert.match(panel,/<summary aria-controls="history-content">/);
 assert.match(panel,/id="history-state"/);
 assert.match(panel,/id="history-preview"/);
 assert.match(panel,/id="history-content"/);
 assert.match(panel,/id="export-history"/);
 assert.match(panel,/id="more-history"/);
 assert.match(panel,/id="rows"/);
 assert.equal((panel.match(/<summary/g)||[]).length,1);
 assert.equal((panel.match(/<\/details>/g)||[]).length,1);
});
test('eleven quick-nav links all point to existing unique named targets',()=>{
 assert.match(html,/<nav class="quick-nav" aria-label="Швидкий перехід до розділів">/);
 assert.match(html,/<details id="quick-nav"><summary>Розділи<\/summary>/);
 for(const id of SECTION_IDS){
  const href='href="#'+id+'"',target='id="'+id+'"';
  assert.ok(html.includes(href),'No nav link to '+id);
  assert.equal(html.split(href).length-1,1,'duplicate link to '+id);
  assert.equal(html.split(target).length-1,1,'missing/duplicate target '+id);
 }
 assert.match(html,/#r2-lifecycle/);
 assert.match(html,/@media\(max-width:750px\)/);
 assert.match(html,/@media\(prefers-reduced-motion:reduce\)/);
 assert.match(html,/:focus-visible/);
 assert.match(html,/aria-current','location'/);
});
test('history state persistence and quick-nav support failure-safe keyboard access',()=>{
 assert.match(html,/historyDetails\.addEventListener\('toggle'/);
 assert.match(html,/localStorage\.getItem\(historyPreferenceKey\)/);
 assert.match(html,/localStorage\.setItem\(historyPreferenceKey,String\(historyDetails\.open\)\)/);
 assert.match(html,/try\{if\(localStorage/);
 assert.match(html,/event\.key==='Escape'/);
 assert.match(html,/quickNav\.querySelector\('summary'\)\.focus\(\)/);
 assert.match(html,/quickNav\.contains\(event\.target\)/);
 assert.match(html,/addEventListener\('scroll',scheduleNavHighlight,\{passive:true\}\)/);
 assert.match(html,/updateHistoryPreview\(\)/);
});
test('client logic is syntactically valid and still uses existing network reads',()=>{
 const marker='<script nonce="__CSP_NONCE__">';
 const start=html.indexOf(marker),end=html.indexOf('</script>',start);
 assert.ok(start>=0&&end>start);
 const code=html.slice(start+marker.length,end);
 assert.doesNotThrow(()=>new Script(code));
 assert.ok(code.includes("let query='/api/history?period='"));
 assert.match(code,/get\('\/api\/trends\?period='/);
 assert.match(code,/900000/);
 // Local navigation never fetches more backend data.
 const navCode=code.slice(code.indexOf('// Fixed right-side local anchor menu;'));
 assert.ok(!navCode.includes("get('/api/"), 'navigation added an API call');
 assert.ok(!navCode.includes('document.cookie'));
 assert.ok(!navCode.includes('sendBeacon'));
 assert.ok(html.includes('MaVka Server Control 0.8.1'));
});
