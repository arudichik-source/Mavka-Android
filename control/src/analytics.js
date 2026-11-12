// Read-only Cloudflare Analytics; never make requests from a browser with API tokens.
// D1 totals are account-wide; Workers track Relay and account total separately; R2 is one bucket.
// Free quotas are guides, not billing balance or a guarantee against overages.
export const LIMITS = Object.freeze({
  workersRequestsPerDay: 100000,
  d1RowsReadPerDay: 5000000,
  d1RowsWrittenPerDay: 100000,
  d1StorageBytesAccount: 5 * 1000 ** 3,
  d1StorageBytesPerDatabase: 500 * 1000 ** 2,
  r2StandardGbMonths: 10,
  // Owner-defined dashboard threshold, NOT a provider hard quota or GB-month usage.
  r2MonitoringBytes: 5_000_000_000,
  r2ClassAOperationsPerMonth: 1000000,
  r2ClassBOperationsPerMonth: 10000000,
  workersBuildMinutesPerMonth: 3000,
  workersScriptsPerAccount: 100,
  workersCronTriggersPerAccount: 5,
  d1DatabasesPerAccount: 10,
});
const WORKERS = `query RelayWorkers($account: string!, $start: string!, $end: string!) {
  viewer { accounts(filter: { accountTag: $account }) {
    workersInvocationsAdaptive(
      limit: 1
      filter: { scriptName: "mavka-relay", datetime_geq: $start, datetime_lt: $end }
    ) { sum { requests errors } }
    accountTotal: workersInvocationsAdaptive(
      limit: 1
      filter: { datetime_geq: $start, datetime_lt: $end }
    ) { sum { requests errors } }
  } }
}`;
const D1 = `query D1Daily($account: string!, $today: Date) {
  viewer { accounts(filter: { accountTag: $account }) {
    d1AnalyticsAdaptiveGroups(limit: 1000,
      filter: { date_geq: $today, date_leq: $today }
    ) { sum { rowsRead rowsWritten } }
  } }
}`;
const R2_STORAGE = `query R2Storage($account: string!, $start: Time!, $end: Time!) {
  viewer { accounts(filter: { accountTag: $account }) {
    r2StorageAdaptiveGroups(limit: 1,
      filter: { datetime_geq: $start, datetime_leq: $end,
        bucketName: "mavka-private-files" }, orderBy: [datetime_DESC]
    ) { max { payloadSize metadataSize objectCount } dimensions { datetime } }
  } }
}`;
const R2_OPS = `query R2Operations($account: string!, $start: Time!, $end: Time!) {
  viewer { accounts(filter: { accountTag: $account }) {
    r2OperationsAdaptiveGroups(limit: 1000,
      filter: { datetime_geq: $start, datetime_leq: $end,
        bucketName: "mavka-private-files" }
    ) { sum { requests } dimensions { actionType } }
    accountOperations: r2OperationsAdaptiveGroups(limit: 1000,
      filter: { datetime_geq: $start, datetime_leq: $end }
    ) { sum { requests } dimensions { actionType } }
  } }
}`;
const D1_STORAGE = `query D1Storage($account: string!, $start: Time!, $end: Time!) {
  viewer { accounts(filter: { accountTag: $account }) {
    d1StorageAdaptiveGroups(limit: 1000,
      filter: { datetime_geq: $start, datetime_leq: $end },
      orderBy: [datetime_DESC]
    ) { max { databaseSizeBytes } dimensions { databaseId datetime } }
  } }
}`;
const R2_CLASS_A = new Set(['ListBuckets','PutBucket','ListObjects','PutObject','CopyObject',
  'CompleteMultipartUpload','CreateMultipartUpload','LifecycleStorageTierTransition',
  'ListMultipartUploads','UploadPart','UploadPartCopy','ListParts','PutBucketEncryption',
  'PutBucketCors','PutBucketLifecycleConfiguration']);
const R2_CLASS_B = new Set(['HeadBucket','HeadObject','GetObject','UsageSummary',
  'GetBucketEncryption','GetBucketLocation','GetBucketCors','GetBucketLifecycleConfiguration']);
