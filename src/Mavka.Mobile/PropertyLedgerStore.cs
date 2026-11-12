using Android.Content;
using Android.Database.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Mavka.Mobile;

internal sealed record InventoryHolding(
    string Name, string Unit, string? Nsn, decimal Quantity,
    decimal? UnitPrice, decimal? TotalValue);
internal sealed record InventoryMovement(
    string DocumentId, string Number, string Date, decimal Quantity,
    decimal? UnitPrice);

/// <summary>
/// Local, transactional receipt ledger. The same document ID cannot add inventory twice.
/// Email delivery is tracked separately and may be retried after a network failure.
/// </summary>
internal sealed class PropertyLedgerStore : SQLiteOpenHelper
{
    public PropertyLedgerStore(Context context)
        : base(context, "mavka_property_ledger_v1.db", null, 3) { }

    public override void OnCreate(SQLiteDatabase db)
    {
        db.ExecSQL(@"CREATE TABLE receipts (
            document_id TEXT PRIMARY KEY NOT NULL,
            document_number TEXT NOT NULL,
            recipient TEXT NOT NULL,
            document_date TEXT NOT NULL,
            confirmed_utc TEXT NOT NULL,
            mail_status TEXT NOT NULL DEFAULT 'pending'
        )");
        db.ExecSQL(@"CREATE TABLE receipt_items (
            document_id TEXT NOT NULL,
            line_number INTEGER NOT NULL,
            name TEXT NOT NULL,
            unit TEXT NOT NULL,
            nsn TEXT,
            quantity TEXT NOT NULL,
            unit_price TEXT,
            PRIMARY KEY(document_id, line_number),
            FOREIGN KEY(document_id) REFERENCES receipts(document_id)
        )");
        db.ExecSQL("CREATE INDEX idx_receipt_unit ON receipts(recipient)");
        CreateEventTable(db);
    }

    public override void OnUpgrade(SQLiteDatabase db, int oldVersion, int newVersion)
    {
        // Non-destructive, sequential migrations keep every receipt and item.
        if (oldVersion < 1 || newVersion != 3)
            throw new InvalidOperationException("Unsupported ledger schema version.");
        if (oldVersion == 1)
            db.ExecSQL("ALTER TABLE receipt_items ADD COLUMN unit_price TEXT");
        if (oldVersion <= 2)
        {
            CreateEventTable(db);
            // Exactly one posted event per existing item. This is an idempotent
            // schema upgrade, not a second inventory receipt.
            db.ExecSQL(@"INSERT INTO ledger_events
              (event_id, document_id, line_number, event_type, accounting_date,
               recorded_utc, unit, name, measure, nsn, quantity_delta, unit_price,
               status, origin)
              SELECT r.document_id || '#' || i.line_number, r.document_id,
                     i.line_number,
                     CASE WHEN r.document_id LIKE 'OPENING:%' THEN 'opening'
                          ELSE 'receipt' END,
                     r.document_date, r.confirmed_utc, r.recipient,
                     i.name, i.unit, i.nsn, i.quantity, i.unit_price,
                     'posted', 'mobile'
                FROM receipts r INNER JOIN receipt_items i
                ON r.document_id = i.document_id");
        }
    }

    private static void CreateEventTable(SQLiteDatabase db)
    {
        db.ExecSQL(@"CREATE TABLE ledger_events(
            event_id TEXT PRIMARY KEY NOT NULL,
            document_id TEXT NOT NULL,
            line_number INTEGER NOT NULL,
            event_type TEXT NOT NULL,
            accounting_date TEXT NOT NULL,
            recorded_utc TEXT NOT NULL,
            unit TEXT NOT NULL,
            name TEXT NOT NULL,
            measure TEXT NOT NULL,
            nsn TEXT,
            quantity_delta TEXT NOT NULL,
            unit_price TEXT,
            status TEXT NOT NULL,
            origin TEXT NOT NULL)");
        db.ExecSQL("CREATE INDEX idx_ledger_event_unit_date ON ledger_events(unit, accounting_date)");
        db.ExecSQL("CREATE UNIQUE INDEX idx_event_document_line ON ledger_events(document_id, line_number, event_type)");
    }

    private static void AddEvent(SQLiteDatabase database, string documentId,
        int line, string kind, string date, string unit, string name,
        string measure, string? nsn, decimal delta, decimal? price)
    {
        var values = new ContentValues();
        values.Put("event_id", documentId + "#" + line);
        values.Put("document_id", documentId);
        values.Put("line_number", line);
        values.Put("event_type", kind);
        values.Put("accounting_date", date);
        values.Put("recorded_utc", DateTimeOffset.UtcNow.ToString("O"));
        values.Put("unit", unit);
        values.Put("name", name);
        values.Put("measure", measure);
        values.Put("nsn", nsn ?? "");
        values.Put("quantity_delta", delta.ToString(CultureInfo.InvariantCulture));
        if (price.HasValue)
            values.Put("unit_price", price.Value.ToString(CultureInfo.InvariantCulture));
        else values.PutNull("unit_price");
        values.Put("status", "posted");
        values.Put("origin", "mobile");
        database.InsertOrThrow("ledger_events", null, values);
    }

    public IReadOnlyList<InventoryHolding> ListHoldingsAsOf(string unit, DateOnly date)
    {
        var postings = new List<DatedLedgerEntry>();
        using (var db = ReadableDatabase)
        using (var cursor = db.RawQuery(
            @"SELECT event_id, document_id, unit, name, measure, nsn,
                     accounting_date, quantity_delta, unit_price, status
              FROM ledger_events WHERE unit = ? COLLATE NOCASE",
            new[] { unit }))
        {
            while (cursor.MoveToNext())
                postings.Add(new DatedLedgerEntry(
                    cursor.GetString(0), cursor.GetString(1), cursor.GetString(2),
                    cursor.GetString(3), cursor.GetString(4),
                    cursor.IsNull(5) ? null : cursor.GetString(5),
                    cursor.GetString(6),
                    decimal.Parse(cursor.GetString(7), CultureInfo.InvariantCulture),
                    cursor.IsNull(8) ? null :
                        decimal.Parse(cursor.GetString(8), CultureInfo.InvariantCulture),
                    cursor.GetString(9)));
        }
        return LedgerBalanceCalculator.Calculate(postings, unit, date)
            .Select(x => new InventoryHolding(x.Name, x.Measure, x.Nsn,
                x.Quantity, x.UnitPrice, x.TotalValue)).ToList();
    }

    public bool HasReceipts(string unit)
    {
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            "SELECT 1 FROM receipts WHERE recipient = ? COLLATE NOCASE LIMIT 1",
            new[] { unit });
        return cursor.MoveToFirst();
    }

