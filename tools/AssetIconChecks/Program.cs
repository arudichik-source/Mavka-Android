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
