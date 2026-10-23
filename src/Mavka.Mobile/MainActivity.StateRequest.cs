using Android.Widget;
using System.Text.Json;

namespace Mavka.Mobile;
public partial class MainActivity
{
    private async Task CancelStateUpdate(TextView status){if(_busy || _exchangeOperation is not null){status.Text="Зачекайте завершення обміну.";return;}using var operation=BeginExchange();_busy=true;try{await Task.Run(()=>{using var mirror=new MobileChangeStore(this);mirror.CancelStateRequest();},operation.Token);status.Text="Очікування відповіді скасовано. Оновіть пошту або сервер, щоб застосувати збережені звичайні пакети.";}catch(Exception error){status.Text=ExplainOperationError(error);}finally{_busy=false;}}
    private async Task RequestStateUpdate(TextView status)
    {
        if(_busy || _exchangeOperation is not null){status.Text="Зачекайте завершення поточного обміну.";return;}
        using var operation=BeginExchange();_busy=true;
        try
        {
            status.Text="Готую звіт про застосовані майно та документи…";
            MobileStateRequest[] parts;await RelaySync.Gate.WaitAsync(operation.Token);
            try{parts=await Task.Run(()=>{using var mirror=new MobileChangeStore(this);return mirror.CreateStateRequest(operation.Token);},operation.Token);}finally{RelaySync.Gate.Release();}
            status.Text="Надсилаю запит оновлення: частин "+parts.Length+"…";
            var sender=_store.GetEmail();var password=_store.GetPassword()??"";var recipient=_store.GetMainMavkaEmail();
            if(_store.HasCredentials && recipient.Length>0)
                await Task.Run(()=>RegistrationMailService.SendStateRequestsAsync(sender,password,recipient,parts,_exchangeKeys,operation.Token),operation.Token);
            else
            {
                var relay=new RelaySync(this);var groups=await Task.Run(()=>parts.Select(p=>relay.Queue("state-request:"+p.requestId+":"+p.index,JsonSerializer.Serialize(p))).ToArray(),operation.Token);
                await Task.Run(()=>relay.Synchronize(operation.Token),operation.Token);
                if(groups.Any(g=>!relay.Submitted(g)))throw new IOException("Запит збережено в черзі Relay. Повторіть оновлення сервера.");
            }
            status.Text="Запит надіслано. На ПК з’явиться пропозиція пакета за звітом телефона. До відповіді нові пакети зберігаються в черзі. Після надсилання пакета з ПК оновіть пошту або сервер — база доповниться автоматично.";
        }
        catch(Exception error){status.Text=ExplainOperationError(error);}
        finally{_busy=false;}
    }
}
internal static partial class RegistrationMailService
{
    internal static async Task SendStateRequestsAsync(string sender,string password,string recipient,MobileStateRequest[] parts,ExchangeKeyStore keys,CancellationToken cancellation)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromMinutes(4));
        var opened=await GmailTlsConnection.OpenSmtpAsync(timeout.Token);using var client=opened.Client;
        await client.AuthenticateAsync(sender,password,timeout.Token);
        foreach(var part in parts)
        {
            timeout.Token.ThrowIfCancellationRequested();part.Validate(part.clientId);
            var message=new MimeKit.MimeMessage();message.From.Add(MimeKit.MailboxAddress.Parse(sender));message.To.Add(MimeKit.MailboxAddress.Parse(recipient));
            message.MessageId="state-"+part.requestId+"-"+part.index+"@mavka.local";
            SetSecureBody(message,MobileStateRequest.Format,"",part,keys);await client.SendAsync(message,timeout.Token);
        }
        await client.DisconnectAsync(true,timeout.Token);
    }
}
