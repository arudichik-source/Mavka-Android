using Android.Content;
using Android.Runtime;
using Android.OS;
using Android.Security.Keystore;
using Java.Security;
using Java.Security.Spec;
using System.Security.Cryptography;

namespace Mavka.Mobile;
internal sealed class RelayIdentity
{
    private const string Preferences = "mavka_relay_identity_v1";
    private static readonly object Gate = new();
    private readonly ISharedPreferences _prefs;
    public RelayIdentity(Context context) { _prefs = context.GetSharedPreferences(Preferences, FileCreationMode.Private)!; }
    public bool Enabled => _prefs.GetBoolean("enabled", false);
    public void SetEnabled(bool value)
    {
        if (!_prefs.Edit()!.PutBoolean("enabled", value)!.Commit()) throw new IOException("Не вдалося зберегти налаштування.");
    }
    public string LastStatus => _prefs.GetString("last_status", "Не підключено")!;
    public void Status(string value) => _prefs.Edit()!.PutString("last_status", value)!.Commit();
    public long RetryAfter => _prefs.GetLong("retry_after", 0);
    public void RateLimited() => _prefs.Edit()!.PutLong("retry_after", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 65)!.Commit();
    public string DeviceId
    {
        get
        {
            lock (Gate)
            {
                var id = _prefs.GetString("device_id", null);
                if (id is not null) { RelayProtocol.Id(id); EnsureExisting(id); return id; }
                id = Guid.NewGuid().ToString("D");
                using var store = Open();
                CreateKey("mavka_relay_sign_" + id, KeyStorePurpose.Sign | KeyStorePurpose.Verify);
                // Android 12+ supports non-exportable ECDH agreement keys.
                if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                    CreateKey("mavka_relay_ecdh_" + id, KeyStorePurpose.AgreeKey);
                else
                {
                    using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                    var secret = ecdh.ExportPkcs8PrivateKey();
                    try
                    {
                        if (!_prefs.Edit()!.PutString("ecdh_wrapped",
                            Convert.ToBase64String(TransferKeystoreCipher.Seal(secret)))!
                            .PutString("ecdh_public", Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo()))!.Commit())
                            throw new IOException("Не збережено захищений ключ ECDH.");
                    }
                    finally { CryptographicOperations.ZeroMemory(secret); }
                }
                if (!_prefs.Edit()!.PutString("device_id", id)!.Commit()) throw new IOException("Не збережено Relay ID.");
                return id;
            }
        }
    }
    private void EnsureExisting(string id)
    {
        using var store = Open();
        if (!store.ContainsAlias("mavka_relay_sign_" + id) ||
            (!store.ContainsAlias("mavka_relay_ecdh_" + id) && string.IsNullOrEmpty(_prefs.GetString("ecdh_wrapped", null))))
            throw new CryptographicException("Ключі Relay недоступні. Потрібне повторне спарювання з новою ідентичністю.");
    }
    public RelayJwk SigningKey
    {
        get { var id = DeviceId; using var store = Open(); return RelayProtocol.Jwk(store.GetCertificate("mavka_relay_sign_" + id)!.PublicKey!.GetEncoded()!); }
    }
    public RelayJwk EncryptionKey
    {
        get
        {
            var id = DeviceId; using var store = Open();
            if (store.ContainsAlias("mavka_relay_ecdh_" + id))
                return RelayProtocol.Jwk(store.GetCertificate("mavka_relay_ecdh_" + id)!.PublicKey!.GetEncoded()!);
            return RelayProtocol.Jwk(Convert.FromBase64String(_prefs.GetString("ecdh_public", null) ??
                throw new CryptographicException("Немає публічного ECDH ключа.")));
        }
    }
    public byte[] Sign(byte[] input)
    {
        lock (Gate)
        {
            var id = DeviceId; using var store = Open();
            using var key = store.GetKey("mavka_relay_sign_" + id, null)?.JavaCast<IPrivateKey>() ??
                throw new CryptographicException("Ключ підпису недоступний.");
            using var signature = Signature.GetInstance("SHA256withECDSA")!;
            signature.InitSign(key); signature.Update(input);
            return RelayProtocol.RawSignature(signature.Sign()!);
        }
    }
    public string Request(string clientId)
    {
        var id = DeviceId;
        return RelayProtocol.Serialize(new { format = "MAVKA_RELAY_DEVICE_REQUEST_V1",
            server = RelayProtocol.Origin, mainDeviceId = RelayProtocol.MainId, deviceId = id,
            clientId, publicSigningKey = SigningKey, publicEncryptionKey = EncryptionKey });
    }
    private static void CreateKey(string alias, KeyStorePurpose purpose)
    {
        using var generator = KeyPairGenerator.GetInstance("EC", "AndroidKeyStore")!;
        using var spec = new KeyGenParameterSpec.Builder(alias, purpose)
            .SetAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))
            .SetDigests(KeyProperties.DigestSha256).Build();
        generator.Initialize(spec); generator.GenerateKeyPair();
    }
    private static KeyStore Open() { var s = KeyStore.GetInstance("AndroidKeyStore")!; s.Load(null); return s; }
}
