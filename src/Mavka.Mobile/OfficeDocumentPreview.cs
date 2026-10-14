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

    public static string Read(string filename, byte[] bytes)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (ext != ".docx" && ext != ".xlsx")
            throw new NotSupportedException("Цей формат потребує сумісного зовнішнього переглядача.");
        using var input = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, true);
        var paths = ext == ".docx"
            ? new[] { "word/document.xml" }
            : zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName).OrderBy(s => s, StringComparer.Ordinal).Take(20).ToArray();
        if (paths.Length == 0) throw new InvalidDataException("Не знайдено вміст документа.");
        var text = new StringBuilder();
        foreach (var path in paths)
        {
            var entry = zip.GetEntry(path);
            if (entry is null || entry.Length > MaxXmlBytes)
                throw new InvalidDataException("Документ завеликий для вбудованого перегляду.");
            if (ext == ".xlsx") text.AppendLine("— " + path + " —");
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaxXmlBytes, IgnoreComments = true
            });
            while (reader.Read() && text.Length < MaxCharacters)
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (ext == ".docx" && reader.LocalName == "p") text.AppendLine();
                if (ext == ".xlsx" && reader.LocalName == "row") text.AppendLine();
                if (reader.LocalName != "t" && reader.LocalName != "v") continue;
                var value = reader.ReadElementContentAsString();
                if (value.Length > MaxCharacters - text.Length)
                    value = value[..(MaxCharacters - text.Length)];
                text.Append(value);
                if (ext == ".xlsx") text.Append("  ");
            }
            if (text.Length >= MaxCharacters) break;
        }
        if (text.Length == 0) return "Текстового вмісту немає.";
        return text.ToString();
    }
}
