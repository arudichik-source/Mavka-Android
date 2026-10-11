using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Android.Graphics;
using Android.Graphics.Pdf;
using PdfSharp.Pdf.IO;

namespace Mavka.Mobile;

/// <summary>
/// Builds a paginated, Cyrillic-capable Android PDF and encrypts it in memory
/// with format-native PDF AES-256. The plaintext is never written to Downloads,
/// a temporary file, diagnostic logs or the device cache.
/// </summary>
internal static class ProtectedPdfBuilder
{
    public static byte[] CreateInvoice(IncomingInvoice invoice, string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 128)
            throw new ArgumentException("Пароль PDF: від 12 до 128 символів.");
        if (invoice.Positions.Count is < 1 or > 200)
            throw new ArgumentException("Неприпустима кількість позицій накладної.");

        using var plaintext = new MemoryStream();
        try
        {
            DrawInvoice(invoice, plaintext);
            plaintext.Position = 0;

            using var protectedPdf = PdfReader.Open(
                plaintext, PdfDocumentOpenMode.Modify);
            protectedPdf.SecuritySettings.UserPassword = password;
            // A random owner password prevents a blank owner-password escape.
            protectedPdf.SecuritySettings.OwnerPassword =
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            protectedPdf.SecurityHandler.SetEncryptionToV5();

            using var encrypted = new MemoryStream();
            protectedPdf.Save(encrypted, false);
            var result = encrypted.ToArray();
            if (result.Length < 64 ||
                !System.Text.Encoding.ASCII.GetString(result, 0, 5)
                    .Equals("%PDF-", StringComparison.Ordinal))
                throw new InvalidOperationException("Encrypted PDF could not be generated.");
            return result;
        }
        finally
        {
            // Memory-only clear of the original PDF bytes, even if encryption fails.
            if (plaintext.TryGetBuffer(out var segment))
                CryptographicOperations.ZeroMemory(
                    segment.AsSpan(0, (int)plaintext.Length));
        }
    }

    private static void DrawInvoice(IncomingInvoice invoice, Stream target)
    {
        const int pageWidth = 595, pageHeight = 842;
        const float margin = 35, bottom = 805, line = 17;
        using var document = new Android.Graphics.Pdf.PdfDocument();
        using var body = new Paint(PaintFlags.AntiAlias);
        using var bold = new Paint(PaintFlags.AntiAlias);
        using var small = new Paint(PaintFlags.AntiAlias);
        body.Color = Color.Rgb(25, 37, 30);
        body.TextSize = 12;
        body.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal));
        bold.Color = Color.Rgb(14, 64, 39);
        bold.TextSize = 15;
        bold.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Bold));
        small.Color = Color.Rgb(50, 67, 54);
        small.TextSize = 10;
        small.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal));

        Android.Graphics.Pdf.PdfDocument.Page? active = null;
        Canvas? canvas = null;
        var pageFlow = new PdfPageFlow(bottom, line);

        void FinishPage()
        {
            if (active is null || canvas is null) return;
            canvas.DrawLine(margin, bottom + 3, pageWidth - margin,
                bottom + 3, small);
            canvas.DrawText("Мавка Mobile • сторінка " + pageFlow.PageNumber,
                margin, bottom + 20, small);
            document.FinishPage(active);
            active = null;
            canvas = null;
        }

        void NewPage()
        {
            FinishPage();
            pageFlow.BeginPage(93);
            var info = new PdfDocument.PageInfo.Builder(
                pageWidth, pageHeight, pageFlow.PageNumber).Create();
            active = document.StartPage(info);
            canvas = active.Canvas;
            canvas!.DrawText("МАВКА MOBILE — ДОКУМЕНТ", margin, 55, bold);
            canvas.DrawLine(margin, 71, pageWidth - margin, 71, small);
        }

        void Paragraph(string text, Paint paint, float spacingAfter = 9)
        {
            var lines = WrapText(paint, text, pageWidth - 2 * margin);
            foreach (var row in lines)
            {
                // A single paragraph may be longer than an entire page.
                // Pagination must happen between individual lines, rather
                // than checking the whole paragraph just once.
                if (!pageFlow.CanWriteLine)
                    NewPage();
                canvas!.DrawText(row, margin, pageFlow.TakeLine(), paint);
            }
            pageFlow.AddSpacing(spacingAfter);
        }

        NewPage();
        Paragraph("Номер: " + invoice.Number, bold, 4);
        Paragraph("Дата: " + invoice.Date, body, 4);
        Paragraph("Отримувач: " + invoice.Recipient, body, 14);
        decimal knownValue = 0;
        int unknownPrices = 0;
        foreach (var item in invoice.Positions)
        {
            Paragraph(item.LineNo + ". " + item.Name, body, 3);
            if (!string.IsNullOrWhiteSpace(item.Nsn))
                Paragraph("NSN: " + item.Nsn, small, 2);

            var summary = "Кількість: " + item.Quantity.ToString("0.###", CultureInfo.GetCultureInfo("uk-UA")) +
                " " + item.Unit;
            if (item.UnitPrice.HasValue)
            {
                var value = item.Quantity * item.UnitPrice.Value;
                knownValue += value;
                summary += "  |  Ціна: " + item.UnitPrice.Value.ToString("0.00",
                    CultureInfo.GetCultureInfo("uk-UA")) +
                    "  |  Вартість: " + value.ToString("0.00", CultureInfo.GetCultureInfo("uk-UA"));
            }
            else
            {
                unknownPrices++;
                summary += "  |  Ціну не зазначено";
            }
            Paragraph(summary, small, 9);
        }
        Paragraph("Разом за позиціями з відомою ціною: " +
            knownValue.ToString("0.00", CultureInfo.GetCultureInfo("uk-UA")) + " грн",
            bold, 8);
        if (unknownPrices > 0)
            Paragraph("Позицій із невідомою ціною: " + unknownPrices +
                ". Їх вартість не включено до підсумку.", body, 8);
        FinishPage();
        document.WriteTo(target);
    }

    private static List<string> WrapText(Paint paint, string? text, float maxWidth)
    {
        var lines = new List<string>();
        var words = (text ?? "").Replace('\n', ' ').Split(
            ' ', StringSplitOptions.RemoveEmptyEntries);
        string current = "";
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (paint.MeasureText(candidate) <= maxWidth)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0)
            {
                lines.Add(current);
                current = "";
            }
            // A very long word must not escape the page margins.
            // Unicode scalar values keep emoji and supplementary Unicode
            // characters intact. UTF-16 char-by-char splitting corrupts them.
            foreach (var scalar in word.EnumerateRunes())
            {
                var glyph = scalar.ToString();
                var next = current + glyph;
                if (paint.MeasureText(next) > maxWidth && current.Length > 0)
                {
                    lines.Add(current);
                    current = "";
                }
                current += glyph;
            }
        }
        if (current.Length > 0) lines.Add(current);
        if (lines.Count == 0) lines.Add("");
        return lines;
    }
}
