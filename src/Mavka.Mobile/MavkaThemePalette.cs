using Android.Content;
using Android.Graphics;

namespace Mavka.Mobile;

/// <summary>
/// Two complete palettes for native Android views. Icon artwork and semantic
/// warning color remain unchanged; only interface chrome/control surfaces
/// adapt to the user-selected appearance.
/// </summary>
internal sealed class MavkaThemePalette
{
    private const string Preferences = "mavka_display_preferences";
    private const string ThemeKey = "color_theme";

    public string Id { get; }
    public bool IsBlue => Id == MavkaThemePolicy.Blue;
    public string Label => MavkaThemePolicy.Label(Id);

    private MavkaThemePalette(string id) => Id = MavkaThemePolicy.Normalize(id);

    public static MavkaThemePalette Load(Context context)
    {
        var stored = context.GetSharedPreferences(Preferences, FileCreationMode.Private)?
            .GetString(ThemeKey, MavkaThemePolicy.Olive);
        return new MavkaThemePalette(MavkaThemePolicy.Normalize(stored));
    }

    public static void Save(Context context, string id)
    {
        string normalized = MavkaThemePolicy.Normalize(id);
        var prefs = context.GetSharedPreferences(Preferences, FileCreationMode.Private) ??
            throw new InvalidOperationException("Не вдалося зберегти тему оформлення.");
        prefs.Edit()!.PutString(ThemeKey, normalized)!.Apply();
    }

    private Color Choose(Color olive, Color blue) => IsBlue ? blue : olive;

    public Color Header => Choose(Color.Rgb(13, 55, 36), Color.Rgb(21, 58, 111));
    public Color HeaderButton => Choose(Color.Rgb(25, 82, 53), Color.Rgb(37, 80, 145));
    public Color Surface => Choose(Color.Rgb(249, 247, 242), Color.Rgb(241, 246, 254));
    public Color Card => Color.White;
    public Color CardOutline => Choose(Color.Rgb(238, 235, 227), Color.Rgb(220, 232, 249));
    public Color Input => Choose(Color.Rgb(251, 250, 245), Color.Rgb(249, 251, 255));
    public Color InputOutline => Choose(Color.Rgb(219, 227, 218), Color.Rgb(199, 217, 242));
    public Color Heading => Choose(Color.Rgb(20, 47, 33), Color.Rgb(23, 51, 89));
    public Color BodyText => Choose(Color.Rgb(92, 100, 94), Color.Rgb(85, 99, 120));
    public Color StrongText => Choose(Color.Rgb(15, 47, 32), Color.Rgb(21, 53, 94));
    public Color AccentText => Choose(Color.Rgb(21, 69, 44), Color.Rgb(29, 77, 143));
    public Color PrimaryAction => Choose(Color.Rgb(9, 83, 50), Color.Rgb(22, 91, 171));
    public Color FilterAction => Choose(Color.Rgb(9, 84, 50), Color.Rgb(22, 91, 171));
    public Color FilterText => Choose(Color.Rgb(31, 73, 47), Color.Rgb(31, 76, 139));
    public Color SoftAction => Choose(Color.Rgb(230, 240, 231), Color.Rgb(225, 237, 254));
    public Color SoftFilter => Choose(Color.Rgb(235, 240, 235), Color.Rgb(226, 237, 252));
    public Color MutedAction => Choose(Color.Rgb(241, 241, 238), Color.Rgb(239, 244, 251));
    public Color MutedText => Choose(Color.Rgb(126, 135, 128), Color.Rgb(132, 145, 166));
    public Color InputSearch => Choose(Color.Rgb(248, 248, 245), Color.Rgb(247, 250, 255));
    public Color InputSearchOutline => Choose(Color.Rgb(234, 236, 229), Color.Rgb(206, 222, 243));
    public Color ListRow => Choose(Color.Rgb(250, 249, 245), Color.Rgb(248, 251, 255));
    public Color PositiveIcon => Choose(Color.Rgb(32, 91, 59), Color.Rgb(31, 81, 150));
    public Color PositiveIconFill => Choose(Color.Rgb(231, 242, 233), Color.Rgb(228, 239, 254));
    public Color NavigationArrow => Choose(Color.Rgb(48, 81, 58), Color.Rgb(45, 79, 129));
    public Color NavigationSelected => Choose(Color.Rgb(11, 78, 47), Color.Rgb(27, 75, 147));
    public Color NavigationMuted => Choose(Color.Rgb(105, 112, 106), Color.Rgb(102, 116, 139));
    public Color NavigationFill => Choose(Color.Rgb(230, 241, 231), Color.Rgb(225, 237, 254));
    public Color AccentMuted => Choose(Color.Rgb(48, 81, 58), Color.Rgb(45, 79, 129));
}