const R2_FREE = new Set(['DeleteObject','DeleteBucket','AbortMultipartUpload']);
// Unrecognized action types are NOT silently treated as billable class A/B.
export function classifyR2Operations(rows) {
  if (!Array.isArray(rows) || rows.length>=1000) return null;
  let a=0,b=0,free=0,unknown=0,total=0;
  for (const row of rows) {
    const action=row?.dimensions?.actionType, count=number(row?.sum?.requests);
    if (count===null) return null;
    total+=count;
    if (R2_CLASS_A.has(action)) a+=count;
    else if (R2_CLASS_B.has(action)) b+=count;
    else if (R2_FREE.has(action)) free+=count;
    else unknown+=count;
  }
  return {classA:unknown===0?a:null,classB:unknown===0?b:null,
    freeOperations:free,unknownOperations:unknown,observedRequests:total,
    classificationComplete:unknown===0,scope:'account',period:'current_utc_month'};
}
export async function readD1Storage(env, now=new Date()) {
  if (!analyticsConfigured(env)) return {state:'not_configured',bytes:null};
  const end=now.toISOString(),start=new Date(now.getTime()-86400000).toISOString();
  try {
    const account=await query(env,'d1Storage',D1_STORAGE,
      {account:env.CF_ACCOUNT_ID,start,end});
    const rows=account?.d1StorageAdaptiveGroups;
    if (!Array.isArray(rows)||!rows.length||rows.length>=1000)
      return {state:'no_reliable_data',bytes:null};
    const latest=new Map();
    for(const row of rows){
      const id=row?.dimensions?.databaseId, value=number(row?.max?.databaseSizeBytes);
      if(typeof id!=='string'||!id||value===null)continue;
      if(!latest.has(id))latest.set(id,value);
    }
    if(!latest.size)return {state:'no_reliable_data',bytes:null};
    const sizes=[...latest.values()];
    return {state:'ready',bytes:sizes.reduce((a,b)=>a+b,0),databaseCount:latest.size,
      largestDatabaseBytes:Math.max(...sizes),window:'last_24h_latest_samples'};
  }catch(e){return {state:safeErrorKind(e),bytes:null};}
}
export function analyticsConfigured(env) {
  return typeof env.CF_ACCOUNT_ID === 'string' &&
    /^[a-f0-9]{32}$/i.test(env.CF_ACCOUNT_ID) &&
    typeof env.CF_ANALYTICS_TOKEN === 'string' && env.CF_ANALYTICS_TOKEN.length >= 20;
}
export function safeErrorKind(error) {
  const message = String(error?.message ?? '');
  // Avoid exposing untrusted exception text, arbitrary hostnames, tokens,
  // account information, response bodies, or stack traces.
  if (['query_invalid', 'permission', 'rate_limit', 'http_failure',
    'graphql_error', 'no_account_data', 'timeout', 'fetch_or_runtime_type_error',
    'runtime_reference_error', 'other_runtime_error'].includes(message)) return message;
  if (error?.name === 'TimeoutError' || error?.name === 'AbortError') return 'timeout';
  if (error?.name === 'TypeError') return 'fetch_or_runtime_type_error';
  if (error?.name === 'ReferenceError') return 'runtime_reference_error';
  return 'other_runtime_error';
}

