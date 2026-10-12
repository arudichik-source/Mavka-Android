#!/usr/bin/env python3
"""Check the actual SQL statements in the Android SQLite migrations.

Uses only Python's sqlite3 on a disposable in-memory fixture. This is not
a substitute for an Android device or for an encrypted Keystore read-back.
"""
from pathlib import Path
import re
import sqlite3

ROOT = Path(__file__).resolve().parents[1]
LEDGER = (ROOT / "src/Mavka.Mobile/PropertyLedgerStore.cs").read_text(encoding="utf-8")
ARCHIVE = (ROOT / "src/Mavka.Mobile/DocumentArchiveStore.cs").read_text(encoding="utf-8")


def section(source: str, opening: str, following: str) -> str:
    return source.split(opening, 1)[1].split(following, 1)[0]


def sql_statements(source: str):
    pattern = re.compile(r'\bdb\.ExecSQL\(\s*(?:@"(?P<raw>.*?)"|"(?P<plain>.*?)")\s*\)', re.S)
    return [m.group("raw") if m.group("raw") is not None else m.group("plain")
            for m in pattern.finditer(source)]


create_events = sql_statements(section(
    LEDGER, "private static void CreateEventTable(SQLiteDatabase db)",
    "private static void AddEvent("))
ledger_upgrade = sql_statements(section(
    LEDGER, "public override void OnUpgrade(SQLiteDatabase db, int oldVersion, int newVersion)",
    "private static void CreateEventTable("))
archive_upgrade = sql_statements(section(
    ARCHIVE, "public override void OnUpgrade(SQLiteDatabase db, int oldVersion, int newVersion)",
    "private static void CreateMailProcessingTable("))
create_status = sql_statements(section(
    ARCHIVE, "private static void CreateStatusTable(SQLiteDatabase db)",
    "private void UpdateStatus("))
create_mail_index = sql_statements(section(
    ARCHIVE, "private static void CreateMailProcessingTable(SQLiteDatabase db)",
    "private static void CreateStatusTable("))

assert len(create_events) == 3, create_events
assert len(ledger_upgrade) == 2, ledger_upgrade
assert len(archive_upgrade) == 4, archive_upgrade
assert len(create_status) == 2, create_status
assert len(create_mail_index) == 1, create_mail_index

def test_ledger(version: int):
    db = sqlite3.connect(":memory:")
    db.execute("""CREATE TABLE receipts (
        document_id TEXT PRIMARY KEY NOT NULL,
        document_number TEXT NOT NULL, recipient TEXT NOT NULL,
        document_date TEXT NOT NULL, confirmed_utc TEXT NOT NULL,
        mail_status TEXT NOT NULL DEFAULT 'pending')""")
    db.execute("""CREATE TABLE receipt_items (
        document_id TEXT NOT NULL, line_number INTEGER NOT NULL,
        name TEXT NOT NULL, unit TEXT NOT NULL, nsn TEXT,
        quantity TEXT NOT NULL, """ +
        ("unit_price TEXT, " if version == 2 else "") +
        "PRIMARY KEY(document_id,line_number))")
    for ident, kind, date, qty, price in [
        ("OPENING:TEST", "ПОЧАТКОВІ ЗАЛИШКИ", "2026-10-01", "10", "12.25"),
        ("DEMO-1", "ТЕСТОВА НАКЛАДНА", "2026-10-01", "5", "13.50")
    ]:
        db.execute("INSERT INTO receipts VALUES (?,?,?,?,?,?)",
                   (ident, kind, "Тестовий підрозділ", date,
                    "2026-10-11T00:00:00Z", "pending"))
        if version == 1:
            db.execute("INSERT INTO receipt_items VALUES (?,?,?,?,?,?)",
                       (ident, 1, "Канцелярія", "шт.", "TEST-NSN", qty))
        else:
            db.execute("INSERT INTO receipt_items VALUES (?,?,?,?,?,?,?)",
                       (ident, 1, "Канцелярія", "шт.", "TEST-NSN", qty, price))
    with db:
        if version == 1:
            db.execute(ledger_upgrade[0])  # Exact v1 -> v2 ADD COLUMN
        for statement in create_events:
            db.execute(statement)
        db.execute(ledger_upgrade[1])     # Exact v2 -> v3 posted event backfill
    assert db.execute("SELECT COUNT(*) FROM receipts").fetchone()[0] == 2
    assert db.execute("SELECT COUNT(*) FROM receipt_items").fetchone()[0] == 2
    events = db.execute("SELECT document_id, event_type, quantity_delta, unit_price "
                        "FROM ledger_events ORDER BY document_id").fetchall()
    assert len(events) == 2, events
    assert {e[1] for e in events} == {"receipt", "opening"}, events
    assert sum(float(e[2]) for e in events) == 15, events
    if version == 1:
        assert all(e[3] is None for e in events)
    else:
        assert {e[3] for e in events} == {"12.25", "13.50"}
    # Backfill does not change the original receipts or insert a second invoice.
    assert db.execute("SELECT COUNT(*) FROM ledger_events").fetchone()[0] == 2
    db.close()


def test_archive():
    db = sqlite3.connect(":memory:")
    db.execute("""CREATE TABLE documents (
        document_id TEXT PRIMARY KEY NOT NULL, kind TEXT NOT NULL,
        number TEXT NOT NULL, unit TEXT NOT NULL, document_date TEXT NOT NULL,
        received_utc TEXT NOT NULL, saved_utc TEXT NOT NULL, status TEXT NOT NULL,
        content_sha256 TEXT NOT NULL, file_name TEXT NOT NULL, sealed_json TEXT NOT NULL)""")
    db.execute("INSERT INTO documents VALUES (?,?,?,?,?,?,?,?,?,?,?)",
               ("DEMO-1", "Накладна", "2", "Тестовий підрозділ", "2026-10-01",
                "2026-10-11T00:00:00Z", "2026-10-11T00:00:00Z", "saved",
                "HASH-EXAMPLE", "document.json", "SEALED-PAYLOAD"))
    with db:
        for statement in archive_upgrade[:3]:
            db.execute(statement)
        for statement in create_status:
            db.execute(statement)
        db.execute(archive_upgrade[3])  # Backfill archive=saved audit row
        db.execute(create_mail_index[0])  # Upgraded v1 needs durable Gmail state
    doc = db.execute("SELECT sealed_json, posting_status, delivery_status, sync_status "
                     "FROM documents WHERE document_id = 'DEMO-1'").fetchone()
    assert doc == ("SEALED-PAYLOAD", "not_posted", "not_queued", "not_synced"), doc
    history = db.execute("SELECT domain,state FROM document_status_history").fetchall()
    assert history == [("archive","saved")], history
    db.execute("INSERT INTO mail_processing VALUES (?,?,?,?,?)",
               ("1024","42","sender@example.org","ready","2026-10-12T00:00:00Z"))
    assert db.execute("SELECT status FROM mail_processing WHERE uid = '42'").fetchone()[0] == "ready"
    # Idempotent schema definition must be safe if called during a repair.
    db.execute(create_mail_index[0])
    assert db.execute("SELECT COUNT(*) FROM documents").fetchone()[0] == 1
    db.close()


test_ledger(1)
test_ledger(2)
test_archive()
print("SQLite SQL migration fixtures: v1->v3, v2->v3, archive v1->v2 PASS")
