-- Additive optional ONE-ROW maintenance health marker, Relay D1 only.
-- Apply ONLY to mavka-messages after Time Travel bookmark / SQL backup.
-- No messages, device IDs, keys or file content are copied here.
CREATE TABLE IF NOT EXISTS relay_maintenance_status (
 id INTEGER PRIMARY KEY CHECK(id = 1),
 last_checked_at INTEGER NOT NULL CHECK(last_checked_at >= 0),
 deleted_messages INTEGER CHECK(deleted_messages >= 0),
 deleted_nonces INTEGER CHECK(deleted_nonces >= 0),
 deleted_rate_windows INTEGER CHECK(deleted_rate_windows >= 0),
 deleted_receipts INTEGER CHECK(deleted_receipts >= 0),
 deleted_security_rollups INTEGER CHECK(deleted_security_rollups >= 0),
 security_prune_ok INTEGER NOT NULL CHECK(security_prune_ok IN (0,1))
);
