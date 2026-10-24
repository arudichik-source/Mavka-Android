import test from 'node:test';
import assert from 'node:assert/strict';
import { Script } from 'node:vm';
import { html } from '../src/ui.js';
import { parseBefore, parsePeriod, readHistoryPage, readTrends } from '../src/history.js';

const NOW = new Date('2026-10-24T10:30:00.000Z');
const END = Math.floor(NOW.getTime()/1000);
const makeRow = timestamp => ({
  bucket_start: timestamp, workers_requests: 7, workers_errors: 0,
  d1_rows_read: 30, d1_rows_written: 2,
  r2_payload_bytes: null, r2_objects: 0,
});
function makeDB(rows) {
  const calls=[];
  const db = {
    prepare(sql) {
      return {
        bind(...bindings){
          calls.push({sql,bindings});
          return { async all() {
            const timestamps = rows.filter(s=>s.bucket_start>=bindings[0]);
            if(sql.includes('GROUP BY CAST')){
              const buckets = new Map();
              for(const row of timestamps) if(row.bucket_start<=bindings[1]){
                const k = Math.floor(row.bucket_start/bindings[2]);
                if(!buckets.has(k)||buckets.get(k).bucket_start<row.bucket_start)buckets.set(k,row);
              }
              return {results:[...buckets.values()].sort((a,b)=>a.bucket_start-b.bucket_start).slice(0,240)};
            }
            return {results:timestamps.filter(s=>s.bucket_start<bindings[1])
              .sort((a,b)=>b.bucket_start-a.bucket_start).slice(0,bindings[2])};
          }};
        },
      };
    },
  };
  return {db,calls};
}
test('strict time period and cursor parsing excludes unknown fields/SQL-like input',()=>{
  assert.equal(parsePeriod('24h'),'24h');assert.equal(parsePeriod('7d'),'7d');
  assert.equal(parsePeriod('30d'),'30d');
  assert.equal(parsePeriod('__proto__'),null);
  assert.equal(parsePeriod('30d OR 1=1'),null);
  assert.equal(parseBefore(null),null);assert.equal(parseBefore('1800000000'),1800000000);
  assert.equal(parseBefore('0 OR 1=1'),undefined);
  assert.equal(parseBefore(''),undefined);
});
test('30-day paginated history returns all underlying samples over multiple pages',async()=>{
  const rows=Array.from({length:130},(_,i)=>makeRow(END-900*(i+1)));
  const {db,calls}=makeDB(rows);
  const page1=await readHistoryPage(db,'30d',null,NOW);
  assert.equal(page1.state,'ready');assert.equal(page1.entries.length,50);
  assert.ok(page1.nextBefore);assert.equal(page1.entries[0].bucket_start,rows[0].bucket_start);
  const page2=await readHistoryPage(db,'30d',page1.nextBefore,NOW);
  assert.equal(page2.entries.length,50);
  assert.notEqual(page2.entries[0].bucket_start,page1.entries.at(-1).bucket_start);
  const page3=await readHistoryPage(db,'30d',page2.nextBefore,NOW);
  assert.equal(page3.entries.length,30);
  assert.equal(page3.nextBefore,null);
  assert.ok(calls.every(c=>c.sql.includes('usage_snapshots')));
});
test('trends query selects existing per-hour/day snapshots, does not fabricate missing rows',async()=>{
  const rows=[makeRow(END-250000),makeRow(END-95000),
    makeRow(END-81000),makeRow(END-80000),
    makeRow(END-3400),makeRow(END-1000)];
  rows[2].d1_rows_read=null;
  const {db,calls}=makeDB(rows);
  const d=await readTrends(db,'7d',NOW);
  assert.equal(d.state,'ready');
  assert.equal(d.intervalSeconds,3600);
  assert.ok(d.entries.some(x=>x.d1_rows_read===null));
  assert.ok(d.entries.length<=rows.length);
  assert.ok(calls[0].sql.includes('MAX(bucket_start)'));
  const m=await readTrends(db,'30d',NOW);
  assert.equal(m.intervalSeconds,86400);
  assert.ok(m.entries.length<=rows.length);
  assert.equal(m.dailyCountersResetAtUtcMidnight,true);
});
test('D1 history is fail-closed, bounded and never returns fake zero samples',async()=>{
  assert.equal((await readTrends(null,'7d',NOW)).state,'not_bound');
  assert.equal((await readHistoryPage(null,'30d',null,NOW)).state,'not_bound');
  assert.equal((await readTrends({prepare(){throw Error('db down')}},'30d',NOW)).state,'database_unavailable');
  assert.equal((await readHistoryPage(null,'wrong',null,NOW)).state,'invalid_query');
  const {db}=makeDB([]);
  assert.deepEqual((await readHistoryPage(db,'30d',null,NOW)).entries,[]);
});

test('dashboard client JavaScript parses and 0.2 history elements exist',()=>{
  const match=html.match(/<script nonce="__CSP_NONCE__">([\\s\\S]*?)<\\/script>/);
  assert.ok(match, 'No self-contained client script');
  assert.doesNotThrow(()=>new Script(match[1]));
  for(const id of ['chart','period-24h','period-7d','period-30d','trend-metric',
    'more-history','security-status','chart-coverage']){
    assert.ok(html.includes('id="'+id+'"'),id);
  }
  assert.ok(html.includes('MaVka Server Control 0.2.0'));
});
