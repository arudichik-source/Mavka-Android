-- Compatible with the devices/messages tables created on 2026-10-18.
-- CREATE IF NOT EXISTS never changes existing table definitions or data.
CREATE TABLE IF NOT EXISTS devices (
  device_id TEXT PRIMARY KEY,
  device_type TEXT NOT NULL CHECK (device_type IN ('main', 'mobile')),
  public_signing_key TEXT NOT NULL,
  public_encryption_key TEXT,
  status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'active', 'revoked')),
  created_at INTEGER NOT NULL DEFAULT (unixepoch()),
  last_seen_at INTEGER,
  revoked_at INTEGER
);
CREATE TABLE IF NOT EXISTS messages (
  message_id TEXT PRIMARY KEY,
  sender_device_id TEXT NOT NULL REFERENCES devices(device_id),
  recipient_device_id TEXT NOT NULL REFERENCES devices(device_id),
  encrypted_payload TEXT NOT NULL,
  created_at INTEGER NOT NULL DEFAULT (unixepoch()),
  expires_at INTEGER NOT NULL,
  CHECK (expires_at > created_at)
);
CREATE INDEX IF NOT EXISTS idx_messages_recipient ON messages(recipient_device_id, created_at);
CREATE INDEX IF NOT EXISTS idx_messages_expiry ON messages(expires_at);
CREATE INDEX IF NOT EXISTS idx_relay_messages_sender ON messages(sender_device_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_relay_single_main ON devices(device_type) WHERE device_type = 'main';
CREATE TABLE IF NOT EXISTS relay_nonces (
  device_id TEXT NOT NULL,
  nonce TEXT NOT NULL,
  expires_at INTEGER NOT NULL,
  PRIMARY KEY (device_id, nonce)
);
CREATE INDEX IF NOT EXISTS idx_relay_nonce_expiry ON relay_nonces(expires_at);
CREATE TABLE IF NOT EXISTS relay_rate_windows (
  device_id TEXT NOT NULL,
  window_start INTEGER NOT NULL,
  requests INTEGER NOT NULL,
  PRIMARY KEY (device_id, window_start)
);
-- Delivery receipts carry only routing IDs and a digest; never ciphertext.
-- They also prevent a fresh HTTP retry from resurrecting an ACKed message.
CREATE TABLE IF NOT EXISTS relay_message_ids (
  message_id TEXT PRIMARY KEY,
  sender_device_id TEXT NOT NULL REFERENCES devices(device_id),
  recipient_device_id TEXT NOT NULL REFERENCES devices(device_id),
  payload_sha256 TEXT NOT NULL,
  message_expires_at INTEGER NOT NULL,
  retain_until INTEGER NOT NULL,
  acked_at INTEGER
);
CREATE INDEX IF NOT EXISTS idx_relay_id_expiry ON relay_message_ids(retain_until);
