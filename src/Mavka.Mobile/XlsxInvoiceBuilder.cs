using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Mavka.Mobile;

/// <summary>
/// Creates a real Office Open XML workbook with typed numeric cells.
/// Does not encrypt XLSX. The caller must not expose it as a protected export
/// until native Office document encryption is integrated and tested.
/// </summary>
internal static class XlsxInvoiceBuilder
{
    private const string Ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] Create(IncomingInvoice invoice)
    {
        if (invoice.Positions.Count is < 1 or > 10000)
            throw new ArgumentException("Unsupported document size.");

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            Text(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            Text(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="utf-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Text(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="utf-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            Text(zip, "xl/workbook.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Накладна" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Text(zip, "xl/styles.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font>
                    <font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
                  <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
                  <borders count="1"><border/></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="4">
                    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                    <xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
                    <xf numFmtId="4" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
                    <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
                  </cellXfs>
                </styleSheet>
                """);
            var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var xml = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                CloseOutput = false,
                Indent = false
            });
            xml.WriteStartDocument();
            xml.WriteStartElement("worksheet", Ss);
            xml.WriteStartElement("sheetViews", Ss);
            xml.WriteStartElement("sheetView", Ss);
            xml.WriteAttributeString("workbookViewId", "0");
            xml.WriteStartElement("pane", Ss);
            xml.WriteAttributeString("ySplit", "6");
            xml.WriteAttributeString("topLeftCell", "A7");
            xml.WriteAttributeString("state", "frozen");
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteStartElement("cols", Ss);
            foreach (var (index, width) in new[] {22d, 38d, 19d, 12d, 15d, 14d, 16d, 18d}
                .Select((v, i) => (i + 1, v)))
            {
                xml.WriteStartElement("col", Ss);
                xml.WriteAttributeString("min", index.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("max", index.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("width", width.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("customWidth", "1");
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
            xml.WriteStartElement("sheetData", Ss);
            StartRow(xml, 1);
            Inline(xml, "A1", "МАВКА — НАКЛАДНА", 3);
            EndRow(xml);
            StartRow(xml, 2);
            Inline(xml, "A2", "Номер документа", 3);
            Inline(xml, "B2", invoice.Number);
            EndRow(xml);
            StartRow(xml, 3);
            Inline(xml, "A3", "Дата документа", 3);
            var date = LedgerBalanceCalculator.ParseDate(invoice.Date);
            Numeric(xml, "B3", date.ToDateTime(TimeOnly.MinValue).ToOADate(), 1);
            EndRow(xml);
            StartRow(xml, 4);
            Inline(xml, "A4", "Підрозділ", 3);
            Inline(xml, "B4", invoice.Recipient);
            EndRow(xml);
            StartRow(xml, 6);
            string[] headers = ["№", "Найменування", "NSN", "Од. вим.",
                                "Дата", "Кількість", "Ціна", "Вартість"];
            for (int i = 0; i < headers.Length; i++)
                Inline(xml, (char)('A' + i) + "6", headers[i], 3);
            EndRow(xml);
            decimal sumKnown = 0m;
            int row = 7;
            foreach (var item in invoice.Positions)
            {
                StartRow(xml, row);
                Numeric(xml, "A" + row, item.LineNo);
                Inline(xml, "B" + row, item.Name);
                Inline(xml, "C" + row, item.Nsn ?? "");
                Inline(xml, "D" + row, item.Unit);
                Numeric(xml, "E" + row, date.ToDateTime(TimeOnly.MinValue).ToOADate(), 1);
                Numeric(xml, "F" + row, item.Quantity);
                if (item.UnitPrice is decimal price)
                {
                    Numeric(xml, "G" + row, price, 2);
                    Numeric(xml, "H" + row, item.Quantity * price, 2);
                    sumKnown += item.Quantity * price;
                }
                EndRow(xml);
                row++;
            }
            StartRow(xml, row);
            Inline(xml, "B" + row, "Разом (позиції з відомою ціною)", 3);
            Numeric(xml, "H" + row, sumKnown, 2);
            EndRow(xml);
            xml.WriteEndElement();
            xml.WriteStartElement("autoFilter", Ss);
            xml.WriteAttributeString("ref", "A6:H" + (row - 1));
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }
        return buffer.ToArray();
    }

    private static void Text(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void StartRow(XmlWriter xml, int number)
    {
        xml.WriteStartElement("row", Ss);
        xml.WriteAttributeString("r", number.ToString(CultureInfo.InvariantCulture));
    }

    private static void EndRow(XmlWriter xml) => xml.WriteEndElement();

    private static void Inline(XmlWriter xml, string reference, string value, int style = 0)
    {
        xml.WriteStartElement("c", Ss);
        xml.WriteAttributeString("r", reference);
        xml.WriteAttributeString("t", "inlineStr");
        if (style > 0) xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        xml.WriteStartElement("is", Ss);
        xml.WriteElementString("t", Ss, value);
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static void Numeric(XmlWriter xml, string reference, decimal value, int style = 0)
        => Number(xml, reference, value.ToString(CultureInfo.InvariantCulture), style);

    private static void Numeric(XmlWriter xml, string reference, int value, int style = 0)
        => Number(xml, reference, value.ToString(CultureInfo.InvariantCulture), style);

    private static void Numeric(XmlWriter xml, string reference, double value, int style = 0)
        => Number(xml, reference, value.ToString("R", CultureInfo.InvariantCulture), style);

    private static void Number(XmlWriter xml, string reference, string value, int style)
    {
        xml.WriteStartElement("c", Ss);
        xml.WriteAttributeString("r", reference);
        if (style > 0) xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        xml.WriteElementString("v", Ss, value);
        xml.WriteEndElement();
    }
}
