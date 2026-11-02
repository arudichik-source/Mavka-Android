using Mavka.Mobile;

void Check(bool ok, string reason)
{
    if (!ok) throw new InvalidOperationException(reason);
}

var local = new[]
{
    new HistoricalHolding("Папір А4", "пач.", "NSN-TEST-1", 5, 100.25m, 501.25m),
    new HistoricalHolding("Папір А4", "пач.", "NSN-TEST-2", 2, 100.25m, 200.50m),
    new HistoricalHolding("Ручка", "шт.", null, 6, null, null),
};
var remote = new[]
{
    new UnitAsset("Папір А4", "пач.", 4, 100.25m, "NSN-TEST-1"),
    new UnitAsset("Папір А4", "пач.", 2, 100.25m, "NSN-TEST-2"),
    new UnitAsset("Ручка", "шт.", 5, null, null),
    new UnitAsset("Тека", "шт.", 3, 17.5m, null)
};
var diffs = LedgerReconciliation.Compare(local, remote);
Check(diffs.Count == 3, "Only differing positions should appear.");
Check(diffs.Single(d => d.Nsn == "NSN-TEST-1").Delta == 1, "Quantity delta sign.");
Check(!diffs.Any(d => d.Nsn == "NSN-TEST-2"), "Equal NSN must not show.");
Check(diffs.Single(d => d.Name == "Ручка").LocalValue is null,
    "Unknown price must remain null.");
Check(diffs.Single(d => d.Name == "Тека").LocalQuantity == 0,
    "Missing local item is zero quantity but not a posted movement.");
var changedPrice = new[]
{
    new UnitAsset("Папір А4","пач.",5,100.26m,"NSN-TEST-1")
};
Check(LedgerReconciliation.Compare(local.Take(1),changedPrice).Count == 2,
    "Separate price batches must not be grouped.");
Check(LedgerReconciliation.Compare(Array.Empty<HistoricalHolding>(),
    Array.Empty<UnitAsset>()).Count == 0, "Empty reconciliation");
Console.WriteLine("Snapshot reconciliation regression checks: 7/7 PASS");