async function query(env, name, graphql, variables) {
  let response;
  try {
    response = await fetch('https://api.cloudflare.com/client/v4/graphql', {
      // workerd does NOT support redirect:'error'; it throws TypeError before
      // contacting Cloudflare. Manual mode never forwards the bearer secret
      // to a redirect target. Treat 3xx as an HTTP failure below.
      method: 'POST', redirect: 'manual',
      headers: { Authorization: 'Bearer ' + env.CF_ANALYTICS_TOKEN,
        'Accept': 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify({ query: graphql, variables }),
      signal: AbortSignal.timeout(8000),
    });
  } catch (error) {
    throw new Error(safeErrorKind(error));
  }
  // Only bounded, non-sensitive diagnostics may be returned to an administrator.
  // NEVER disclose raw GraphQL errors, HTTP bodies, account IDs or bearer tokens.
  if (!response.ok) {
    const kind = response.status === 401 || response.status === 403 ? 'permission'
      : response.status === 429 ? 'rate_limit' : 'http_failure';
    throw new Error(kind);
  }
  const result = await response.json();
  if (result.errors?.length) {
    const message = result.errors.map(e => typeof e?.message === 'string' ? e.message : '').join(' ');
    const kind = /unknown type|type mismatch|not defined|variable.*type|expected type|cannot query field|unknown argument|validation/i.test(message)
      ? 'query_invalid'
      : /authorization|authenticate|permission|forbidden|not authorized|access denied/i.test(message)
        ? 'permission' : 'graphql_error';
    throw new Error(kind);
  }
  if (!result.data?.viewer?.accounts?.[0]) throw new Error('no_account_data');
  return result.data.viewer.accounts[0];
}
const number = v => typeof v === 'number' && Number.isFinite(v) && v >= 0 ? v : null;
const sum = (items, key) => items?.reduce((n, item) => n + (number(item?.sum?.[key]) ?? 0), 0) ?? null;
const view = result => result.status === 'fulfilled' ? result.value : null;

export async function readAnalytics(env, now = new Date()) {
  const at = now.toISOString(), today = at.slice(0, 10);
  if (!analyticsConfigured(env)) return { state: 'unconfigured', at, limits: LIMITS,
    workers: null, d1: null, r2: null, errors: ['cloudflare_analytics_not_configured'] };
  const start = today + 'T00:00:00Z';
  const month = today.slice(0, 7) + '-01T00:00:00Z';
  const yesterday = new Date(now.getTime() - 86400000).toISOString();
  const variables = { account: env.CF_ACCOUNT_ID, start, end: at };
  const results = await Promise.allSettled([
    query(env, 'workers', WORKERS, variables),
    query(env, 'd1', D1, { account: env.CF_ACCOUNT_ID, today }),
    query(env, 'r2Storage', R2_STORAGE, { account: env.CF_ACCOUNT_ID, start: yesterday, end: at }),
    query(env, 'r2Operations', R2_OPS, { account: env.CF_ACCOUNT_ID, start: month, end: at }),
  ]);
  const w = view(results[0]), d = view(results[1]), storage = view(results[2]), ops = view(results[3]);
  const wr = w?.workersInvocationsAdaptive;
  const wa = w?.accountTotal;
  const dr = d?.d1AnalyticsAdaptiveGroups;
  const st = storage?.r2StorageAdaptiveGroups?.[0];
  const orows = ops?.r2OperationsAdaptiveGroups;
  const accountR2 = classifyR2Operations(ops?.accountOperations);
  const errors = ['workers', 'd1', 'r2Storage', 'r2Operations']
    .filter((name, i) => results[i].status !== 'fulfilled')
    .map(name => name + '_unavailable');
  const diagnostics = Object.fromEntries(['workers', 'd1', 'r2Storage', 'r2Operations']
    .map((name, i) => [name, results[i].status === 'fulfilled'
      ? 'ok' : safeErrorKind(results[i].reason)]));
  return {
    state: errors.length === 4 ? 'unavailable' : errors.length ? 'partial' : 'ready',
    at, utcDay: today, limits: LIMITS,
    workers: w ? { scope: 'mavka-relay_only', requestsToday: sum(wr, 'requests'),
      errorsToday: sum(wr, 'errors'),
      accountRequestsToday: Array.isArray(wa) ? sum(wa, 'requests') : null,
      accountErrorsToday: Array.isArray(wa) ? sum(wa, 'errors') : null } : null,
    d1: d ? { scope: 'account', rowsReadToday: sum(dr, 'rowsRead'),
      rowsWrittenToday: sum(dr, 'rowsWritten') } : null,
    r2: { bucket: 'mavka-private-files',
      storage: storage ? { bytes: number(st?.max?.payloadSize),
        metadataBytes: number(st?.max?.metadataSize), objects: number(st?.max?.objectCount),
        observedAt: st?.dimensions?.datetime ?? null,
        billingGbMonths: null } : null,
      operations: ops ? { requestsThisMonth: sum(orows, 'requests'),
        actionTypes: (orows ?? []).slice(0, 30).map(o => ({
          action: String(o.dimensions?.actionType ?? 'unknown').slice(0, 80),
          requests: number(o.sum?.requests) ?? 0 })),
        classA: classifyR2Operations(orows)?.classA ?? null,
        classB: classifyR2Operations(orows)?.classB ?? null,
        account: accountR2 } : null },
    errors, diagnostics,
    caveats: ['Relay Workers count is one Worker; account-wide total is a separate aggregate.',
      'R2 current bytes are not GB-months; class A/B and billing costs are unverified.',
      'Analytics may be delayed or sampled; the actual invoice is in Cloudflare Billing.'],
  };
}
