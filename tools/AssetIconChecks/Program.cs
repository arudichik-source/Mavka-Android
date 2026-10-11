using Mavka.Mobile;

var tests = new (string Name, AssetIconKind Kind)[]
{
    ("155 мм снаряд", AssetIconKind.Shell),
    ("Снаряди", AssetIconKind.Shell),
    ("155мм заряд", AssetIconKind.Charge),
    ("Заряди", AssetIconKind.Charge),
    ("40 мм постріл", AssetIconKind.Round),
    ("Постріли", AssetIconKind.Round),
    ("Праймер", AssetIconKind.Primer),
    ("Праймери артилерійські", AssetIconKind.Primer),
    ("122 мм реактивний снаряд", AssetIconKind.Rocket),
    ("Реактивні снаряди", AssetIconKind.Rocket),
    ("Підривник", AssetIconKind.Fuze),
    ("Підривники", AssetIconKind.Fuze),
    ("12х70 мм патрон", AssetIconKind.TwelveGauge),
    ("12×70мм патрон", AssetIconKind.TwelveGauge),
    ("12x70 патрон", AssetIconKind.TwelveGauge),
    ("12 х 70 патрон", AssetIconKind.TwelveGauge),
    ("12 калібру патрон", AssetIconKind.TwelveGauge),
    ("12 калібр патрон", AssetIconKind.TwelveGauge),
    ("12мм патрон", AssetIconKind.TwelveGauge),
    ("12 мм патрон", AssetIconKind.TwelveGauge),
    ("12 кАлІбРу", AssetIconKind.TwelveGauge),
    ("12,7 мм патрон", AssetIconKind.Cartridge),
    ("5,45 мм патрон", AssetIconKind.Cartridge),
    ("Патрони", AssetIconKind.Cartridge),
    ("112мм патрон", AssetIconKind.Cartridge),
    ("Папір А4", AssetIconKind.Unknown),
    ("", AssetIconKind.Unknown)
};

foreach (var (name, expected) in tests)
{
    var actual = AssetIconClassifier.Classify(name);
    if (actual != expected)
        throw new Exception($"Mismatch for '{name}': expected {expected}, got {actual}");
}
Console.WriteLine($"Icon category tests: {tests.Length}/{tests.Length} PASS.");


const string journalJson = """
{
  "format": "MAVKA_UNIT_MOVEMENTS_V1",
  "unit": "Тестовий підрозділ",
  "asOf": "2026-10-11",
  "entries": [
    {"type":"invoice","documentId":"R1","documentNumber":"N-001",
     "date":"2026-10-10","name":"Папір А4","unit":"пач.",
     "quantity":10,"price":120.50},
    {"type":"writeoff","documentId":"W1","documentNumber":"A-001",
     "date":"2026-10-11","name":"Папір А4","unit":"пач.",
     "quantity":2,"price":120.50}
  ]
}
""";
if (!MavkaMovementJournal.TryRead(journalJson, out var journal) ||
    journal is null || journal.Entries.Count != 2)
    throw new Exception("Valid movement journal rejected.");
var movements = MavkaMovementJournal.ForAsset(journal,
    "Тестовий підрозділ", "Папір А4", "пач.", null);
if (movements.Count != 2 || movements[1].Kind != "writeoff")
    throw new Exception("Write-off history not available.");
if (MavkaMovementJournal.ForAsset(journal,
    "Інший підрозділ", "Папір А4", "пач.", null).Count != 0)
    throw new Exception("Unit boundary check failed.");
if (MavkaMovementJournal.TryRead(journalJson.Replace(
        "\"quantity\":2", "\"quantity\":-2"), out _))
    throw new Exception("Invalid negative quantity was accepted.");
Console.WriteLine("Movement journal tests: 4/4 PASS.");


