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
        private string? _wrapped;
        private byte[]? _dataKey;
        public ReadSession(){_key=_store.GetKey(Alias,null)??throw new System.Security.Cryptography.CryptographicException("Local key unavailable.");}
        public string OpenText(string encoded)
        {
            if(encoded.StartsWith("batch1:",StringComparison.Ordinal))
            {
                var fields=encoded.Split(':');
                if(fields.Length!=3)throw new System.Security.Cryptography.CryptographicException("Invalid local batch envelope.");
                if(_wrapped!=fields[1])
                {
                    if(_dataKey is not null)System.Security.Cryptography.CryptographicOperations.ZeroMemory(_dataKey);
                    _dataKey=Open(Convert.FromBase64String(fields[1]));_wrapped=fields[1];
                    if(_dataKey.Length!=32)throw new System.Security.Cryptography.CryptographicException("Invalid local batch key.");
                }
                var wire=Convert.FromBase64String(fields[2]);
                if(wire.Length<28)throw new System.Security.Cryptography.CryptographicException("Truncated local record.");
                var plain=new byte[wire.Length-28];
                try
                {
                    using var aes=new System.Security.Cryptography.AesGcm(_dataKey,16);
                    aes.Decrypt(wire.AsSpan(0,12),wire.AsSpan(28),wire.AsSpan(12,16),plain,Aad);
                    return System.Text.Encoding.UTF8.GetString(plain);
                }
                finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain);}
            }
            var encrypted=Convert.FromBase64String(encoded);
            if(encrypted.Length<29)throw new System.Security.Cryptography.CryptographicException("Truncated part.");
            _cipher.Init(Javax.Crypto.CipherMode.DecryptMode,_key,new GCMParameterSpec(128,encrypted[..12]));
            _cipher.UpdateAAD(Aad);var bytes=_cipher.DoFinal(encrypted[12..])!;
            try{return System.Text.Encoding.UTF8.GetString(bytes);}finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);}
        }
        public void Dispose(){if(_dataKey is not null)System.Security.Cryptography.CryptographicOperations.ZeroMemory(_dataKey);_cipher.Dispose();_key.Dispose();_store.Dispose();}
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
    internal sealed class WriteSession:IDisposable
    {
        // Envelope encryption avoids tens of thousands of slow hardware IPCs.
        // Only this random data key is in process memory. The non-exportable
        // Android master key wraps it once; the plaintext key is never stored.
        private readonly byte[] _dataKey=System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        private readonly string _wrapped;
        public WriteSession(){_wrapped=Convert.ToBase64String(Seal(_dataKey));}
        public string SealText(string value)
        {
            var bytes=System.Text.Encoding.UTF8.GetBytes(value);
            try
            {
                var wire=new byte[28+bytes.Length];System.Security.Cryptography.RandomNumberGenerator.Fill(wire.AsSpan(0,12));
                using var aes=new System.Security.Cryptography.AesGcm(_dataKey,16);
                aes.Encrypt(wire.AsSpan(0,12),bytes,wire.AsSpan(28),wire.AsSpan(12,16),Aad);
                return "batch1:"+_wrapped+":"+Convert.ToBase64String(wire);
            }
            finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);}
        }
        public void Dispose(){System.Security.Cryptography.CryptographicOperations.ZeroMemory(_dataKey);}
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
