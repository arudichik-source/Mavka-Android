using Mavka.Mobile;

void Check(bool condition, string msg)
{
    if (!condition) throw new InvalidOperationException(msg);
}
var events = new[]
{
    new DatedLedgerEntry("OPEN#1","OPEN","Unit A","Paper","pack","X",
        "2026-10-01",10,12.25m,"posted"),
    new DatedLedgerEntry("R#1","R","Unit A","Paper","pack","X",
        "2026-10-01",5,12.25m,"posted"),
    new DatedLedgerEntry("W#1","W","Unit A","Paper","pack","X",
        "2026-10-02",-3,12.25m,"posted"),
    new DatedLedgerEntry("OTHER#1","OTHER","Unit B","Paper","pack","X",
        "2026-10-01",100,12.25m,"posted"),
    new DatedLedgerEntry("DRAFT#1","DRAFT","Unit A","Paper","pack","X",
        "2026-10-01",100,12.25m,"draft"),
    new DatedLedgerEntry("ALT#1","ALT","Unit A","Paper","pack","Y",
        "2026-10-01",8,12.25m,"posted"),
    new DatedLedgerEntry("N#1","N","Unit A","Unknown","pack",null,
        "2026-10-01",2,null,"posted")
};
var first = LedgerBalanceCalculator.Calculate(events,"Unit A",new DateOnly(2026,10,1));
var firstX = first.Single(e=>e.Nsn=="X");
Check(firstX.Quantity == 15m && firstX.TotalValue == 183.75m,"Opening + same-day posted receipt");
Check(first.Any(e=>e.Nsn=="Y" && e.Quantity==8),"Distinct NSN");
Check(first.Single(e=>e.Name=="Unknown").TotalValue is null,"Unknown price must remain unknown");
var second = LedgerBalanceCalculator.Calculate(events,"Unit A",new DateOnly(2026,10,2));
Check(second.Single(e=>e.Nsn=="X").Quantity==12m,"Date-dependent writeoff");
Check(LedgerBalanceCalculator.ParseDate("02.10.2026")==new DateOnly(2026,10,2),
    "Ukrainian date");
Check(LedgerBalanceCalculator.Calculate(events,"Unit C",
    new DateOnly(2026,10,2)).Count==0,"Unit boundary");
bool duplicateRejected=false;
try { LedgerBalanceCalculator.Calculate(events.Append(events[0]),
    "Unit A",new DateOnly(2026,10,2)); }
catch (InvalidOperationException) { duplicateRejected=true; }
Check(duplicateRejected,"Duplicate event ID");
bool malformedRejected=false;
try { LedgerBalanceCalculator.ParseDate("tomorrow"); }
catch (ArgumentException) { malformedRejected=true; }
Check(malformedRejected,"Invalid accounting date");
// Device QA regression: "12" must show 54,307.62 UAH for the two
// visible military items, not the 605,253.62 UAH total of all rows.
var screenHoldings = new[]
{
    new HistoricalHolding("12×70 мм патрон гладкоствольний (QA31)",
        "шт.", null, 18m, 17.09m, 307.62m),
    new HistoricalHolding("120 мм постріл мінометний навчальний (QA31)",
        "шт.", null, 3m, 18000m, 54000m),
    new HistoricalHolding("155 мм снаряд навчальний (QA31)",
        "шт.", "NSN-QA31-155", 2m, 275473m, 550946m),
    new HistoricalHolding("Заряд тестовий без ціни",
        "шт.", null, 1m, null, null)
};
var visible12 = InventorySearchSummary.Filter(screenHoldings, "12",
    x => x.Name, x => x.Nsn);
Check(visible12.Count == 2, "Search 12 must find exactly two inventory rows");
Check(InventorySearchSummary.Summarize(visible12, x => x.TotalValue).KnownTotal
    == 54307.62m, "Visible value must be 54,307.62 UAH");
Check(InventorySearchSummary.Summarize(screenHoldings, x => x.TotalValue).KnownTotal
    == 605253.62m, "All-items value must remain available separately");
Check(InventorySearchSummary.Filter(screenHoldings, "nsn-qa31-155",
    x => x.Name, x => x.Nsn).Count == 1, "NSN search remains case-insensitive");
Check(InventorySearchSummary.Filter(screenHoldings, "absent",
    x => x.Name, x => x.Nsn).Count == 0, "Unmatched search must show zero rows");
Check(InventorySearchSummary.Summarize(
    InventorySearchSummary.Filter(screenHoldings, "без ціни", x => x.Name, x => x.Nsn),
    x => x.TotalValue).UnknownPriceCount == 1, "Unknown prices are counted only within visible rows");
Check(InventorySearchSummary.Summarize(
    InventorySearchSummary.Filter(screenHoldings, "absent", x => x.Name, x => x.Nsn),
    x => x.TotalValue).KnownTotal == 0m, "Empty search has zero visible total");
Console.WriteLine("Historical ledger + live-search inventory totals: 15/15 PASS");