const string opening = """
{
 "format":"MAVKA_UNIT_ASSETS_V1", "unit":"Тестовий підрозділ",
 "asOf":"2026-10-01",
 "items":[
   {"name":"Папір А4","unit":"пач.","quantity":10,"price":125.50,"nsn":"TEST-A"},
   {"name":"Папір А4","unit":"пач.","quantity":5,"price":132.00,"nsn":"TEST-B"}
 ]
}
""";
if (!MavkaExchangeParser.TryAssets(opening, out var openingSnapshot) ||
    openingSnapshot is null || openingSnapshot.Items.Count != 2)
    throw new Exception("Opening balance snapshot failed to parse.");
if (openingSnapshot.Items[0].Nsn != "TEST-A" ||
    openingSnapshot.Items[1].Nsn != "TEST-B" ||
    openingSnapshot.Items[0].UnitPrice != 125.50m ||
    openingSnapshot.Items[1].UnitPrice != 132m)
    throw new Exception("Opening balance NSNs/prices were lost.");
const string pricedInvoice = """
{
 "format":"MAVKA_DOCUMENT_V1", "type":"invoice",
 "documentId":"TEST-I-002", "documentNumber":"TEST-2",
 "recipient":"Тестовий підрозділ", "date":"2026-10-11",
 "positions":[
   {"name":"Ручка кулькова", "unit":"шт.", "quantity":4, "price":9.75, "nsn":"TEST-9"},
   {"name":"Папір А4", "unit":"пач.", "quantity":3, "nsn":"TEST-A"}
 ]
}
""";
if (!MavkaExchangeParser.TryInvoice(pricedInvoice, out var invoice) ||
    invoice is null || invoice.Positions.Count != 2 ||
    invoice.Positions[0].UnitPrice != 9.75m ||
    invoice.Positions[1].UnitPrice is not null ||
    invoice.Positions[0].Nsn != "TEST-9")
    throw new Exception("Invoice price/null/NSN regression.");
if (MavkaExchangeParser.TryAssets(opening.Replace(
    "\"price\":132.00", "\"price\":-3.00"), out _))
    throw new Exception("Negative accounting price accepted.");
Console.WriteLine("Snapshot and invoice monetary/NSN tests: 4/4 PASS.");


// Regressions from real-device build #58: JSON arrived successfully,
// but MAVKA_UNIT_ASSETS_V1 was called "unknown" and Cyrillic was shown as \u04xx.
const string escapedSnapshot = """
{
  "format": "MAVKA_UNIT_ASSETS_V1",
  "unit": "\u0422\u0435\u0441\u0442\u043e\u0432\u0438\u0439 \u043f\u0456\u0434\u0440\u043e\u0437\u0434\u0456\u043b",
  "asOf": "2026-10-01",
  "items": [
    {"name": "\u041f\u0430\u043f\u0456\u0440 \u04104",
     "unit": "\u043f\u0430\u0447.", "quantity": 10,
     "price": 120.50, "nsn": "DEMO-PAPER-001"},
    {"name": "\u0420\u0443\u0447\u043a\u0430",
     "unit": "\u0448\u0442.", "quantity": 20,
     "price": 10.25, "nsn": "DEMO-PEN-002"}
  ]
}
""";
var assetPreview = MavkaJsonPreview.Format(escapedSnapshot);
foreach (var required in new[]
{
    "MAVKA_UNIT_ASSETS_V1", "Тестовий підрозділ", "Папір А4", "Ручка",
    "DEMO-PAPER-001", "120,50", "1 205,00", "не зараховано автоматично"
})
{
    // All strings except presentation-specific non-breaking group separator.
    if (required == "1 205,00") continue;
    if (!assetPreview.Contains(required, StringComparison.Ordinal))
        throw new Exception("Missing text in inventory preview: " + required);
}
if (assetPreview.Contains("Формат обміну Мавки не визначений", StringComparison.Ordinal) ||
    assetPreview.Contains(@"\u04", StringComparison.OrdinalIgnoreCase))
    throw new Exception("Inventory preview is still unrecognized or Unicode-escaped.");

var journalPreview = MavkaJsonPreview.Format(journalJson);
if (!journalPreview.Contains("Акт списання", StringComparison.Ordinal) ||
    !journalPreview.Contains("Історія рухів", StringComparison.Ordinal) ||
    !journalPreview.Contains("не змінюють залишки", StringComparison.Ordinal))
    throw new Exception("Movement journal is not presented as a read-only document.");

