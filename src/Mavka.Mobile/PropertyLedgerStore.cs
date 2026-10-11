using Android.Content;
using Android.Database.Sqlite;
using System.Globalization;

namespace Mavka.Mobile;

internal sealed record InventoryHolding(
    string Name, string Unit, string? Nsn, decimal Quantity,
    decimal? UnitPrice, decimal? TotalValue);

/// <summary>
/// Local, transactional receipt ledger. The same document ID cannot add inventory twice.
/// Email delivery is tracked separately and may be retried after a network failure.
/// </summary>
internal sealed class PropertyLedgerStore : SQLiteOpenHelper
{
    public PropertyLedgerStore(Context context)
        : base(context, "mavka_property_ledger_v1.db", null, 2) { }

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
    }

    public override void OnUpgrade(SQLiteDatabase db, int oldVersion, int newVersion)
    {
        // Non-destructive migration preserves all previously confirmed movements.
        // Historical records receive NULL (unknown) prices.
        if (oldVersion == 1 && newVersion >= 2)
            db.ExecSQL("ALTER TABLE receipt_items ADD COLUMN unit_price TEXT");
        else if (newVersion != oldVersion)
            throw new InvalidOperationException("Unsupported ledger schema version.");
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
                else
                    values.PutNull("unit_price");
                database.InsertOrThrow("receipt_items", null, values);
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
            decimal? price = cursor.IsNull(4)
                ? null
                : decimal.Parse(cursor.GetString(4), CultureInfo.InvariantCulture);
            lines.Add(new InventoryHolding(cursor.GetString(0), cursor.GetString(1),
                cursor.IsNull(2) ? null : cursor.GetString(2), quantity,
                price, price.HasValue ? quantity * price.Value : null));
        }

        return lines
            // Equal name but different price must remain separate accounting lots.
            .GroupBy(v => (v.Name, v.Unit, v.Nsn, v.UnitPrice))
            .Select(group => new InventoryHolding(group.Key.Name, group.Key.Unit,
                group.Key.Nsn, group.Sum(v => v.Quantity), group.Key.UnitPrice,
                group.Key.UnitPrice.HasValue
                    ? group.Sum(v => v.TotalValue ?? 0m) : null))
            .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
