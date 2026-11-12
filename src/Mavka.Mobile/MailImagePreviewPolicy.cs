namespace Mavka.Mobile;

/// <summary>Bounded, in-memory photo preview from an explicitly authorized Gmail sender.</summary>
internal static class MailImagePreviewPolicy
{
    public const int MaxBytes = 2_000_000;
    public static bool IsImage(string? name) =>
        name?.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == true ||
        name?.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) == true ||
        name?.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) == true ||
        name?.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) == true;

    public static bool HasValidSignature(string name, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12 || payload.Length > MaxBytes) return false;
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return payload.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            return payload[0] == 0xff && payload[1] == 0xd8 &&
                payload[2] == 0xff;
        if (name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            return payload.Slice(0, 4).SequenceEqual("RIFF"u8) &&
                payload.Slice(8, 4).SequenceEqual("WEBP"u8);
        return false;
    }
}
