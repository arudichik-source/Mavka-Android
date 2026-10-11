using Mavka.Mobile;

void Check(bool success, string message)
{
    if (!success) throw new InvalidOperationException(message);
}
Check(PhoneNumberNormalizer.Normalize("067 123 45 67") == "+380671234567", "UA local");
Check(PhoneNumberNormalizer.Normalize("380671234567") == "+380671234567", "UA international");
Check(PhoneNumberNormalizer.Normalize("+48 500 100 200") == "+48500100200", "International");
Check(PhoneNumberNormalizer.Normalize(null) == "", "Legacy profile with no phone");
foreach (var bad in new[] { "a123456789", "++380671234567", "067123", "+0123456789" })
{
    bool rejected = false;
    try { PhoneNumberNormalizer.Normalize(bad); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "Invalid phone accepted: " + bad);
}
const string unit = "Тестова частина";
const string validInvoice = """
{"format":"MAVKA_DOCUMENT_V1","type":"invoice",
 "documentId":"LOCAL-001","documentNumber":"7","recipient":"Тестова частина",
 "date":"2026-10-11","positions":[
 {"name":"Канцелярський папір","unit":"пач.","quantity":2,"price":100.55,"nsn":"TEST"}]}
""";
Check(ArchiveDocumentClassifier.TryClassify(validInvoice, unit, out var invoice)
    && invoice is not null && invoice.DocumentId == "LOCAL-001"
    && invoice.Kind == "Накладна", "Invoice classification");
Check(!ArchiveDocumentClassifier.TryClassify(validInvoice, "Інша частина", out _),
    "Cross-unit archive must be rejected");
const string snapshot = """
{"format":"MAVKA_UNIT_ASSETS_V1","unit":"Тестова частина",
 "asOf":"2026-10-01","items":[{"name":"Папір","unit":"пач.","quantity":1}]}
""";
Check(ArchiveDocumentClassifier.TryClassify(snapshot, unit, out var first)
    && ArchiveDocumentClassifier.TryClassify(snapshot, unit, out var second)
    && first!.DocumentId == second!.DocumentId
    && first.DocumentId.StartsWith("SNAPSHOT:", StringComparison.Ordinal),
    "Stable snapshot deduplication ID");
const string movements = """
{"format":"MAVKA_UNIT_MOVEMENTS_V1","unit":"Тестова частина",
 "asOf":"2026-10-11","entries":[
 {"type":"writeoff","documentId":"W-1","documentNumber":"5",
 "date":"2026-10-10","name":"Папір","unit":"пач.","quantity":1}]}
""";
Check(ArchiveDocumentClassifier.TryClassify(movements, unit, out var journal)
    && journal is not null && journal.Kind == "Довідкова історія",
    "Read-only history classification");
Check(!ArchiveDocumentClassifier.TryClassify("{}", unit, out _), "Unknown attachment");
Check(!ArchiveDocumentClassifier.TryClassify(
    validInvoice.Replace("\"quantity\":2", "\"quantity\":-9"), unit, out _),
    "Invalid quantity cannot be archived");
Console.WriteLine("Archive and profile regression checks: 14/14 PASS");
