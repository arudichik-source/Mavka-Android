using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Mavka.Mobile;

/// <summary>
/// Creates a standards-compliant DOCX from an archived invoice. This method
/// returns bytes in memory only; never write its plaintext result to downloads.
/// A caller must encrypt it for transport with the approved exchange key.
/// </summary>
internal static class DocxInvoiceBuilder
{
    private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly UTF8Encoding Utf8 = new(false);

    public static byte[] Create(IncomingInvoice invoice)
    {
        if (invoice.Positions.Count is < 1 or > 200)
            throw new ArgumentException("Накладна містить недопустиму кількість позицій.");
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Text(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Text(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="utf-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            using var entry = zip.CreateEntry("word/document.xml", CompressionLevel.Optimal).Open();
            using var xml = XmlWriter.Create(entry, new XmlWriterSettings
            {
                Encoding = Utf8, CloseOutput = false, Indent = false
            });
            xml.WriteStartDocument();
            xml.WriteStartElement("w", "document", Word);
            xml.WriteStartElement("w", "body", Word);
            Paragraph(xml, "МАВКА — НАКЛАДНА");
            Paragraph(xml, "№ " + invoice.Number);
            Paragraph(xml, "Дата: " +
                LedgerBalanceCalculator.ParseDate(invoice.Date).ToString("dd.MM.yyyy",
                    CultureInfo.InvariantCulture));
            Paragraph(xml, "Підрозділ: " + invoice.Recipient);
            xml.WriteStartElement("w", "tbl", Word);
            Row(xml, new[] { "№", "Найменування", "NSN", "Од.", "Кількість", "Ціна", "Вартість" });
            decimal sum = 0m;
            foreach (var item in invoice.Positions)
            {
                var known = item.UnitPrice.HasValue;
                var total = known ? item.Quantity * item.UnitPrice!.Value : 0m;
                if (known) sum = checked(sum + total);
                Row(xml, new[]
                {
                    item.LineNo.ToString(CultureInfo.InvariantCulture),
                    item.Name, item.Nsn ?? "", item.Unit,
                    item.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
                    known ? item.UnitPrice!.Value.ToString("0.00", CultureInfo.InvariantCulture) : "—",
                    known ? total.ToString("0.00", CultureInfo.InvariantCulture) : "—"
                });
            }
            Row(xml, new[] { "", "Разом (відомі ціни)", "", "", "", "",
                sum.ToString("0.00", CultureInfo.InvariantCulture) });
            xml.WriteEndElement(); // table
            xml.WriteStartElement("w", "sectPr", Word);
            xml.WriteStartElement("w", "pgSz", Word);
            xml.WriteAttributeString("w", "w", Word, "16838");
            xml.WriteAttributeString("w", "h", Word, "11906");
            xml.WriteAttributeString("w", "orient", Word, "landscape");
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement(); // body
            xml.WriteEndElement(); // document
            xml.WriteEndDocument();
        }
        return stream.ToArray();
    }

    private static void Row(XmlWriter xml, IEnumerable<string> cells)
    {
        xml.WriteStartElement("w", "tr", Word);
        foreach (var cell in cells)
        {
            xml.WriteStartElement("w", "tc", Word);
            Paragraph(xml, cell);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static void Paragraph(XmlWriter xml, string text)
    {
        xml.WriteStartElement("w", "p", Word);
        xml.WriteStartElement("w", "r", Word);
        xml.WriteStartElement("w", "t", Word);
        xml.WriteAttributeString("xml", "space",
            "http://www.w3.org/XML/1998/namespace", "preserve");
        xml.WriteString(text);
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static void Text(ZipArchive zip, string path, string xml)
    {
        using var writer = new StreamWriter(
            zip.CreateEntry(path, CompressionLevel.Optimal).Open(), Utf8);
        writer.Write(xml);
    }
}
