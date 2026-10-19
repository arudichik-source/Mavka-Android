using MailKit.Net.Smtp;
using MailKit.Security;

namespace Mavka.Mobile;
internal enum RegistrationMailStage {Credentials,Message,Connect,Authenticate,Submit}
internal sealed class RegistrationMailFailure(RegistrationMailStage stage,Exception cause):Exception("Registration mail failed at "+stage,cause)
{
    public RegistrationMailStage Stage {get;}=stage;
    public string Code=>"REG-"+Stage.ToString().ToUpperInvariant()+"-"+InnerException!.GetType().Name+(InnerException is SmtpCommandException smtp?"-"+(int)smtp.StatusCode:"");
    public string UserMessage
    {
        get
        {
            var detail=InnerException switch
            {
                AuthenticationException=>"Gmail відхилив вхід. Перевір пароль застосунку в налаштуваннях Gmail.",
                SslHandshakeException=>"Не встановлено захищене з'єднання. Перевір дату телефона та мережу.",
                SmtpCommandException smtp when smtp.ErrorCode==SmtpErrorCode.RecipientNotAccepted=>"Gmail не прийняв адресу одержувача. Перевір пошту ГОЛОВНОЇ МАВКИ.",
                SmtpCommandException=>"Gmail відхилив надсилання листа. Перевір адресу відправника та стан поштового облікового запису.",
                FormatException or ArgumentException=>"Некоректна адреса або дані реєстрації. Перевір пошту користувача й ГОЛОВНОЇ МАВКИ.",
                KeyNotFoundException or System.Security.Cryptography.CryptographicException=>"Не вдалося прочитати захищені налаштування. Перевір підключення Gmail; не видаляй профіль чи ключі.",
                OperationCanceledException=>"Перевищено час очікування Gmail.",
                System.Net.Sockets.SocketException=>"Немає з'єднання із сервером Gmail. Перевір мережу.",
                _=>"Не завершено етап «"+StageName+"». Код допоможе визначити причину."
            };
            return detail+(Stage==RegistrationMailStage.Submit && InnerException is not SmtpCommandException?" Результат надсилання невідомий: перевір «Надіслані» перед повтором.":"")+"\nКод: "+Code;
        }
    }
    public string StageName=>Stage switch{RegistrationMailStage.Credentials=>"Читання пароля Gmail",RegistrationMailStage.Message=>"Підготовка листа",RegistrationMailStage.Connect=>"Підключення до Gmail",RegistrationMailStage.Authenticate=>"Вхід у Gmail",_=>"Надсилання листа"};
}
