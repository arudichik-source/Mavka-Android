using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

// This record is stored only on the device, encrypted using an Android Keystore key.
internal sealed record UserProfile(
    string ClientId,
    string Unit,
    string FullName,
    string Rank,
    string Position,
    bool IsMvo,
    string PhoneNumber = "");

internal sealed class UserProfileStore
{
    private const string Alias = "mavka_profile_aes_v1";
    private const string Preferences = "mavka_profile_private_v1";
    private readonly ISharedPreferences _prefs;

    public UserProfileStore(Context context)
    {
        _prefs = context.GetSharedPreferences(Preferences, FileCreationMode.Private)!;
    }

    public UserProfile? Load()
    {
        string? stored = _prefs.GetString("profile", null);
        if (string.IsNullOrWhiteSpace(stored)) return null;

        var parts = stored.Split(':', 2);
        if (parts.Length != 2) throw new InvalidOperationException("Profile data invalid.");

        using var keyStore = LoadKeyStore();
        using var key = keyStore.GetKey(Alias, null) ??
            throw new InvalidOperationException("Profile encryption key missing.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var gcm = new GCMParameterSpec(128, Convert.FromBase64String(parts[0]));
        cipher.Init(CipherMode.DecryptMode, key, gcm);

        var bytes = cipher.DoFinal(Convert.FromBase64String(parts[1]))!;
        return JsonSerializer.Deserialize<UserProfile>(Encoding.UTF8.GetString(bytes));
    }

    public UserProfile Save(string unit, string fullName, string rank, string position, bool isMvo, string? phoneNumber = null)
    {
        string id = Load()?.ClientId ?? Guid.NewGuid().ToString("N");
        var profile = new UserProfile(id, unit, fullName, rank, position, isMvo,
            PhoneNumberNormalizer.Normalize(phoneNumber));

        using var keyStore = LoadKeyStore();
        if (!keyStore.ContainsAlias(Alias))
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

        using var key = keyStore.GetKey(Alias, null)!;
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, key);
        var iv = cipher.GetIV() ?? throw new InvalidOperationException("Profile IV missing.");
        var encrypted = cipher.DoFinal(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(profile)))!;

        using var editor = _prefs.Edit()!;
        editor.PutString("profile", Convert.ToBase64String(iv) +
            ":" + Convert.ToBase64String(encrypted));
        if (!editor.Commit()) throw new InvalidOperationException("Profile save failed.");

        return profile;
    }

    private static KeyStore LoadKeyStore()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore")!;
        keyStore.Load((KeyStore.ILoadStoreParameter?)null);
        return keyStore;
    }
}
