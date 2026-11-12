namespace Mavka.Mobile;

/// <summary>Stable on-device theme identifiers, independent of Android resources.</summary>
internal static class MavkaThemePolicy
{
    public const string Olive = "olive";
    public const string Blue = "blue";

    public static string Normalize(string? value) =>
        string.Equals(value, Blue, StringComparison.Ordinal) ? Blue : Olive;

    public static string Label(string? value) =>
        Normalize(value) == Blue ? "Синя" : "Оливкова";
}
