namespace Mavka.Mobile;

/// <summary>
/// Opening balances are a start-of-day baseline. Historical receipts preceding
/// it must never be added later as new inventory, or the baseline double-counts.
/// </summary>
internal static class OpeningBalancePostingPolicy
{
    public static DateOnly ValidateOpeningDate(string date) =>
        LedgerBalanceCalculator.ParseDate(date);

    public static DateOnly ValidateReceiptDate(string invoiceDate, string? openingDate)
    {
        var invoice = LedgerBalanceCalculator.ParseDate(invoiceDate);
        if (openingDate is not null)
        {
            var start = ValidateOpeningDate(openingDate);
            if (invoice < start)
                throw new InvalidOperationException(
                    "Накладна датована раніше початкових залишків. " +
                    "Її не можна повторно зараховувати поверх контрольного знімка; " +
                    "потрібна звірка з ГОЛОВНОЮ МАВКОЮ.");
        }
        return invoice;
    }
}
