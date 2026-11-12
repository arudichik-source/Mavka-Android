using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// Ephemeral pairing secret protected by Android Keystore. No network
/// credentials, exchange keys or documents are provisioned through QR itself.
/// </summary>
internal sealed class QrPairingStore(Context context)
{
    private const string Alias = "mavka_pairing_secret_v1";
    private const string Prefs = "mavka_qr_pairing";
    private const string Pending = "pending";
    private const string Confirmed = "confirmed_main_email";

    private ISharedPreferences Preferences =>
        context.GetSharedPreferences(Prefs, FileCreationMode.Private) ??
        throw new InvalidOperationException("QR-пам'ять недоступна.");

    public void SavePending(QrPairingProtocol.Invitation invite)
    {
        var serialized = JsonSerializer.Serialize(invite);
        Preferences.Edit()!.PutString(Pending, Seal(serialized))!.Apply();
    }

    public QrPairingProtocol.Invitation? LoadPending()
    {
        var value = Preferences.GetString(Pending, null);
        if (string.IsNullOrWhiteSpace(value)) return null;
        return JsonSerializer.Deserialize<QrPairingProtocol.Invitation>(Unseal(value));
    }

    public string? ConfirmedEmail => Preferences.GetString(Confirmed, null);

    public void MarkConfirmed(string mainEmail)
    {
        Preferences.Edit()!.PutString(Confirmed, mainEmail.Trim().ToLowerInvariant())!
            .Remove(Pending)!.Apply();
    }

    public void ClearPending() => Preferences.Edit()!.Remove(Pending)!.Apply();

    private static string Seal(string message)
    {
        using var store = OpenStore();
        if (!store.ContainsAlias(Alias))
        {
            using var generator = KeyGenerator.GetInstance("AES", "AndroidKeyStore")!;
            using var spec = new KeyGenParameterSpec.Builder(
                Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes("GCM").SetEncryptionPaddings("NoPadding").Build();
            generator.Init(spec);
            generator.GenerateKey();
        }
        using var key = store.GetKey(Alias, null)!;
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, key);
        return Convert.ToBase64String(cipher.GetIV()!) + ":" +
            Convert.ToBase64String(cipher.DoFinal(Encoding.UTF8.GetBytes(message))!);
    }

    private static string Unseal(string data)
    {
        var parts = data.Split(':', 2);
        if (parts.Length != 2) throw new InvalidOperationException("QR-стан пошкоджений.");
        using var store = OpenStore();
        using var key = store.GetKey(Alias, null) ??
            throw new InvalidOperationException("Ключ QR-прив'язки недоступний.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var spec = new GCMParameterSpec(128, Convert.FromBase64String(parts[0]));
        cipher.Init(CipherMode.DecryptMode, key, spec);
        return Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(parts[1]))!);
    }

    private static KeyStore OpenStore()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load((KeyStore.ILoadStoreParameter?)null);
        return store;
    }
}
