-- Apply ONLY to a new, separate D1 database: mavka-control.
-- No personal data, IP, email addresses, message contents, signatures or keys.
-- 96 snapshots/day maximum (unique quarter-hour key).
CREATE TABLE IF NOT EXISTS usage_snapshots (
  bucket_start INTEGER PRIMARY KEY NOT NULL,
  workers_requests INTEGER CHECK(workers_requests >= 0),
  workers_errors INTEGER CHECK(workers_errors >= 0),
  d1_rows_read INTEGER CHECK(d1_rows_read >= 0),
  d1_rows_written INTEGER CHECK(d1_rows_written >= 0),
  r2_payload_bytes INTEGER CHECK(r2_payload_bytes >= 0),
  r2_objects INTEGER CHECK(r2_objects >= 0)
);
