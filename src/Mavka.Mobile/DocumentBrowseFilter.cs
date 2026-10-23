using System.Globalization;

namespace Mavka.Mobile;

// Read-only presentation filter. Authorization is checked separately on every
// render and again when opening a captured row; a filter never grants access.
internal sealed record DocumentBrowseFilter(string Type = "", string Status = "", DateOnly? From = null, DateOnly? To = null)
{
    public bool Active => Type.Length > 0 || Status.Length > 0 || From is not null || To is not null;
    public bool Matches(string type, string status, string date)
    {
        if (Type.Length > 0 && !string.Equals(Type, type, StringComparison.OrdinalIgnoreCase) ||
            Status.Length > 0 && !string.Equals(Status, status, StringComparison.OrdinalIgnoreCase)) return false;
        if (From is null && To is null) return true;
        return TryDate(date, out var day) && (From is null || day >= From) && (To is null || day <= To);
    }
    internal static bool TryDate(string value, out DateOnly date) => DateOnly.TryParseExact(value,
        new[] { "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    public string Summary => !Active ? "Без додаткових обмежень" : string.Join(" · ", new[] {
        Type, Status, From is {} from ? "з " + from.ToString("dd.MM.yyyy") : "",
        To is {} to ? "до " + to.ToString("dd.MM.yyyy") : "" }.Where(s => s.Length > 0));
}
