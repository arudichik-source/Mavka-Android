using System.Text.RegularExpressions;

namespace Mavka.Mobile;

// Stable semantic categories. Presentation assets must not be selected by
// item codes, quantities or prices, only by the nomenclature name.
internal enum AssetIconKind
{
    Unknown,
    Shell,
    Charge,
    Round,
    Primer,
    Rocket,
    Fuze,
    TwelveGauge,
    Cartridge
}

internal static class AssetIconClassifier
{
    private static readonly Regex Gauge12 = new(
        @"(?<![\p{L}\p{N}])12\s*(?:(?:[xх×]\s*70)(?!\d)|(?:[-\s]*калібр\p{L}*)|(?:[-\s]*мм)(?![\p{L}\p{N}]))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly (Regex Rule, AssetIconKind Category)[] Rules =
    [
        (Token(@"реактивн\p{L}*"), AssetIconKind.Rocket),
        (Token(@"підривник\p{L}*"), AssetIconKind.Fuze),
        (Token(@"праймер\p{L}*"), AssetIconKind.Primer),
        (Token(@"постріл\p{L}*"), AssetIconKind.Round),
        (Token(@"снаряд\p{L}*"), AssetIconKind.Shell),
        (Token(@"заряд\p{L}*"), AssetIconKind.Charge),
        (Token(@"патрон\p{L}*"), AssetIconKind.Cartridge)
    ];

    private static Regex Token(string fragment) => new(
        @"(?<!\p{L})(?:" + fragment + @")(?!\p{L})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static AssetIconKind Classify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return AssetIconKind.Unknown;

        // Critical exception: a 12-gauge cartridge must NOT use the generic
        // "Cartridge" icon; and "12,7 мм" must NOT count as "12 мм".
        if (Gauge12.IsMatch(name)) return AssetIconKind.TwelveGauge;
        foreach (var (rule, category) in Rules)
            if (rule.IsMatch(name)) return category;

        return AssetIconKind.Unknown;
    }
}
