using Android.Content;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Mavka.Mobile;
internal sealed class BootstrapStore(Context context)
{
    private static readonly object Gate=new();
    private ISharedPreferences Preferences=>context.GetSharedPreferences("mavka_exchange_keys_v1",FileCreationMode.Private)!;
    public sealed record Pending(RegistrationInvitation Invitation,RegistrationRequest Request,string Wire);
    public Pending SaveQr(string raw,string email,string password)
    {
        lock(Gate)
        {
            var profile=new UserProfileStore(context).Load()??throw new InvalidOperationException("Збережи профіль.");
            var q=RegistrationV2Protocol.Parse(raw,DateTimeOffset.UtcNow);RegistrationV2Protocol.Password(password);
            var prefs=Preferences;
            var existing=prefs.GetString("bootstrap_pending",null);
            if(existing is not null)
            {
                var old=JsonSerializer.Deserialize<Pending>(Open(existing))!;
                if(old.Invitation.sessionId==q.sessionId)
                {
                    if(old.Invitation!=q || old.Request.clientId!=profile.ClientId || old.Request.email!=email)throw new CryptographicException("Інші дані повторної заявки.");
                    var check=RegistrationV2Protocol.Derive(q,"request");try{_=RegistrationV2Protocol.Open(old.Wire,check,password);}finally{CryptographicOperations.ZeroMemory(check);}
                    return old;
                }
            }
            new ExchangeKeyStore(context).PrepareNewRegistration();
            profile=new UserProfileStore(context).Save(profile.Unit,profile.FullName,profile.Rank,profile.Position,profile.IsMvo,profile.PhoneNumber,Guid.NewGuid().ToString("N"));
            RelayBackground.SetEnabled(context,false);
            var identity=new RelayIdentity(context);identity.BeginNewEnrollment();
            _=identity.Request(profile.ClientId); // Local private-key signing proof; no QR or network.
            var request=new RegistrationRequest(RegistrationV2Protocol.Request,q.sessionId,Guid.NewGuid().ToString(),profile.ClientId,email,profile.FullName,profile.Rank,profile.Position,profile.Unit,profile.IsMvo,profile.PhoneNumber,identity.DeviceId,identity.SigningKey,identity.EncryptionKey);
            RegistrationV2Protocol.Validate(request);
            var key=RegistrationV2Protocol.Derive(q,"request");string wire;
            try{wire=RegistrationV2Protocol.Seal(RelayProtocol.Serialize(request),key,password);}finally{CryptographicOperations.ZeroMemory(key);}
            var pending=new Pending(q,request,wire);
            using var editor=prefs.Edit()!;
            if(!editor.PutString("bootstrap_pending",Seal(JsonSerializer.Serialize(pending)))!.Commit())throw new IOException("QR та заявку не збережено.");
            return pending;
        }
    }
    public bool TryInstall(string envelope,string? sender=null,string? messageId=null)
    {
        lock(Gate)
        {
            var prefs=Preferences;
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
            var saved=JsonSerializer.Deserialize<Pending>(Open(pending))!;var q=saved.Invitation;
            var derived=RegistrationV2Protocol.Derive(q,"keys");string keyId;
            try{keyId=RelayProtocol.Hash(derived)[..16];}finally{CryptographicOperations.ZeroMemory(derived);}
            if(outer.RootElement.GetProperty("keyId").GetString()!=keyId)return false;
            if(sender is not null && !sender.Equals(q.mainEmail,StringComparison.OrdinalIgnoreCase))throw new CryptographicException("Інший відправник першого підключення.");
            var profile=new UserProfileStore(context).Load()??throw new InvalidOperationException("Немає профілю.");
            if(saved.Request.clientId!=profile.ClientId || saved.Request.deviceId!=new RelayIdentity(context).DeviceId)throw new CryptographicException("Інший профіль або телефон.");
            var password=new DocumentPasswordStore(context).Load()??throw new InvalidOperationException("Введи спільний пароль обміну вручну.");
            var data=BootstrapProtocol.Open(envelope,q,saved.Request,password,DateTimeOffset.UtcNow);
            if(messageId is not null && messageId!=data.messageId)throw new CryptographicException("Інший пакет першого підключення.");
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
        var prefs=context.GetSharedPreferences("mavka_exchange_keys_v1",FileCreationMode.Private)!;
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
        var prefs=context.GetSharedPreferences("mavka_exchange_keys_v1",FileCreationMode.Private)!;using var editor=prefs.Edit()!;
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