    // Existing durable receipt statuses also act as an offline sending queue.
    // They survive process termination without any extra in-memory job list.
    public IReadOnlyList<string> ListPendingReceiptIds(int limit = 25)
    {
        var ids = new List<string>();
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT document_id FROM receipts
              WHERE mail_status = 'pending'
              ORDER BY confirmed_utc ASC LIMIT ?",
            new[] { Math.Clamp(limit, 1, 100).ToString(CultureInfo.InvariantCulture) });
        while (cursor.MoveToNext()) ids.Add(cursor.GetString(0));
        return ids;
    }

    public string? GetMailStatus(string documentId)
    {
        using var database = ReadableDatabase;
        using var cursor = database.RawQuery(
            "SELECT mail_status FROM receipts WHERE document_id = ?",
            new[] { documentId });
        return cursor.MoveToFirst() ? cursor.GetString(0) : null;
    }

    public bool AddConfirmedInvoice(IncomingInvoice invoice)
    {
        using var database = WritableDatabase;
        database.BeginTransaction();
        try
        {
            using (var existing = database.RawQuery(
                "SELECT 1 FROM receipts WHERE document_id = ?",
                new[] { invoice.DocumentId }))
            {
                if (existing.MoveToFirst()) return false;
            }

            var receipt = new ContentValues();
            receipt.Put("document_id", invoice.DocumentId);
            receipt.Put("document_number", invoice.Number);
            receipt.Put("recipient", invoice.Recipient);
            receipt.Put("document_date", invoice.Date);
            receipt.Put("confirmed_utc", DateTimeOffset.UtcNow.ToString("O"));
            receipt.Put("mail_status", "pending");
            database.InsertOrThrow("receipts", null, receipt);

            foreach (var item in invoice.Positions)
            {
                var values = new ContentValues();
                values.Put("document_id", invoice.DocumentId);
                values.Put("line_number", item.LineNo);
                values.Put("name", item.Name);
                values.Put("unit", item.Unit);
                values.Put("nsn", item.Nsn ?? "");
                values.Put("quantity", item.Quantity.ToString(CultureInfo.InvariantCulture));
                if (item.UnitPrice.HasValue)
                    values.Put("unit_price", item.UnitPrice.Value.ToString(CultureInfo.InvariantCulture));
                else values.PutNull("unit_price");
                database.InsertOrThrow("receipt_items", null, values);
                AddEvent(database, invoice.DocumentId, item.LineNo, "receipt",
                    invoice.Date, invoice.Recipient, item.Name, item.Unit,
                    item.Nsn, item.Quantity, item.UnitPrice);
            }

            database.SetTransactionSuccessful();
            return true;
        }
        finally { database.EndTransaction(); }
    }

    /// <summary>
    /// Import a single, explicit opening-balance baseline for the unit.
    /// Never mix a dated snapshot into a ledger that already has posted receipts.
    /// </summary>
    public bool AddOpeningBalances(UnitAssetsSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Unit) ||
            snapshot.Items.Count == 0 || snapshot.Items.Count > 500)
            throw new ArgumentException("Invalid opening inventory snapshot.");

        var unit = snapshot.Unit.Trim();
        var id = "OPENING:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(unit.ToUpperInvariant())))[..24];
        using var database = WritableDatabase;
        database.BeginTransaction();
        try
        {
            using (var existing = database.RawQuery(
                "SELECT document_id FROM receipts WHERE recipient = ? COLLATE NOCASE LIMIT 1",
                new[] { unit }))
            {
                if (existing.MoveToFirst())
                {
                    // An earlier opening baseline is idempotent; any other
                    // document means importing a baseline risks double counting.
                    using var opening = database.RawQuery(
                        "SELECT 1 FROM receipts WHERE document_id = ?", new[] { id });
                    if (opening.MoveToFirst()) return false;
                    throw new InvalidOperationException(
                        "Unit already has posted inventory. Opening balances must be imported first.");
                }
            }
            var receipt = new ContentValues();
            receipt.Put("document_id", id);
            receipt.Put("document_number", "ПОЧАТКОВІ ЗАЛИШКИ");
            receipt.Put("recipient", unit);
            receipt.Put("document_date", snapshot.AsOf);
            receipt.Put("confirmed_utc", DateTimeOffset.UtcNow.ToString("O"));
            receipt.Put("mail_status", "not_required");
            database.InsertOrThrow("receipts", null, receipt);

            var line = 0;
            foreach (var asset in snapshot.Items)
            {
                if (asset.Quantity < 0 || asset.Quantity > 1000000000m ||
                    string.IsNullOrWhiteSpace(asset.Name))
                    throw new ArgumentException("Invalid opening balance row.");
                if (asset.Quantity == 0) continue;

                var values = new ContentValues();
                values.Put("document_id", id);
                values.Put("line_number", ++line);
                values.Put("name", asset.Name);
                values.Put("unit", asset.Measure);
                values.Put("nsn", asset.Nsn ?? "");
                values.Put("quantity", asset.Quantity.ToString(CultureInfo.InvariantCulture));
                if (asset.UnitPrice.HasValue)
                    values.Put("unit_price", asset.UnitPrice.Value.ToString(CultureInfo.InvariantCulture));
                else values.PutNull("unit_price");
                database.InsertOrThrow("receipt_items", null, values);
                AddEvent(database, id, line, "opening",
                    snapshot.AsOf, unit, asset.Name, asset.Measure,
                    asset.Nsn, asset.Quantity, asset.UnitPrice);
            }
            database.SetTransactionSuccessful();
            return true;
        }
        finally { database.EndTransaction(); }
    }

    public void MarkMailSent(string documentId)
    {
        using var database = WritableDatabase;
        var values = new ContentValues();
        values.Put("mail_status", "sent");
        database.Update("receipts", values, "document_id = ?", new[] { documentId });
    }

    /// <summary>Read-only movement history of locally confirmed receipts for one exact item.</summary>
    public IReadOnlyList<InventoryMovement> ListMovements(
        string recipient, string name, string unit, string? nsn)
    {
        var result = new List<InventoryMovement>();
        using var db = ReadableDatabase;
        using var cursor = db.RawQuery(
            @"SELECT r.document_id, r.document_number, r.document_date, i.quantity, i.unit_price
              FROM receipt_items i
              INNER JOIN receipts r ON r.document_id = i.document_id
              WHERE r.recipient = ? COLLATE NOCASE
                AND i.name = ? AND i.unit = ? AND COALESCE(i.nsn, '') = ?
              ORDER BY r.document_date DESC, r.document_id DESC",
            new[] { recipient, name, unit, nsn ?? "" });
        while (cursor.MoveToNext())
            result.Add(new InventoryMovement(cursor.GetString(0), cursor.GetString(1),
                cursor.GetString(2), decimal.Parse(cursor.GetString(3),
                    CultureInfo.InvariantCulture), cursor.IsNull(4) ? null :
                    decimal.Parse(cursor.GetString(4), CultureInfo.InvariantCulture)));
        return result;
    }

    public IReadOnlyList<InventoryHolding> ListHoldings(string unit)
    {
        var lines = new List<InventoryHolding>();
        using var database = ReadableDatabase;
        using var cursor = database.RawQuery(
            @"SELECT i.name, i.unit, i.nsn, i.quantity, i.unit_price
              FROM receipt_items i
              INNER JOIN receipts r ON r.document_id = i.document_id
              WHERE r.recipient = ? COLLATE NOCASE
              ORDER BY i.name", new[] { unit });
        while (cursor.MoveToNext())
        {
            var quantity = decimal.Parse(cursor.GetString(3), CultureInfo.InvariantCulture);
            decimal? price = cursor.IsNull(4) ? null :
                decimal.Parse(cursor.GetString(4), CultureInfo.InvariantCulture);
            lines.Add(new InventoryHolding(cursor.GetString(0), cursor.GetString(1),
                cursor.IsNull(2) ? null : cursor.GetString(2), quantity,
                price, price.HasValue ? quantity * price.Value : null));
        }

        return lines
            .GroupBy(v => (v.Name, v.Unit, v.Nsn, v.UnitPrice))
            .Select(group => new InventoryHolding(group.Key.Name, group.Key.Unit,
                group.Key.Nsn, group.Sum(v => v.Quantity), group.Key.UnitPrice,
                group.Key.UnitPrice.HasValue ? group.Sum(v => v.TotalValue ?? 0m) : null))
            .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
