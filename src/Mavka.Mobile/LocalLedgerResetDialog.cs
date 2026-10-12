using Android.App;
using Android.Text;
using Android.Widget;

namespace Mavka.Mobile;

/// <summary>
/// Permanent, explicit per-unit accounting maintenance. Never silently touches
/// document archive, Gmail messages, profile, encryption keys or exported files.
/// </summary>
internal static class LocalLedgerResetDialog
{
    public static void Show(Activity activity, PropertyLedgerStore ledger,
        DocumentArchiveStore archive, string unit, Func<bool> busy,
        Action<string> report)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            report("Спочатку вкажи підрозділ у профілі.");
            return;
        }
        new AlertDialog.Builder(activity)
            .SetTitle("Очистити локальний облік?")
            .SetMessage("Підрозділ: " + unit + "\n\n" +
                "Будуть видалені його локальні залишки і проведені рухи. " +
                "Дія незворотна. Архів документів, Gmail, ключі, профіль і файли " +
                "не зміняться. Синхронізація незавершених підтверджень обов’язкова.")
            .SetNegativeButton("Скасувати", (_, _) => { })
            .SetPositiveButton("Продовжити", (_, _) =>
            {
                var confirmation = new EditText(activity)
                {
                    Hint = "Введи ОЧИСТИТИ",
                    InputType = InputTypes.ClassText
                };
                confirmation.SetSingleLine(true);
                new AlertDialog.Builder(activity)
                    .SetTitle("Підтвердження очищення обліку")
                    .SetMessage("Щоб очистити облік підрозділу «" + unit +
                        "», введи слово ОЧИСТИТИ.")
                    .SetView(confirmation)
                    .SetNegativeButton("Скасувати", (_, _) => { })
                    .SetPositiveButton("Очистити", (_, _) =>
                    {
                        if (!string.Equals(confirmation.Text?.Trim(), "ОЧИСТИТИ",
                                StringComparison.Ordinal))
                        {
                            report("Контрольне слово не збігається. Облік не змінено.");
                            return;
                        }
                        if (busy())
                        {
                            report("Заверши синхронізацію, перш ніж очищати облік.");
                            return;
                        }
                        try
                        {
                            var deleted = ledger.ClearLedgerForUnit(unit);
                            // The archive is a separate SQLite database and
                            // keeps document evidence. Update only its local
                            // posting flags; keep sent/remote sync history.
                            try
                            {
                                var statuses = archive.ResetLocalPostingForUnit(unit);
                                report("Локальні рухи очищено: " + deleted +
                                    ". Статусів проведення оновлено: " + statuses +
                                    ". Документи, ключі та історію відправлень збережено.");
                            }
                            catch (Exception ex)
                            {
                                report("Локальні рухи очищено: " + deleted +
                                    ". Але оновити статуси в архіві не вдалося: " +
                                    ex.Message + ". Повтори дію для завершення звірки.");
                            }
                        }
                        catch (Exception ex)
                        {
                            report("Очищення відхилено: " + ex.Message);
                        }
                    }).Show();
            }).Show();
    }
}
