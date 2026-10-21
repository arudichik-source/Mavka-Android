namespace Mavka.Mobile;

// Pure validation used by the encrypted profile store and user-interface.
internal static class PhoneNumberNormalizer
{
    public static string Normalize(string? input)
    {
        var value = (input ?? "").Trim();
        if (value.Length == 0) return "";
        // Accept visual separators, but never letters, extensions or control characters.
        if (value.Any(c => !(char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')')))
            throw new ArgumentException("Номер телефону містить неприпустимі символи.");
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (value.StartsWith('+'))
        {
            if (value.Count(c => c == '+') != 1 || value[0] != '+')
                throw new ArgumentException("Некоректний міжнародний номер.");
        }
        else if (value.Contains('+'))
            throw new ArgumentException("Некоректний міжнародний номер.");

        // Ukrainian local and international formats.
        if (!value.StartsWith('+') && digits.Length == 10 && digits.StartsWith('0'))
            digits = "38" + digits;
        if (!value.StartsWith('+') && digits.Length == 12 && digits.StartsWith("380"))
            return "+" + digits;
        if (!value.StartsWith('+') && digits.StartsWith("00"))
            digits = digits[2..];
        if (digits.Length is < 8 or > 15 || digits[0] == '0')
            throw new ArgumentException("Номер телефону має містити 8–15 цифр у міжнародному форматі.");
        return "+" + digits;
    }
}
