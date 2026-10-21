using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
namespace Mavka.Mobile;
internal static class TransferKeystoreCipher
{
    private const string Alias = "mavka_pending_parts_aes_v1";
    private static readonly byte[] Aad = "MAVKA_PENDING_PART_LOCAL_V1"u8.ToArray();
    private static KeyStore Store() { var s = KeyStore.GetInstance("AndroidKeyStore")!; s.Load(null); return s; }
    internal sealed class ReadSession:IDisposable
    {
        private readonly KeyStore _store=Store();
        private readonly Java.Security.IKey _key;
        private readonly Cipher _cipher=Cipher.GetInstance("AES/GCM/NoPadding")!;
        public ReadSession(){_key=_store.GetKey(Alias,null)??throw new System.Security.Cryptography.CryptographicException("Local key unavailable.");}
        public string OpenText(string encoded)
        {
            var encrypted=Convert.FromBase64String(encoded);
            if(encrypted.Length<29)throw new System.Security.Cryptography.CryptographicException("Truncated part.");
            _cipher.Init(Javax.Crypto.CipherMode.DecryptMode,_key,new GCMParameterSpec(128,encrypted[..12]));
            _cipher.UpdateAAD(Aad);var bytes=_cipher.DoFinal(encrypted[12..])!;
            try{return System.Text.Encoding.UTF8.GetString(bytes);}finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);}
        }
        public void Dispose(){_cipher.Dispose();_key.Dispose();_store.Dispose();}
    }
    public static byte[] Seal(byte[] plain)
    {
        using var store = Store();
        if (!store.ContainsAlias(Alias))
        {
            using var gen = KeyGenerator.GetInstance("AES", "AndroidKeyStore")!;
            using var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes("GCM").SetEncryptionPaddings("NoPadding").Build();
            gen.Init(spec); gen.GenerateKey();
        }
        using var key = store.GetKey(Alias, null)!;
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key); cipher.UpdateAAD(Aad);
        return cipher.GetIV()!.Concat(cipher.DoFinal(plain)!).ToArray();
    }
    public static byte[] Open(byte[] encrypted)
    {
        if (encrypted.Length < 29) throw new System.Security.Cryptography.CryptographicException("Truncated part.");
        using var store = Store(); using var key = store.GetKey(Alias, null)!;
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key, new GCMParameterSpec(128, encrypted[..12]));
        cipher.UpdateAAD(Aad); return cipher.DoFinal(encrypted[12..])!;
    }
}
