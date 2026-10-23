using Android.Widget;
namespace Mavka.Mobile;
public partial class MainActivity
{
    private async Task<string> ReceiveGmailUpdatesAsync(string password,string sender,CancellationToken cancellation,Action<string> progress)
    {
        var session=new GmailRefreshSession();
        var retained=new Dictionary<(uint,uint),MavkaMailItem>();
        var report="";var mirrorStatus="";
        try
        {
            while(!session.AtLimit)
            {
                cancellation.ThrowIfCancellationRequested();
                progress($"Отримую оновлення Gmail… Перевірено листів: {session.Messages}.\n"+mirrorStatus);
                _messages=await Task.Run(()=>GmailConnectionService.ReadFromSenderAsync(
                    _store.GetEmail(),password,sender,_exchangeKeys,_documentPasswords,LoadUserProfile()?.ClientId,
                    validity=>_archive.ListMailProcessingStates(validity,MailProcessingScope()),cancellation:cancellation,attempted:session.Attempted),cancellation);
                if(!session.Register(_messages.Select(m=>(m.UidValidity,m.MessageUid))))break;
                progress($"Зберігаю отриману порцію. Перевірено листів: {session.Messages}.\n"+mirrorStatus);
                try
                {
                    var pendingQr = _qrPairing.LoadPending();
                    var currentProfile = LoadUserProfile();
                    if (pendingQr is not null && currentProfile is not null)
                    {
                        if (DateTimeOffset.UtcNow >= pendingQr.ExpiresUtc)
                            _qrPairing.ClearPending();
                        else foreach (var mail in _messages)
                            if (!mail.PreviouslyArchived &&
                                QrPairingProtocol.VerifyAck(mail.Body, pendingQr,
                                    currentProfile.ClientId, DateTimeOffset.UtcNow))
                            {
                                _qrPairing.MarkConfirmed(pendingQr.MainEmail);
                                _archive.MarkMailReady(mail.UidValidity, mail.MessageUid,
                                    MailProcessingScope());
                                _archive.RecordSyncEvent("pairing", "QR-зв'язок підтверджено");
                                break;
                            }
                    }
                }
                catch
                {
                    // Pairing is optional. Never relax Gmail authentication or
                    // prevent existing encrypted transfers after a bad QR.
                }
    
                    await ProcessFileTransfersAsync(password,sender,cancellation);
                report=await ArchiveAndCleanupAsync(password,sender,cancellation);
                foreach(var mail in _messages)retained[(mail.UidValidity,mail.MessageUid)]=mail;
                mirrorStatus=await Task.Run(()=>{using var mirror=new MobileChangeStore(this);return mirror.SynchronizationProgress();},cancellation);
                progress($"Продовжую завантаження. Перевірено листів: {session.Messages}.\n"+mirrorStatus);
                SaveInboxCacheSafe();
            }
            if(mirrorStatus.Length==0)mirrorStatus=await Task.Run(()=>{using var mirror=new MobileChangeStore(this);return mirror.SynchronizationProgress();},cancellation);
            return $"Перевірено листів за це оновлення: {session.Messages}.\n"+mirrorStatus+"\nОстання порція: "+report+
                (session.AtLimit?"\nДосягнуто межу одного оновлення. Збережені частини залишено; натисніть «Оновити пошту», щоб продовжити.":"");
        }
        finally
        {
            foreach(var mail in _messages)retained[(mail.UidValidity,mail.MessageUid)]=mail;
            _messages=retained.Values.OrderByDescending(m=>m.MessageUid).Take(30).ToArray();SaveInboxCacheSafe();
        }
    }
}