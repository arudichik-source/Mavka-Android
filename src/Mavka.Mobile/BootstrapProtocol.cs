using System.Security.Cryptography;
using System.Text.Json;

namespace Mavka.Mobile;
internal sealed record BootstrapInvitation(string format,string server,string mainEmail,string mainDeviceId,RelayJwk publicSigningKey,string clientId,string recipientDeviceId,string sessionId,string messageId,long expiresAt,string keyId,string bootstrapKey);
internal sealed record BootstrapKeys(string format,string sessionId,string messageId,string clientId,string recipientDeviceId,string mainDeviceId,long expiresAt,int generation,string currentKey,string futureKey,string documentPassword,MobileAccessPolicy access);
internal static class BootstrapProtocol
{
    internal const string Qr="MAVKA_BOOTSTRAP_QR_V1",Keys="MAVKA_BOOTSTRAP_KEYS_V1",Ack="MAVKA_BOOTSTRAP_KEYS_ACK_V1";
    public static BootstrapInvitation Invitation(string json,string client,string device,DateTimeOffset now)
    {
        if(json.Length>4096)throw new CryptographicException("QR завеликий.");
        var q=JsonSerializer.Deserialize<BootstrapInvitation>(json)??throw new CryptographicException("Порожній QR.");
        RelayProtocol.Id(q.sessionId);RelayProtocol.Id(q.messageId);
        if(q.format!=Qr || q.server!=RelayProtocol.Origin || q.mainDeviceId!=RelayProtocol.MainId || q.publicSigningKey!=RelayProtocol.MainKey || q.clientId!=client || q.recipientDeviceId!=device || q.expiresAt<=now.ToUnixTimeSeconds() || q.expiresAt>now.AddMinutes(11).ToUnixTimeSeconds() || !System.Net.Mail.MailAddress.TryCreate(q.mainEmail,out _))throw new CryptographicException("QR прострочений або призначений іншому телефону.");
        var key=Convert.FromBase64String(q.bootstrapKey);
        try{if(key.Length!=32 || Convert.ToBase64String(key)!=q.bootstrapKey || RelayProtocol.Hash(key)[..16]!=q.keyId)throw new CryptographicException("Некоректний одноразовий ключ.");}
        finally{CryptographicOperations.ZeroMemory(key);}return q;
    }
    public static BootstrapKeys Open(string envelope,BootstrapInvitation invitation,DateTimeOffset now)
    {
        var key=Convert.FromBase64String(invitation.bootstrapKey);
        string plain;
        try{plain=RelayContentCipher.Decrypt(envelope,id=>id==invitation.keyId?key.ToArray():null);}
        finally{CryptographicOperations.ZeroMemory(key);}
        var k=JsonSerializer.Deserialize<BootstrapKeys>(plain)??throw new CryptographicException("Порожній пакет.");
        if(k.format!=Keys || k.sessionId!=invitation.sessionId || k.messageId!=invitation.messageId || k.clientId!=invitation.clientId || k.recipientDeviceId!=invitation.recipientDeviceId || k.mainDeviceId!=invitation.mainDeviceId || k.expiresAt!=invitation.expiresAt || k.expiresAt<=now.ToUnixTimeSeconds() || k.generation!=1 || k.documentPassword.Length is <6 or >128)throw new CryptographicException("Перший пакет не відповідає QR.");
        var current=Convert.FromBase64String(k.currentKey);var future=Convert.FromBase64String(k.futureKey);
        try{if(current.Length!=32 || future.Length!=32 || current.SequenceEqual(future))throw new CryptographicException("Некоректна пара ключів.");}
        finally{CryptographicOperations.ZeroMemory(current);CryptographicOperations.ZeroMemory(future);}
        k.access.Validate(k.clientId);return k;
    }
    public static string Confirmation(BootstrapKeys k,DateTimeOffset now)=>RelayProtocol.Serialize(new{format=Ack,clientId=k.clientId,sessionId=k.sessionId,messageId=k.messageId,recipientDeviceId=k.recipientDeviceId,mainDeviceId=k.mainDeviceId,generation=1,currentKeyId=KeyId(k.currentKey),futureKeyId=KeyId(k.futureKey),status="INSTALLED",installedAtUtc=now.ToString("O")});
    private static string KeyId(string value){var key=Convert.FromBase64String(value);try{return RelayProtocol.Hash(key)[..16];}finally{CryptographicOperations.ZeroMemory(key);}}
}
