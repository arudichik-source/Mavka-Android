-- Additive: run on production D1 before enabling the R2 file routes.
-- Files remain encrypted in R2; this table stores only routing and receipt metadata.
CREATE TABLE IF NOT EXISTS relay_file_parts (
  recipient_device_id TEXT NOT NULL REFERENCES devices(device_id),
  sender_device_id TEXT NOT NULL REFERENCES devices(device_id),
  transfer_id TEXT NOT NULL,
  part_index INTEGER NOT NULL CHECK(part_index >= 0 AND part_index < 4096),
  part_count INTEGER NOT NULL CHECK(part_count BETWEEN 1 AND 4096),
  payload_sha256 TEXT NOT NULL,
  size_bytes INTEGER NOT NULL CHECK(size_bytes > 0 AND size_bytes <= 49152),
  expires_at INTEGER NOT NULL,
  created_at INTEGER NOT NULL,
  acked_at INTEGER,
  PRIMARY KEY (recipient_device_id, sender_device_id, transfer_id, part_index),
  CHECK(part_index < part_count),
  CHECK(expires_at > created_at)
);
CREATE INDEX IF NOT EXISTS idx_relay_file_expiry ON relay_file_parts(expires_at);
CREATE INDEX IF NOT EXISTS idx_relay_file_sender_pending ON relay_file_parts(sender_device_id, expires_at) WHERE acked_at IS NULL;
