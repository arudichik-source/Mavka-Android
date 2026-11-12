using Mavka.Mobile;
var flow = new PdfPageFlow(805, 17);
var lines = 0;
var pageBreaks = 0;
flow.BeginPage(93);
void Paragraph(int lineCount)
{
    for (var i = 0; i < lineCount; i++)
    {
        if (!flow.CanWriteLine)
        {
            flow.BeginPage(93);
            pageBreaks++;
        }
        var baseline = flow.TakeLine();
        if (baseline < 93 || baseline + 17 > 805)
            throw new Exception("Wrapped line escapes printable page bounds.");
        lines++;
    }
    flow.AddSpacing(9);
}
Paragraph(1000); // One paragraph larger than the whole page.
for (var i = 0; i < 200; i++)
{
    Paragraph(9); // 200 long entries over dozens of pages.
    Paragraph(1);
}
if (lines != 3000 || pageBreaks < 30 || flow.PageNumber != pageBreaks + 1)
    throw new Exception("PDF paging must preserve all 3000 wrapped lines.");

bool rejected = false;
try { while (flow.CanWriteLine) flow.TakeLine(); flow.TakeLine(); }
catch (InvalidOperationException) { rejected = true; }
if (!rejected) throw new Exception("Page cursor accepted an overflowing line.");

rejected = false;
try { flow.AddSpacing(-1); }
catch (ArgumentOutOfRangeException) { rejected = true; }
if (!rejected) throw new Exception("Invalid negative spacing was accepted.");

Console.WriteLine($"PDF text pagination: 3000 lines, {flow.PageNumber} pages, margins PASS");