using Android.App;
using Android.Text;
using Android.Widget;

namespace Mavka.Mobile;

/// <summary>Explicit, scoped QA operation. Does not touch Gmail, keys, archives or vault.</summary>
internal static class TestLedgerResetDialog
{
    public static void Show(Activity activity, PropertyLedgerStore ledger,
        string unit, Func<bool> busy, Action<string> report)
    {
        new AlertDialog.Builder(activity)
            .SetTitle("Очищення тестового обліку")
            .SetMessage("Видаляються тільки проведені рухи і залишки «Тестового підрозділу»." +
                " Архів документів, Gmail, ключі та захищені файли не змінюються." +
                " У разі ненадісланих підтверджень операцію буде заборонено.")
            .SetNegativeButton("Скасувати", (_, _) => { })
            .SetPositiveButton("Продовжити", (_, _) =>
            {
                var confirmation = new EditText(activity)
                {
                    Hint = "Набери ОЧИСТИТИ",
                    InputType = InputTypes.ClassText
                };
                confirmation.SetSingleLine(true);
                new AlertDialog.Builder(activity)
                    .SetTitle("Підтверди очищення")
                    .SetMessage("Напиши ОЧИСТИТИ, щоб підтвердити дію.")
                    .SetView(confirmation)
                    .SetNegativeButton("Скасувати", (_, _) => { })
                    .SetPositiveButton("Виконати", (_, _) =>
                    {
                        if (confirmation.Text?.Trim() != "ОЧИСТИТИ")
                        {
                            report("Контрольне слово не збігається. Залишки збережені.");
                            return;
                        }
                        if (busy())
                        {
                            report("Дочекайся завершення синхронізації.");
                            return;
                        }
                        try
                        {
                            int removed = ledger.ClearTestUnitLedger(unit);
                            report("Тестовий облік очищено: " + removed +
                                " рухів. Архів і криптографічні ключі залишилися.");
                        }
                        catch (Exception ex)
                        {
                            report("Очищення не проведено: " + ex.Message);
                        }
                    }).Show();
            }).Show();
    }
}
