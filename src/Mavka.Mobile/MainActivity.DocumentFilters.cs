using Android.App;
using Android.Widget;

namespace Mavka.Mobile;
public partial class MainActivity
{
    private bool _showDocumentArchive;
    private DocumentBrowseFilter _rootDocumentFilter = new(), _archiveDocumentFilter = new();
    private string _rootDocumentQuery = "";
    private sealed record DocumentBrowseRow(MobileMirrorRecord Record, string Type, string Number, string Date, string Unit, string Status);

    private void ShowDocumentFilters(DocumentBrowseFilter current, IEnumerable<string> types,
        IEnumerable<string> statuses, Action<DocumentBrowseFilter> apply)
    {
        var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        body.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        Spinner Choice(string label, IEnumerable<string> values, string selected)
        {
            Text(body, label, 14, true);
            var options = new[] { "Усі" }.Concat(values.Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s)).ToArray();
            var spinner = new Spinner(this);
            var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, options);
            adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            spinner.Adapter = adapter;
            spinner.SetSelection(Math.Max(0, Array.FindIndex(options, s => s == selected)));
            body.AddView(spinner);
            return spinner;
        }
        var type = Choice("Тип документа", types.Append(current.Type), current.Type);
        var status = Choice("Статус", statuses.Append(current.Status), current.Status);
        DateOnly? from = current.From, to = current.To;
        var dates = Text(body, "", 14);
        void Dates() => dates.Text = (from?.ToString("dd.MM.yyyy") ?? "Початок не обмежено") + " — " +
            (to?.ToString("dd.MM.yyyy") ?? "Кінець не обмежено");
        void Pick(bool start)
        {
            var initial = (start ? from : to) ?? DateOnly.FromDateTime(DateTime.Today);
            new DatePickerDialog(this, (_, args) => {
                var value = DateOnly.FromDateTime(args.Date); if (start) from = value; else to = value; Dates();
            }, initial.Year, initial.Month - 1, initial.Day).Show();
        }
        Dates(); Button(body, "Дата від…", () => Pick(true)); Button(body, "Дата до…", () => Pick(false));
        var error = Text(body, "", 13);
        var scroll = new ScrollView(this); scroll.AddView(body);
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle("Фільтри документів"); builder.SetView(scroll);
        builder.SetNegativeButton("Скасувати", (_, _) => { });
        var dialog = builder.Create() ?? throw new InvalidOperationException("Не вдалося відкрити фільтри.");
        Button(body, "Застосувати", () => {
            if (from > to) { error.Text = "Дата початку має передувати даті завершення."; return; }
            apply(new(type.SelectedItemPosition == 0 ? "" : type.SelectedItem?.ToString() ?? "",
                status.SelectedItemPosition == 0 ? "" : status.SelectedItem?.ToString() ?? "", from, to)); dialog.Dismiss();
        });
        Button(body, "Скинути фільтри", () => { apply(new()); dialog.Dismiss(); });
        dialog.Show();
    }
}
