using Android.Content;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Mavka.Mobile;
internal sealed class BootstrapStore(Context context)
{
    private static readonly object Gate=new();
    private ISharedPreferences Preferences=>context.GetSharedPreferences("mavka_exchange_keys_v1",FileCreationMode.Private)!;
    public void SaveQr(string raw)
    {
        lock(Gate)
        {
            var profile=new UserProfileStore(context).Load()??throw new InvalidOperationException("Збережи профіль.");
            _=BootstrapProtocol.Invitation(raw,profile.ClientId,new RelayIdentity(context).DeviceId,DateTimeOffset.UtcNow);
            using var prefs=Preferences;
            if(new ExchangeKeyStore(context).HasKeyPair)throw new InvalidOperationException("Перші ключі вже встановлено. Нове підключення потребує нового профілю.");
            using var editor=prefs.Edit()!;
            if(!editor.PutString("bootstrap_pending",Seal(raw))!.Commit())throw new IOException("QR не збережено.");
        }
    }
    public bool TryInstall(string envelope,string? sender=null,string? messageId=null)
    {
        lock(Gate)
        {
            using var prefs=Preferences;
            using var hashDoc=JsonDocument.Parse(envelope);
            var h=hashDoc.RootElement;
            var hash=RelayProtocol.Hash(RelayProtocol.Serialize(new{format=h.GetProperty("format").GetString(),algorithm=h.GetProperty("algorithm").GetString(),keyId=h.GetProperty("keyId").GetString(),nonce=h.GetProperty("nonce").GetString(),ciphertext=h.GetProperty("ciphertext").GetString(),tag=h.GetProperty("tag").GetString()}));
            if(prefs.GetString("bootstrap_hash",null)==hash)
            {
                using var receipt=JsonDocument.Parse(prefs.GetString("bootstrap_ack",null)!);
                if(messageId is not null && messageId!=receipt.RootElement.GetProperty("messageId").GetString() || sender is not null && !sender.Equals(prefs.GetString("bootstrap_sender",null),StringComparison.OrdinalIgnoreCase))throw new CryptographicException("Повторний пакет має інший маршрут.");
                return true;
            }
            var pending=prefs.GetString("bootstrap_pending",null);if(pending is null)return false;
            using var outer=JsonDocument.Parse(envelope);
            var q=JsonSerializer.Deserialize<BootstrapInvitation>(Open(pending))!;
            if(outer.RootElement.GetProperty("keyId").GetString()!=q.keyId)return false;
            if(sender is not null && !sender.Equals(q.mainEmail,StringComparison.OrdinalIgnoreCase) || messageId is not null && messageId!=q.messageId)throw new CryptographicException("Інший відправник або пакет першого підключення.");
            var profile=new UserProfileStore(context).Load()??throw new InvalidOperationException("Немає профілю.");
            q=BootstrapProtocol.Invitation(Open(pending),profile.ClientId,new RelayIdentity(context).DeviceId,DateTimeOffset.UtcNow);
            var data=BootstrapProtocol.Open(envelope,q,DateTimeOffset.UtcNow);
            new ExchangeKeyStore(context).InstallBootstrap(data,hash,BootstrapProtocol.Confirmation(data,DateTimeOffset.UtcNow),q.mainEmail);
            // Independent read-back precedes transport ACK. The pending one-time key
            // is removed by the same atomic preference commit as the key pair.
            if(!new ExchangeKeyStore(context).HasKeyPair || new DocumentPasswordStore(context).Load() is null || new MobileAccessStore(context).Load() is null || prefs.GetString("bootstrap_hash",null)!=hash)throw new IOException("Не вдалося перевірити збережені ключі.");
            return true;
        }
    }
    public string? Confirmation=>Preferences.GetString("bootstrap_ack",null);
    private static string Seal(string value)=>Convert.ToBase64String(TransferKeystoreCipher.Seal(Encoding.UTF8.GetBytes(value)));
    private static string Open(string value){var bytes=TransferKeystoreCipher.Open(Convert.FromBase64String(value));try{return Encoding.UTF8.GetString(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}}
}
internal sealed class MobileAccessStore(Context context)
{
    public MobileAccessPolicy? Load()
    {
        var id=new UserProfileStore(context).Load()?.ClientId;
        using var prefs=context.GetSharedPreferences("mavka_exchange_keys_v1",FileCreationMode.Private)!;
        var raw=prefs.GetString("mobile_access",null);if(raw is null || id is null)return null;
        var access=JsonSerializer.Deserialize<MobileAccessPolicy>(raw)!;access.Validate(id);return access;
    }
    public MobileAccessPolicy Accept(JsonElement access)
    {
        var client=new UserProfileStore(context).Load()?.ClientId??throw new InvalidOperationException("Немає профілю.");
        var policy=access.Deserialize<MobileAccessPolicy>()??throw new CryptographicException("Немає дозволів.");policy.Validate(client);
        var old=Load();
        if(old is not null && policy.revision<old.revision)return old;
        if(old is not null && policy.revision==old.revision && JsonSerializer.Serialize(policy)!=JsonSerializer.Serialize(old))throw new CryptographicException("Суперечливі дозволи.");
        using var prefs=context.GetSharedPreferences("mavka_exchange_keys_v1",FileCreationMode.Private)!;using var editor=prefs.Edit()!;
        if(!editor.PutString("mobile_access",JsonSerializer.Serialize(policy))!.Commit())throw new IOException("Дозволи не збережено.");return policy;
    }
    public bool Allows(string unit,bool assets=false)=>Load() is {} access && access.units.Any(u=>u.unit.Equals(unit.Trim(),StringComparison.OrdinalIgnoreCase)) && (assets?access.canViewAssets:access.canViewDocuments);
    public bool TryDocument(string json,out ArchiveImport? document)
    {
        document=null;using var parsed=JsonDocument.Parse(json);
        var root=parsed.RootElement;
        var profile=new UserProfileStore(context).Load()??throw new InvalidOperationException("Немає профілю.");
        if(!root.TryGetProperty("recipientClientId",out var recipient) || recipient.GetString()!=profile.ClientId)return false;
        var access=root.TryGetProperty("access",out var policy)?Accept(policy):Load();
        if(access is null)return false;
        foreach(var grant in access.units)
            if(ArchiveDocumentClassifier.TryClassify(json,grant.unit,out document) && document is not null && Allows(document.Unit,document.Kind=="Знімок залишків"))
            {
                var clean=System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();clean.Remove("access");
                document=document with{Json=clean.ToJsonString()};return true;
            }
        return false;
    }
    public void AutoPost(string json)
    {
        if(!TryDocument(json,out var document) || document is null)return;
        var access=Load()!;
        if(access.isMvo || !access.autoPostInvoices || !MavkaExchangeParser.TryInvoice(json,out var invoice) || invoice is null)return;
        using var ledger=new PropertyLedgerStore(context);ledger.AddConfirmedInvoice(invoice);
        ledger.MarkMailSent(invoice.DocumentId); // Automatic import is not an MVO approval.
        using var archive=new DocumentArchiveStore(context);archive.MarkPosted(invoice.DocumentId);
    }
}
