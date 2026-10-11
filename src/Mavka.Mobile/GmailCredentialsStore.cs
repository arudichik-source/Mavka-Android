using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Text;

namespace Mavka.Mobile;

internal sealed class GmailCredentialsStore
{
    private const string Alias = "mavka_gmail_aes_v1";
    private const string PrefsName = "mavka_gmail_prefs_v1";
    private readonly ISharedPreferences _prefs;

    public GmailCredentialsStore(Context context)
    {
        _prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
    }

    public string GetEmail() => _prefs.GetString("email", "") ?? "";
public string GetMainMavkaEmail()
{
    const string defaultEmail = "mavkaoblik@gmail.com";

    var savedEmail = _prefs.GetString(
        "main_mavka_email", null);

    return string.IsNullOrWhiteSpace(savedEmail)
        ? defaultEmail
        : savedEmail.Trim();
}

public void SaveMainMavkaEmail(string email)
{
    using var editor = _prefs.Edit()!;

    editor.PutString(
        "main_mavka_email",
        email.Trim().ToLowerInvariant());

    if (!editor.Commit())
        throw new InvalidOperationException(
            "Не вдалося зберегти адресу ГОЛОВНОЇ МАВКИ");
}
    public bool HasCredentials => !string.IsNullOrWhiteSpace(GetEmail()) &&
                                  !string.IsNullOrEmpty(_prefs.GetString("encrypted_password", ""));

    public void Save(string email, string password)
    {
        using var store = LoadStore();
        if (!store.ContainsAlias(Alias))
        {
            using var generator = KeyGenerator.GetInstance("AES", "AndroidKeyStore")!;
            using var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes("GCM")
                .SetEncryptionPaddings("NoPadding")
                .Build();
            generator.Init(spec);
            generator.GenerateKey();
        }
        using var key = store.GetKey(Alias, null)!;
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, key);
        var iv = cipher.GetIV() ?? throw new InvalidOperationException("Missing IV");
        var result = cipher.DoFinal(Encoding.UTF8.GetBytes(password))!;
        var encoded = Convert.ToBase64String(iv) + ":" + Convert.ToBase64String(result);
        using var editor = _prefs.Edit()!;
        editor.PutString("email", email);
        editor.PutString("encrypted_password", encoded);
        if (!editor.Commit()) throw new InvalidOperationException("Unable to save Gmail settings");
    }

    public string? GetPassword()
    {
        var value = _prefs.GetString("encrypted_password", null);
        if (string.IsNullOrEmpty(value)) return null;
        var split = value.Split(':', 2);
        if (split.Length != 2) throw new InvalidOperationException("Invalid credential format");
        using var store = LoadStore();
        using var key = store.GetKey(Alias, null) ??
            throw new InvalidOperationException("Android Keystore key missing");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var parameters = new GCMParameterSpec(128, Convert.FromBase64String(split[0]));
        cipher.Init(CipherMode.DecryptMode, key, parameters);
        return Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(split[1]))!);
    }

    public void Clear()
    {
        using var editor = _prefs.Edit()!;
        editor.Clear();
        if (!editor.Commit()) throw new InvalidOperationException("Unable to clear Gmail settings");
    }

    private static KeyStore LoadStore()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load((KeyStore.ILoadStoreParameter?)null);
        return store;
    }
}
