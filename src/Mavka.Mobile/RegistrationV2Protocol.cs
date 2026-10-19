using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;
internal sealed record RegistrationInvitation(string format,string server,string mainEmail,string mainDeviceId,RelayJwk publicSigningKey,string sessionId,long expiresAt,string bootstrapKey);
internal sealed record RegistrationRequest(string format,string sessionId,string requestId,string clientId,string email,string fullName,string rank,string position,string unit,bool isMvo,string phoneNumber,string deviceId,RelayJwk publicSigningKey,RelayJwk publicEncryptionKey);
internal static class RegistrationV2Protocol
{
    public const string Qr="MAVKA_REGISTRATION_QR_V2",Request="MAVKA_REGISTRATION_REQUEST_V2",Keys="MAVKA_BOOTSTRAP_KEYS_V2",Ack="MAVKA_BOOTSTRAP_KEYS_ACK_V2";
    public static void Password(string password){if(password.Length is <6 or >128)throw new ArgumentException("Введіть спільний пароль обміну (6–128 символів) вручну на обох пристроях.");}
    public static byte[] Derive(RegistrationInvitation q,string direction)
    {
        var secret=Convert.FromBase64String(q.bootstrapKey);
        try{return HKDF.DeriveKey(HashAlgorithmName.SHA256,secret,32,Encoding.UTF8.GetBytes(q.sessionId),Encoding.UTF8.GetBytes("MAVKA_REGISTRATION_V2:"+direction));}
        finally{CryptographicOperations.ZeroMemory(secret);}
    }
    public static RegistrationInvitation Parse(string json,DateTimeOffset now)
    {
        if(json.Length>4096)throw new ArgumentException("QR завеликий.");
        using var parsed=JsonDocument.Parse(json);var r=parsed.RootElement;
        if(r.EnumerateObject().Count()!=8)throw new ArgumentException("Невідомі поля QR.");
        var q=r.Deserialize<RegistrationInvitation>()??throw new ArgumentException("Порожній QR.");
        if(q.format!=Qr || q.server!=RelayProtocol.Origin || q.mainDeviceId!="5110f288-ed5f-4d7f-afb5-2c2501f50076" || q.publicSigningKey!=new RelayJwk("EC","P-256","CnlXB4fypB0siFfCtSrgL5NoRTcMF-ZUQo6_SuINXpo","0XW_nZEm7LIg51pc5N0KTl1QrQStQZ3NTEFSndG5j_o") || !System.Net.Mail.MailAddress.TryCreate(q.mainEmail,out var email) || email.Address!=q.mainEmail || q.expiresAt<=now.ToUnixTimeSeconds() || q.expiresAt>now.AddMinutes(11).ToUnixTimeSeconds())throw new ArgumentException("QR прострочений або належить іншій головній Мавці.");
        RelayProtocol.Id(q.sessionId);var key=Convert.FromBase64String(q.bootstrapKey);try{if(key.Length!=32 || Convert.ToBase64String(key)!=q.bootstrapKey)throw new ArgumentException("Некоректний разовий ключ.");}finally{CryptographicOperations.ZeroMemory(key);}return q;
    }
    public static void Validate(RegistrationRequest r)
    {
        if(r.format!=Request || r.clientId is null || r.clientId.Length is <10 or >100 || !System.Net.Mail.MailAddress.TryCreate(r.email,out var email) || email.Address!=r.email || new[]{r.fullName,r.rank,r.position,r.unit}.Any(v=>string.IsNullOrWhiteSpace(v) || v.Length>160 || v.Any(char.IsControl)) || r.phoneNumber is null || r.phoneNumber.Length>32)throw new ArgumentException("Некоректні дані реєстрації.");
        RelayProtocol.Id(r.sessionId);RelayProtocol.Id(r.requestId);RelayProtocol.Id(r.deviceId);
    }
    public static string Seal(string plaintext,byte[] key,string password)
    {Password(password);return Encrypt(MavkaPasswordedEnvelope.Wrap(Encrypt(plaintext,key),password),key);}
    public static string Open(string wire,byte[] key,string password)
    {Password(password);return Decrypt(MavkaPasswordedEnvelope.Unwrap(Decrypt(wire,key),password),key);}
    public static string Encrypt(string plaintext,byte[] key)
    {
        var id=RelayProtocol.Hash(key)[..16];var nonce=RandomNumberGenerator.GetBytes(12);var bytes=Encoding.UTF8.GetBytes(plaintext);var cipher=new byte[bytes.Length];var tag=new byte[16];
        try{using var aes=new AesGcm(key,16);aes.Encrypt(nonce,bytes,cipher,tag,Encoding.UTF8.GetBytes("MAVKA_ENCRYPTED_V1:"+id));return RelayProtocol.Serialize(new{format="MAVKA_ENCRYPTED_V1",algorithm="AES-256-GCM",keyId=id,nonce=Convert.ToBase64String(nonce),ciphertext=Convert.ToBase64String(cipher),tag=Convert.ToBase64String(tag)});}
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public static string Decrypt(string wire,byte[] key)
    {
        if(wire.Length>262144)throw new ArgumentException("Пакет реєстрації завеликий.");using var doc=JsonDocument.Parse(wire);var r=doc.RootElement;
        if(r.EnumerateObject().Count()!=6 || r.GetProperty("format").GetString()!="MAVKA_ENCRYPTED_V1" || r.GetProperty("algorithm").GetString()!="AES-256-GCM" || r.GetProperty("keyId").GetString()!=RelayProtocol.Hash(key)[..16])throw new CryptographicException("Інший ключ або формат.");
        var nonce=Convert.FromBase64String(r.GetProperty("nonce").GetString()!);var cipher=Convert.FromBase64String(r.GetProperty("ciphertext").GetString()!);var tag=Convert.FromBase64String(r.GetProperty("tag").GetString()!);var bytes=new byte[cipher.Length];
        if(nonce.Length!=12 || tag.Length!=16)throw new CryptographicException("Некоректний конверт.");
        try{using var aes=new AesGcm(key,16);aes.Decrypt(nonce,cipher,tag,bytes,Encoding.UTF8.GetBytes("MAVKA_ENCRYPTED_V1:"+RelayProtocol.Hash(key)[..16]));return new UTF8Encoding(false,true).GetString(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
}
