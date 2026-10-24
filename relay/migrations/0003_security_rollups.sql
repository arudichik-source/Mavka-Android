-- Optional ADDITIVE security aggregates. Apply only to mavka-messages
-- after checking D1 Time Travel / a backup. No existing rows are altered.
-- Store only strict, allowlisted security dimensions; no client IDs, IPs,
-- headers, paths, tokens, signatures, or message/document payloads.
CREATE TABLE IF NOT EXISTS relay_security_rollups (
  bucket_start INTEGER NOT NULL CHECK(bucket_start >= 0),
  category TEXT NOT NULL,
  reason TEXT NOT NULL,
  country_scope TEXT NOT NULL CHECK(country_scope IN ('UA','non_UA','unknown')),
  route TEXT NOT NULL,
  status INTEGER NOT NULL CHECK(status BETWEEN 400 AND 599),
  principal_scope TEXT NOT NULL CHECK(principal_scope IN ('verified_main','verified_mobile','unverified')),
  events INTEGER NOT NULL DEFAULT 0 CHECK(events >= 0),
  PRIMARY KEY(bucket_start, category, reason, country_scope, route, status, principal_scope)
);
CREATE INDEX IF NOT EXISTS idx_relay_security_rollup_time
  ON relay_security_rollups(bucket_start);
