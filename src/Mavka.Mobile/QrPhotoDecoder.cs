using Android.Content;
using Android.Graphics;
using ZXing;
using ZXing.Common;

namespace Mavka.Mobile;

internal static class QrPhotoDecoder
{
    public static string ReadQrFromUri(ContentResolver resolver, Android.Net.Uri uri)
    {
        using var source = resolver.OpenInputStream(uri) ??
            throw new IOException("Фото QR-коду недоступне.");
        using var original = BitmapFactory.DecodeStream(source) ??
            throw new InvalidOperationException("Не вдалося прочитати зображення.");
        return ReadQr(original);
    }

    public static string ReadQr(Bitmap original)
    {
        if (original.Width <= 0 || original.Height <= 0 ||
            original.Width > 12_000 || original.Height > 12_000)
            throw new ArgumentException("Некоректний розмір фото QR-коду.");
        int sample = Math.Max(1, (int)Math.Ceiling(
            Math.Max(original.Width, original.Height) / 1500.0));
        using var resized = Bitmap.CreateScaledBitmap(original,
            Math.Max(1, original.Width / sample),
            Math.Max(1, original.Height / sample), false);
        int width = resized!.Width, height = resized.Height;
        var pixels = new int[checked(width * height)];
        resized.GetPixels(pixels, 0, width, 0, 0, width, height);
        var rgb = new byte[checked(width * height * 3)];
        for (int i = 0; i < pixels.Length; i++)
        {
            int value = pixels[i];
            rgb[3 * i] = (byte)(value >> 16);
            rgb[3 * i + 1] = (byte)(value >> 8);
            rgb[3 * i + 2] = (byte)value;
        }
        var luminance = new RGBLuminanceSource(rgb, width, height,
            RGBLuminanceSource.BitmapFormat.RGB24);
        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions
            {
                TryHarder = true, PossibleFormats = new[] { BarcodeFormat.QR_CODE }
            }
        };
        var result = reader.Decode(luminance);
        if (string.IsNullOrWhiteSpace(result?.Text) ||
            result.Text.Length > 1400)
            throw new InvalidOperationException(
                "QR-код Мавки не знайдено. Сфотографуй його ближче або вибери чітке зображення.");
        return result.Text;
    }
}
