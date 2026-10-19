using Android.App;
namespace Mavka.Mobile;
public partial class MainActivity
{
    private void ShowSafeFailure(string action,Exception error)
    {
        var code="MOBILE-"+error.GetType().Name;
        try
        {
            var path=System.IO.Path.Combine(FilesDir!.AbsolutePath,"last-ui-failure.txt");
            // Type and managed call sites only: never exception.Message/ToString,
            // passwords, profile values, enrollment QR or encrypted packet text.
            System.IO.File.WriteAllText(path,DateTimeOffset.UtcNow.ToString("O")+"\n"+action+"\n"+code+"\n"+error.StackTrace);
        }catch{}
        var message=error is RegistrationMailFailure mail?mail.UserMessage:
            error is ArgumentException or InvalidOperationException?error.Message:
            "Не вдалося відкрити або виконати дію. Дані та ключі збережено. Код: "+code;
        new AlertDialog.Builder(this).SetTitle(action).SetMessage(message).SetPositiveButton("Закрити",(_,_)=>{}).Show();
    }
}
