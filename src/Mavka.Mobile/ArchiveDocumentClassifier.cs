using System.Security.Cryptography;
using System.Text;

namespace Mavka.Mobile;

internal sealed record ArchiveImport(
    string DocumentId, string Kind, string Number, string Unit,
    string DocumentDate, string Json);

/// <summary>Only known, schema-validated documents may enter the auto-cleanup path.</summary>
internal static class ArchiveDocumentClassifier
{
    public static bool TryClassify(string? json, string expectedUnit,
        out ArchiveImport? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(expectedUnit))
            return false;

        string id, kind, number, unit, date;
        if (MavkaExchangeParser.TryInvoice(json, out var invoice) && invoice is not null)
        {
            id = invoice.DocumentId;
            kind = "Накладна";
            number = invoice.Number;
            unit = invoice.Recipient;
            date = invoice.Date;
        }
        else if (MavkaExchangeParser.TryAssets(json, out var snapshot) && snapshot is not null)
        {
            id = "SNAPSHOT:" + Fingerprint(json);
            kind = "Знімок залишків";
            number = "Знімок " + snapshot.AsOf;
            unit = snapshot.Unit;
            date = snapshot.AsOf;
        }
        else if (MavkaMovementJournal.TryRead(json, out var journal) && journal is not null)
        {
            id = "JOURNAL:" + Fingerprint(json);
            kind = "Довідкова історія";
            number = "Журнал " + journal.AsOf;
            unit = journal.Unit;
            date = journal.AsOf;
        }
        else return false;

        // Fail closed. A different unit is not an importable document for this profile.
        if (!string.Equals(unit.Trim(), expectedUnit.Trim(),
            StringComparison.OrdinalIgnoreCase)) return false;
        document = new ArchiveImport(id, kind, number, unit, date, json);
        return true;
    }

    private static string Fingerprint(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
