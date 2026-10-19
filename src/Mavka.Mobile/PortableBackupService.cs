using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

internal sealed record BackupDocument(string DocumentId, string Kind,
    string Number, string Unit, string DocumentDate, string FileName, string Json);

internal sealed record BackupPayload(
    string Format, string Unit, UserProfile Profile,
    IReadOnlyList<BackupDocument> Documents,
    IReadOnlyList<BackupReceipt> Receipts);

/// <summary>
/// Independent of Android Keystore: portable encrypted backup of the profile's
/// metadata, encrypted-archive document contents, and exact dated ledger
/// history. Does NOT contain Gmail app passwords or exchange private keys.
/// </summary>
internal sealed class PortableBackupService(
    DocumentArchiveStore archive, PropertyLedgerStore ledger,
    UserProfileStore profiles)
{
    public const string DataFormat = "MAVKA_BACKUP_DATA_V1";

    public byte[] Create(string password)
    {
        var profile = profiles.Load() ??
            throw new InvalidOperationException("Спочатку налаштуй профіль.");
        if (archive.CountDocuments(profile.Unit) > 1000)
            throw new InvalidOperationException("Понад 1000 документів: потрібна багатотомна копія.");
        var docs = archive.ListDocuments(profile.Unit)
            .Select(d => new BackupDocument(d.DocumentId, d.Kind,
                d.Number, d.Unit, d.DocumentDate, d.FileName, d.Json)).ToArray();
        var receipts = ledger.ExportReceipts(profile.Unit);
        var data = new BackupPayload(DataFormat, profile.Unit, profile, docs, receipts);
        var raw = JsonSerializer.Serialize(data);
        return PortableBackupEnvelope.Encrypt(raw, password);
    }

    public (int Documents, int Receipts) Restore(byte[] encrypted, string password)
    {
        var raw = PortableBackupEnvelope.Decrypt(encrypted, password);
        var bundle = JsonSerializer.Deserialize<BackupPayload>(raw) ??
            throw new InvalidOperationException("Резервну копію не розпізнано.");
        if (bundle.Format != DataFormat ||
            string.IsNullOrWhiteSpace(bundle.Unit) ||
            bundle.Documents is null || bundle.Receipts is null ||
            bundle.Profile is null || bundle.Documents.Count > 1000 ||
            bundle.Receipts.Count > 1000 ||
            !string.Equals(bundle.Profile.Unit, bundle.Unit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Пошкоджена структура резервної копії.");

        var active = profiles.Load();
        if (active is not null &&
            !string.Equals(active.Unit, bundle.Unit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Підрозділ у резервній копії не збігається з поточним профілем.");
        var duplicateIds = bundle.Documents.GroupBy(d => d.DocumentId)
            .Any(g => g.Count() > 1);
        if (duplicateIds) throw new InvalidOperationException("Повторні ідентифікатори документів.");
        if (ledger.HasReceipts(bundle.Unit))
            throw new InvalidOperationException(
                "У підрозділі вже є проведений облік. Відновлення поверх нього заборонено.");

        // Validate every document before the first local database write.
        foreach (var d in bundle.Documents)
        {
            if (string.IsNullOrWhiteSpace(d.DocumentId) ||
                !string.Equals(d.Unit, bundle.Unit, StringComparison.OrdinalIgnoreCase) ||
                d.Json?.Length is < 1 or > 1_000_000 ||
                d.Number.Length > 240 ||
                !ArchiveDocumentClassifier.TryClassify(d.Json, bundle.Unit, out var parsed) ||
                parsed is null || parsed.DocumentId != d.DocumentId)
                throw new InvalidOperationException("Документ у резервній копії не пройшов перевірку.");
            var present = archive.FindDocument(d.DocumentId);
            if (present is not null && !string.Equals(present.Json, d.Json, StringComparison.Ordinal))
                throw new InvalidOperationException("Збіг ID з іншим вмістом документа.");
        }

        // Existing profile is never overwritten; a new phone gets a new
        // ClientId and must complete pairing and key exchange again.
        if (active is null)
            profiles.Save(bundle.Unit, bundle.Profile.FullName,
                bundle.Profile.Rank, bundle.Profile.Position,
                bundle.Profile.IsMvo, bundle.Profile.PhoneNumber);

        // SaveDocument uses its own AES-GCM read-back and is idempotent. If a
        // write fails, importing the same authenticated backup can be retried.
        foreach (var d in bundle.Documents)
            archive.SaveDocument(d.DocumentId, d.Kind, d.Number, d.Unit,
                d.DocumentDate, d.FileName, d.Json);
        int restored = ledger.RestoreReceipts(bundle.Unit, bundle.Receipts);
        foreach (var receipt in bundle.Receipts)
        {
            if (archive.FindDocument(receipt.DocumentId) is null) continue;
            archive.MarkPosted(receipt.DocumentId);
            if (receipt.MailStatus == "sent")
                archive.MarkDeliverySent(receipt.DocumentId);
            else archive.MarkDeliveryQueued(receipt.DocumentId);
        }
        return (bundle.Documents.Count, restored);
    }
}
