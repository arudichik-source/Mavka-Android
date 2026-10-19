using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Text;

namespace Mavka.Mobile;

// Settings-managed password for the removable password-protected file format.
// The password is encrypted at rest with an Android Keystore key.
internal sealed class DocumentPasswordStore
{
    private const string Alias = "mavka_document_password_wrap_v1";
    private const string Preferences = "mavka_document_password_v1";
    private readonly ISharedPreferences _prefs;
    private readonly Context _context;

    public DocumentPasswordStore(Context context)
    { _context=context;_prefs = context.GetSharedPreferences(Preferences, FileCreationMode.Private)!; }

    public bool IsConfigured => !string.IsNullOrEmpty(_prefs.GetString("sealed_password", null)) || new ExchangeKeyStore(_context).BootstrapDocumentPassword() is not null;

    public string? Load()
    {
        if(new ExchangeKeyStore(_context).BootstrapDocumentPassword() is string initial)return initial;
        var sealedValue = _prefs.GetString("sealed_password", null);
        if (string.IsNullOrEmpty(sealedValue)) return new ExchangeKeyStore(_context).BootstrapDocumentPassword();
        var parts = sealedValue.Split(':', 2);
        if (parts.Length != 2) throw new InvalidOperationException("Invalid stored password.");

        using var store = OpenStore();
        using var key = store.GetKey(Alias, null) ??
            throw new InvalidOperationException("Keystore key is unavailable.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var spec = new GCMParameterSpec(128, Convert.FromBase64String(parts[0]));
        cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key, spec);
        return Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(parts[1]))!);
    }

    public void Save(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 6 or > 128)
            throw new ArgumentException("Password must be 6 to 128 characters.");

        using var store = OpenStore();
        if (!store.ContainsAlias(Alias))
        {
            using var generator = KeyGenerator.GetInstance("AES", "AndroidKeyStore")!;
            using var spec = new KeyGenParameterSpec.Builder(
                    Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes("GCM")
                .SetEncryptionPaddings("NoPadding")
                .Build();
            generator.Init(spec);
            generator.GenerateKey();
        }

        using var key = store.GetKey(Alias, null)!;
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key);
        var iv = cipher.GetIV()!;
        var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(value))!;

        using var editor = _prefs.Edit()!;
        editor.PutString("sealed_password",
            Convert.ToBase64String(iv) + ":" + Convert.ToBase64String(encrypted));
        if (!editor.Commit())
            throw new InvalidOperationException("Unable to save document password.");
        new ExchangeKeyStore(_context).ClearBootstrapDocumentPassword();
    }

    private static KeyStore OpenStore()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load((KeyStore.ILoadStoreParameter?)null);
        return store;
    }
}
