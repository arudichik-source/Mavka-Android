using Android.Content;

namespace Mavka.Mobile;

internal sealed record IncomingBatchResult(int OpeningUnits,int Invoices,IReadOnlyList<string> Issues)
{
    public string Summary => $"Зараховано залишків підрозділів: {OpeningUnits}; накладних: {Invoices}." +
        (Issues.Count==0 ? " Решта вже зарахована або збережена для перегляду." :
            " Потребують звірки: "+Issues.Count+".\n"+string.Join("\n",Issues));
}

/// <summary>Recover saved documents across exact granted units, independent of the selected screen.
/// Each document commits atomically; a failure remains visible and never undoes another unit.
/// Only authenticated, read-back-verified archive documents are candidates. No network calls.</summary>
internal static class IncomingBatchPosting
{
    public static IncomingBatchResult Apply(Context context,bool manual=false)
    {
        var accessStore=new MobileAccessStore(context);
        var policy=accessStore.Load();
        if(policy is null || policy.isMvo && !manual)return new(0,0,[]);
        using var archive=new DocumentArchiveStore(context);
        using var ledger=new PropertyLedgerStore(context);
        var openings=0;var invoices=0;var issues=new List<string>();
        foreach(var grant in policy.units)
        {
            var summaries=archive.ListSummaries(grant.unit);
            // Never sum dated snapshots or pick one by Gmail arrival order.
            if(policy.canViewAssets && !ledger.HasOpeningBalances(grant.unit))
            {
                var latest=summaries.Where(d=>d.Kind=="Знімок залишків")
                    .OrderByDescending(d=>d.DocumentDate,StringComparer.Ordinal)
                    .ThenByDescending(d=>d.SavedUtc,StringComparer.Ordinal).FirstOrDefault();
                if(latest is not null)Post(latest,true);
            }
            if(policy.canViewDocuments)
                foreach(var doc in summaries.Where(d=>d.Kind=="Накладна" && d.PostingStatus is not ("posted" or "superseded"))
                    .OrderBy(d=>d.DocumentDate,StringComparer.Ordinal).ThenBy(d=>d.DocumentId,StringComparer.Ordinal))Post(doc,false);
        }
        return new(openings,invoices,issues);

        void Post(ArchivedDocumentSummary summary,bool opening)
        {
            try
            {
                var stored=archive.FindDocument(summary.DocumentId)??throw new IOException("Документ не прочитано з архіву.");
                // Revalidate the current grant, recipient and current invoice version
                // inside PostDocument, including after an access-policy change.
                if(accessStore.PostDocument(stored.Json,manual))
                {if(opening)openings++;else invoices++;}
            }
            catch(Exception ex) when(ex is InvalidOperationException or InvalidDataException or ArgumentException)
            {
                issues.Add(summary.Unit+" · "+summary.Number+": "+ex.Message);
            }
        }
    }
}
