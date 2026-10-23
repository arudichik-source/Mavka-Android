namespace Mavka.Mobile;

// One refresh drains small durable portions. Failed/unsupported UIDs are left
// remotely and revisited next refresh, without starving later parts this time.
internal sealed class GmailRefreshSession
{
    internal const int MaximumPasses=128;
    private readonly HashSet<(uint Validity,uint Uid)> _attempted=[];
    internal IReadOnlySet<(uint Validity,uint Uid)> Attempted=>_attempted;
    internal int Passes{get;private set;}
    internal int Messages{get;private set;}
    internal bool AtLimit=>Passes>=MaximumPasses;
    internal bool Register(IEnumerable<(uint Validity,uint Uid)> batch)
    {
        if(AtLimit)return false;
        var fresh=batch.Where(k=>k.Validity>0 && k.Uid>0).Distinct().Where(k=>!_attempted.Contains(k)).ToArray();
        if(fresh.Length==0)return false;
        foreach(var key in fresh)_attempted.Add(key);
        Passes++;Messages+=fresh.Length;return true;
    }
}
