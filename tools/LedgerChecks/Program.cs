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
Console.WriteLine("Historical ledger arithmetic regression checks: 8/8 PASS");