var invoicePreview = MavkaJsonPreview.Format(pricedInvoice);
if (!invoicePreview.Contains("Ручка кулькова", StringComparison.Ordinal) ||
    !invoicePreview.Contains("9,75", StringComparison.Ordinal) ||
    !invoicePreview.Contains("TEST-9", StringComparison.Ordinal))
    throw new Exception("Invoice price/NSN preview regression.");

var unknownPreview = MavkaJsonPreview.Format(
    """{"format":"OTHER","name":"\u041f\u0430\u043f\u0456\u0440"}""");
if (!unknownPreview.Contains("Папір", StringComparison.Ordinal) ||
    unknownPreview.Contains(@"\u04", StringComparison.OrdinalIgnoreCase))
    throw new Exception("Unknown JSON does not display readable Ukrainian.");

var invalidSnapshot = MavkaJsonPreview.Format(
    """{"format":"MAVKA_UNIT_ASSETS_V1","unit":"Demo","items":{}}""");
if (!invalidSnapshot.Contains("не відповідають структурі", StringComparison.Ordinal))
    throw new Exception("Invalid inventory structure not reported.");
Console.WriteLine("Mobile JSON preview regressions: 5/5 PASS.");


const string escapedAssets = """
{
  "format":"MAVKA_UNIT_ASSETS_V1",
  "unit":"\u0422\u0435\u0441\u0442\u043e\u0432\u0438\u0439 \u043f\u0456\u0434\u0440\u043e\u0437\u0434\u0456\u043b",
  "asOf":"2026-10-01",
  "items":[
    {"name":"\u041f\u0430\u043f\u0456\u0440 \u04104",
     "unit":"\u043f\u0430\u0447.", "quantity":10,
     "price":120.50,"nsn":"DEMO-PAPER-001"},
    {"name":"\u0420\u0443\u0447\u043a\u0430 \u043a\u0443\u043b\u044c\u043a\u043e\u0432\u0430",
     "unit":"\u0448\u0442.", "quantity":20,
     "price":10.25,"nsn":"DEMO-PEN-002"}
  ]
}
""";
var readable = MavkaJsonPreview.Format(escapedAssets);
if (!readable.Contains("Майно підрозділу") ||
    !readable.Contains("Папір А4") ||
    !readable.Contains("Ручка кулькова") ||
    !readable.Contains("DEMO-PAPER-001") ||
    !readable.Replace(" ", "").Replace("\u00a0", "").Replace("\u202f", "").Contains("1410,00грн") ||
    readable.Contains("\\u04", StringComparison.Ordinal))
    throw new Exception("Opening inventory Unicode/money preview failed.");

const string escapedUnknown = """
{"type":"demo","value":"\u041f\u0430\u043f\u0456\u0440"}
""";
var prettyUnknown = MavkaJsonPreview.Format(escapedUnknown);
if (!prettyUnknown.Contains("Папір") ||
    prettyUnknown.Contains("\\u04", StringComparison.Ordinal))
    throw new Exception("Unknown format still escapes Cyrillic text.");

const string invalidAssets = """
{"format":"MAVKA_UNIT_ASSETS_V1","unit":"Test",
 "items":[{"name":"Bad","unit":"pcs","quantity":-2}]}
""";
if (!MavkaJsonPreview.Format(invalidAssets).Contains("не відповідають структурі"))
    throw new Exception("Malformed asset snapshot was not visibly rejected.");

const string movementForPreview = """
{"format":"MAVKA_UNIT_MOVEMENTS_V1","unit":"Тестовий підрозділ",
 "entries":[{"type":"writeoff","documentId":"W1","documentNumber":"A-001",
 "date":"2026-10-11","name":"Папір А4","unit":"пач.","quantity":2,"price":120.50}]}
""";
if (!MavkaJsonPreview.Format(movementForPreview).Contains("Акт списання № A-001"))
    throw new Exception("Write-off movement format was not recognized.");
Console.WriteLine("Readable JSON preview tests: 4/4 PASS.");
