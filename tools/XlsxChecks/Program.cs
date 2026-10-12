using Mavka.Mobile;
using System.IO.Compression;
using System.Xml.Linq;

var rows = Enumerable.Range(1,200).Select(i=>
    new IncomingPosition(i,"Тестова канцелярія " + i,"шт.",i,
        "TEST-" + i,i % 2 == 0 ? 1.25m : null)).ToList();
var invoice = new IncomingInvoice("TEST-DOC-1","ТЕСТ-200",
    "Тестова частина","2026-10-11",rows);
var bytes = XlsxInvoiceBuilder.Create(invoice);
if(bytes.Length < 1000) throw new Exception("Output too small");
using var buffer = new MemoryStream(bytes);
using var zip = new ZipArchive(buffer,ZipArchiveMode.Read);
foreach(var name in new[]{"[Content_Types].xml","_rels/.rels",
    "xl/workbook.xml","xl/_rels/workbook.xml.rels","xl/styles.xml",
    "xl/worksheets/sheet1.xml"})
    if(zip.GetEntry(name) is null) throw new Exception("Missing: " + name);
using var sheetStream=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
var x=XDocument.Load(sheetStream);
XNamespace s="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
var data=x.Descendants(s+"row").ToList();
if(data.Count!=206) throw new Exception("Row count wrong: "+data.Count);
if(x.Descendants(s+"autoFilter").Single().Attribute("ref")?.Value!="A6:H206")
    throw new Exception("Autofilter wrong");
var amount=x.Descendants(s+"c").Single(c=>c.Attribute("r")?.Value=="F7");
if(amount.Element(s+"v")?.Value!="1")throw new Exception("Quantity not numeric");
var priceUnknown=x.Descendants(s+"c").Any(c=>c.Attribute("r")?.Value=="G7");
if(priceUnknown)throw new Exception("Unknown price exported as numeric zero");
var amountKnown=x.Descendants(s+"c").Single(c=>c.Attribute("r")?.Value=="H8");
if(amountKnown.Element(s+"v")?.Value!="2.50")throw new Exception("Known value incorrect");
if(!x.ToString().Contains("Тестова канцелярія"))
    throw new Exception("Cyrillic missing");
if(x.Descendants(s+"f").Any())throw new Exception("Unexpected formulas");
var wordBytes = DocxInvoiceBuilder.Create(invoice);
if(wordBytes.Length < 1000) throw new Exception("DOCX unexpectedly small");
using var wordStream = new MemoryStream(wordBytes);
using var wordZip = new ZipArchive(wordStream, ZipArchiveMode.Read);
foreach (var name in new[]{ "[Content_Types].xml", "_rels/.rels", "word/document.xml" })
    if (wordZip.GetEntry(name) is null) throw new Exception("Missing Word part: " + name);
using var documentStream = wordZip.GetEntry("word/document.xml")!.Open();
var wordDoc = XDocument.Load(documentStream);
XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
var docRows = wordDoc.Descendants(w + "tr").ToList();
if (docRows.Count != 202) throw new Exception("DOCX header/200 items/total row mismatch: " + docRows.Count);
if (!wordDoc.ToString().Contains("ТЕСТ-200") ||
    !wordDoc.ToString().Contains("Тестова канцелярія 200") ||
    !wordDoc.ToString().Contains("Тестова частина"))
    throw new Exception("Cyrillic or last item missing in DOCX");
if (wordDoc.Descendants(w + "pgSz").Single()
    .Attribute(w + "orient")?.Value != "landscape")
    throw new Exception("DOCX layout must be landscape");
Console.WriteLine("XLSX and DOCX 200-position Office generation: PASS");
