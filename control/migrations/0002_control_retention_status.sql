-- OPTIONAL one-time migration for the separate mavka-control database ONLY.
-- Take a D1 Time Travel bookmark/export before applying; never use on mavka-messages.
-- One fixed-size singleton row: no identities, messages, tokens, or audit log data.
CREATE TABLE IF NOT EXISTS control_retention_status (
  id INTEGER PRIMARY KEY CHECK(id = 1),
  last_attempt_at INTEGER NOT NULL CHECK(last_attempt_at > 0),
  last_attempt_ok INTEGER NOT NULL CHECK(last_attempt_ok IN (0, 1)),
  last_success_at INTEGER CHECK(last_success_at > 0),
  last_deleted INTEGER CHECK(last_deleted >= 0)
);
