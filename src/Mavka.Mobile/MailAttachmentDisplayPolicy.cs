namespace Mavka.Mobile;

/// <summary>
/// Safe metadata-only classification for attachments that are not imported by
/// the validated TXT/JSON document pipeline. No binary decoding, execution,
/// import, or automatic mailbox cleanup is authorized by this classifier.
/// </summary>
internal static class MailAttachmentDisplayPolicy
{
    internal enum PreviewType { None, Pdf, Excel }

    public static PreviewType Detect(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 240 ||
            fileName.IndexOfAny(['/', '\\', '\0']) >= 0)
            return PreviewType.None;
        if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return PreviewType.Pdf;
        if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return PreviewType.Excel;
        return PreviewType.None;
    }

    public static string Description(string fileName, long size)
    {
        var kind = Detect(fileName);
        if (kind == PreviewType.None)
            throw new ArgumentException("Not a recognized document preview.", nameof(fileName));
        var type = kind == PreviewType.Pdf ? "PDF" : "Excel XLSX";
        var amount = size > 0 ? $" · {size / 1024d:0.#} КБ" : "";
        return $"{type}{amount}\n" +
            "Назву вкладення прочитано з Gmail. Вміст не відкрито й не збережено. " +
            "Зашифрований обмін цим форматом ще не підтримується; " +
            "лист залишається у вхідних.";
    }
}
