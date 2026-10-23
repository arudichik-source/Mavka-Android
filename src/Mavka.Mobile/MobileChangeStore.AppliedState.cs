using System.Globalization;
namespace Mavka.Mobile;
internal sealed record MobileAppliedState(long Sequence,DateOnly? AsOf);
internal sealed partial class MobileChangeStore
{
    internal MobileAppliedState? AppliedState()
    {
        using var db=ReadableDatabase;
        using var row=db.RawQuery("SELECT sequence,asof FROM heads WHERE client=? ORDER BY rowid DESC LIMIT 1",new[]{Client});
        if(!row.MoveToFirst())return null;
        return new(row.GetLong(0),DateOnly.TryParseExact(row.GetString(1),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:null);
    }
}
