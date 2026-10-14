using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Mavka.Mobile;

/// <summary>
/// Bounded, read-only Office text preview entirely in application memory.
/// Never writes decrypted Office data to Downloads, cache or a shared provider.
/// The original vault bytes and accounting data remain untouched.
/// </summary>
internal static class OfficeDocumentPreview
{
    private const int MaxCharacters = 16000;
    private const long MaxXmlBytes = 2_000_000;
    private const int MaxSharedStrings = 10000;
    private const int MaxSharedCharacters = 80000;

    public static string Read(string filename, byte[] bytes)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (ext != ".docx" && ext != ".xlsx")
            throw new NotSupportedException("Цей формат потребує сумісного зовнішнього переглядача.");
        using var input = new MemoryStream(bytes, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var paths = ext == ".docx"
            ? new[] { "word/document.xml" }
            : zip.Entries.Where(e =>
                    e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName).OrderBy(s => s, StringComparer.Ordinal)
                .Take(20).ToArray();
        if (paths.Length == 0) throw new InvalidDataException("Не знайдено вміст документа.");

        var shared = ext == ".xlsx" ? ReadSharedStrings(zip) : Array.Empty<string>();
        var text = new StringBuilder();
        foreach (var path in paths)
        {
            var entry = zip.GetEntry(path);
            if (entry is null || entry.Length > MaxXmlBytes)
                throw new InvalidDataException("Документ завеликий для вбудованого перегляду.");
            if (ext == ".xlsx") Append(text, "— " + path + " —\n");
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlSettings());
            string? cellType = null;
            while (reader.Read() && text.Length < MaxCharacters)
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (ext == ".docx")
                {
                    if (reader.LocalName is "p" or "br") Append(text, "\n");
                    if (reader.LocalName == "tab") Append(text, "\t");
                    if (reader.LocalName == "t")
                        Append(text, reader.ReadElementContentAsString());
                    continue;
                }

                if (reader.LocalName == "row") Append(text, "\n");
                if (reader.LocalName == "c")
                {
                    cellType = reader.GetAttribute("t");
                    continue;
                }
                if (reader.LocalName == "v")
                {
                    string raw = reader.ReadElementContentAsString();
                    if (cellType == "s")
                    {
                        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture,
                                out var index) || index < 0 || index >= shared.Length)
                            throw new InvalidDataException("Некоректний індекс спільного рядка XLSX.");
                        raw = shared[index];
                    }
                    Append(text, raw + "\t");
                }
                else if (reader.LocalName == "t" && cellType == "inlineStr")
                    Append(text, reader.ReadElementContentAsString() + "\t");
            }
            if (text.Length >= MaxCharacters) break;
        }
        return text.Length == 0 ? "Текстового вмісту немає." : text.ToString();
    }

    private static string[] ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return Array.Empty<string>();
        if (entry.Length > MaxXmlBytes)
            throw new InvalidDataException("Таблиця рядків XLSX завелика.");
        var result = new List<string>();
        var totalCharacters = 0;
        StringBuilder? item = null;
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, SecureXmlSettings());
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
            {
                if (result.Count >= MaxSharedStrings)
                    throw new InvalidDataException("Перевищено межу рядків XLSX.");
                item = new StringBuilder();
            }
            else if (reader.NodeType == XmlNodeType.Element &&
                     reader.LocalName == "t" && item is not null)
            {
                var value = reader.ReadElementContentAsString();
                totalCharacters += value.Length;
                if (totalCharacters > MaxSharedCharacters)
                    throw new InvalidDataException("Перевищено межу текстового вмісту XLSX.");
                item.Append(value);
            }
            else if (reader.NodeType == XmlNodeType.EndElement &&
                     reader.LocalName == "si" && item is not null)
            {
                result.Add(item.ToString());
                item = null;
            }
        }
        return result.ToArray();
    }

    private static XmlReaderSettings SecureXmlSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxXmlBytes,
        IgnoreComments = true
    };

    private static void Append(StringBuilder output, string value)
    {
        if (output.Length >= MaxCharacters) return;
        if (value.Length > MaxCharacters - output.Length)
            value = value[..(MaxCharacters - output.Length)];
        output.Append(value);
    }
}
