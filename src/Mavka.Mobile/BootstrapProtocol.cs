using System.Security.Cryptography;
using System.Text.Json;

namespace Mavka.Mobile;
internal sealed record BootstrapKeys(string format,string sessionId,string requestId,string messageId,string clientId,string recipientDeviceId,string mainDeviceId,long expiresAt,int generation,string currentKey,string futureKey,MobileAccessPolicy access);
internal static class BootstrapProtocol
{
    internal const string Qr=RegistrationV2Protocol.Qr,Keys=RegistrationV2Protocol.Keys,Ack=RegistrationV2Protocol.Ack;
    public static BootstrapKeys Open(string envelope,RegistrationInvitation invitation,RegistrationRequest request,string password,DateTimeOffset now)
    {
        _=RegistrationV2Protocol.Parse(JsonSerializer.Serialize(invitation),now);
        var key=RegistrationV2Protocol.Derive(invitation,"keys");string plain;
        try{plain=RegistrationV2Protocol.Open(envelope,key,password);}finally{CryptographicOperations.ZeroMemory(key);}
        using var json=JsonDocument.Parse(plain);
        if(json.RootElement.EnumerateObject().Count()!=12)throw new CryptographicException("Невідомі поля пакета ключів.");
        var k=json.RootElement.Deserialize<BootstrapKeys>()??throw new CryptographicException("Порожній пакет.");
        RelayProtocol.Id(k.messageId);
        if(k.format!=Keys || k.sessionId!=invitation.sessionId || k.requestId!=request.requestId || k.clientId!=request.clientId || k.recipientDeviceId!=request.deviceId || k.mainDeviceId!=invitation.mainDeviceId || k.expiresAt!=invitation.expiresAt || k.expiresAt<=now.ToUnixTimeSeconds() || k.generation!=1)throw new CryptographicException("Перший пакет не відповідає запрошенню та заявці.");
        var current=Convert.FromBase64String(k.currentKey);var future=Convert.FromBase64String(k.futureKey);
        try{if(current.Length!=32 || future.Length!=32 || current.SequenceEqual(future))throw new CryptographicException("Некоректна пара ключів.");}
        finally{CryptographicOperations.ZeroMemory(current);CryptographicOperations.ZeroMemory(future);}
        k.access.Validate(k.clientId);return k;
    }
    public static string Confirmation(BootstrapKeys k,DateTimeOffset now)=>RelayProtocol.Serialize(new{format=Ack,clientId=k.clientId,sessionId=k.sessionId,messageId=k.messageId,recipientDeviceId=k.recipientDeviceId,mainDeviceId=k.mainDeviceId,generation=1,currentKeyId=KeyId(k.currentKey),futureKeyId=KeyId(k.futureKey),status="INSTALLED",installedAtUtc=now.ToString("O")});
    private static string KeyId(string value){var key=Convert.FromBase64String(value);try{return RelayProtocol.Hash(key)[..16];}finally{CryptographicOperations.ZeroMemory(key);}}
}
