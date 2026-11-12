namespace Mavka.Mobile;

/// <summary>
/// Pure, device-independent vertical layout cursor for paginated text.
/// Each baseline is guaranteed to be at or above the printable bottom.
/// UI rendering decides when to start a new page.
/// </summary>
internal sealed class PdfPageFlow
{
    private readonly float _bottom;
    private readonly float _lineHeight;
    public float NextBaseline { get; private set; }
    public int PageNumber { get; private set; }

    public PdfPageFlow(float printableBottom, float lineHeight)
    {
        if (!float.IsFinite(printableBottom) || printableBottom <= 0 ||
            !float.IsFinite(lineHeight) || lineHeight <= 0 ||
            lineHeight >= printableBottom)
            throw new ArgumentOutOfRangeException(nameof(printableBottom));
        _bottom = printableBottom;
        _lineHeight = lineHeight;
    }

    public void BeginPage(float firstBaseline)
    {
        if (!float.IsFinite(firstBaseline) || firstBaseline < 0 ||
            firstBaseline + _lineHeight > _bottom)
            throw new ArgumentOutOfRangeException(nameof(firstBaseline));
        PageNumber++;
        NextBaseline = firstBaseline;
    }

    public bool CanWriteLine =>
        PageNumber > 0 && NextBaseline + _lineHeight <= _bottom;

    public float TakeLine()
    {
        if (!CanWriteLine)
            throw new InvalidOperationException("Text does not fit on the page.");
        var at = NextBaseline;
        NextBaseline += _lineHeight;
        return at;
    }

    public void AddSpacing(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (PageNumber == 0)
            throw new InvalidOperationException("No active PDF page.");
        NextBaseline += amount;
    }
}
