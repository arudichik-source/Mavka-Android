-- OPTIONAL additive migration for hourly singleton relay_maintenance_status.
-- Apply ONLY to mavka-messages after D1 Time Travel bookmark/SQL backup.
-- Run each statement ONCE. Existing hourly cleanup row remains unchanged.
-- Empty values are NULL until a successful bounded R2 audit. No object keys.
ALTER TABLE relay_maintenance_status ADD COLUMN r2_audit_at INTEGER CHECK(r2_audit_at >= 0);
ALTER TABLE relay_maintenance_status ADD COLUMN r2_audit_state TEXT CHECK(r2_audit_state IN ('ready','failed','missing_binding'));
ALTER TABLE relay_maintenance_status ADD COLUMN r2_sample_count INTEGER CHECK(r2_sample_count >= 0);
ALTER TABLE relay_maintenance_status ADD COLUMN r2_sample_bytes INTEGER CHECK(r2_sample_bytes >= 0);
ALTER TABLE relay_maintenance_status ADD COLUMN r2_overdue_count INTEGER CHECK(r2_overdue_count >= 0);
ALTER TABLE relay_maintenance_status ADD COLUMN r2_oldest_age_seconds INTEGER CHECK(r2_oldest_age_seconds >= 0);
ALTER TABLE relay_maintenance_status ADD COLUMN r2_sample_truncated INTEGER CHECK(r2_sample_truncated IN (0,1));
