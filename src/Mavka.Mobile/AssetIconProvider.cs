using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace Mavka.Mobile;

/// <summary>
/// Actual artwork from the user-approved concept archive. A single WebP atlas
/// is decoded once and individual category tiles are reused across list rows.
/// </summary>
internal sealed class AssetIconProvider
{
    private const int Cell = 112;
    private readonly Context _context;
    private Bitmap? _atlas;
    private readonly Dictionary<AssetIconKind, Bitmap> _cache = [];

    public AssetIconProvider(Context context) => _context = context;

    public View Create(string? nomenclature, int sizeDp = 62)
    {
        var kind = AssetIconClassifier.Classify(nomenclature);
        var sizePx = (int)(sizeDp * _context.Resources!.DisplayMetrics!.Density + 0.5f);
        var bitmap = Get(kind);
        if (bitmap is null)
        {
            var fallback = new TextView(_context)
            {
                Text = "▤", TextSize = 29,
                Gravity = GravityFlags.Center
            };
            fallback.SetTextColor(Color.Rgb(31, 94, 60));
            fallback.Background = Background();
            fallback.LayoutParameters = new LinearLayout.LayoutParams(sizePx, sizePx);
            return fallback;
        }

        var picture = new ImageView(_context);
        picture.SetScaleType(ImageView.ScaleType.FitCenter);
        picture.SetImageBitmap(bitmap);
        picture.ContentDescription = kind.ToString();
        picture.LayoutParameters = new LinearLayout.LayoutParams(sizePx, sizePx);
        return picture;
    }

    private Bitmap? Get(AssetIconKind kind)
    {
        if (kind == AssetIconKind.Unknown) return null;
        if (_cache.TryGetValue(kind, out var found)) return found;

        if (_atlas is null)
        {
            try
            {
                using var stream = _context.Assets!.Open("asset_icons.webp");
                _atlas = BitmapFactory.DecodeStream(stream);
            }
            catch (Java.IO.IOException)
            {
                return null;
            }
        }

        if (_atlas is null || _atlas.Width < 4 * Cell || _atlas.Height < 2 * Cell)
            return null;

        // 4 columns x 2 rows: shell / charge / round / primer /
        // rocket / fuze / 12 gauge / standard cartridge.
        int index = kind switch
        {
            AssetIconKind.Shell => 0,
            AssetIconKind.Charge => 1,
            AssetIconKind.Round => 2,
            AssetIconKind.Primer => 3,
            AssetIconKind.Rocket => 4,
            AssetIconKind.Fuze => 5,
            AssetIconKind.TwelveGauge => 6,
            AssetIconKind.Cartridge => 7,
            _ => -1
        };
        if (index < 0) return null;
        var icon = Bitmap.CreateBitmap(_atlas, (index % 4) * Cell, (index / 4) * Cell,
            Cell, Cell);
        _cache[kind] = icon;
        return icon;
    }

    private GradientDrawable Background()
    {
        var shape = new GradientDrawable();
        shape.SetColor(Color.Rgb(247, 242, 229));
        shape.SetCornerRadius(18 * _context.Resources!.DisplayMetrics!.Density);
        return shape;
    }
}
