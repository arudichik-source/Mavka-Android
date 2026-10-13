using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// Password-based portable backup. Never copy an Android Keystore ciphertext
/// that is impossible to open on the replacement phone. Use independent
/// scrypt-like PBKDF2 key stretching and authenticated AES-256-GCM.
/// </summary>
internal static class PortableBackupEnvelope
{
    public const string Format = "MAVKA_PORTABLE_BACKUP_V1";
    public const int MaxPlainBytes = 28_000_000;
    private const int Iterations = 550_000;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes(Format);

    public static byte[] Encrypt(string json, string password)
    {
        CheckPassword(password);
        byte[] plain = new UTF8Encoding(false, true).GetBytes(json);
        if (plain.Length is < 10 or > MaxPlainBytes)
            throw new ArgumentException("Резервна копія перевищує 28 МБ. Потрібно розбиття.");
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt,
            Iterations, HashAlgorithmName.SHA256, 32);
        byte[] cipher = new byte[plain.Length], tag = new byte[16];
        try
        {
            using var aes = new AesGcm(derived, 16);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData);
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                format = Format,
                algorithm = "AES-256-GCM",
                kdf = "PBKDF2-SHA256",
                iterations = Iterations,
                salt = Convert.ToBase64String(salt),
                nonce = Convert.ToBase64String(nonce),
                ciphertext = Convert.ToBase64String(cipher),
                tag = Convert.ToBase64String(tag)
            }));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(cipher);
        }
    }

    public static string Decrypt(byte[] encrypted, string password)
    {
        CheckPassword(password);
        if (encrypted.Length is < 150 or > 40_000_000)
            throw new ArgumentException("Непідтримуваний розмір резервної копії.");
        using var doc = JsonDocument.Parse(encrypted, new JsonDocumentOptions { MaxDepth = 4 });
        var root = doc.RootElement;
        string Get(string field) => root.GetProperty(field).GetString() ??
            throw new ArgumentException("Некоректний контейнер.");
        if (Get("format") != Format || Get("algorithm") != "AES-256-GCM" ||
            Get("kdf") != "PBKDF2-SHA256" ||
            root.GetProperty("iterations").GetInt32() != Iterations)
            throw new ArgumentException("Невідома версія резервної копії.");
        var salt = Convert.FromBase64String(Get("salt"));
        var nonce = Convert.FromBase64String(Get("nonce"));
        var tag = Convert.FromBase64String(Get("tag"));
        var ciphertext = Convert.FromBase64String(Get("ciphertext"));
        if (salt.Length != 16 || nonce.Length != 12 || tag.Length != 16 ||
            ciphertext.Length > MaxPlainBytes)
            throw new ArgumentException("Резервна копія пошкоджена.");
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt,
            Iterations, HashAlgorithmName.SHA256, 32);
        var plain = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(derived, 16);
            aes.Decrypt(nonce, ciphertext, tag, plain, AssociatedData);
            return new UTF8Encoding(false, true).GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static void CheckPassword(string password)
    {
        if (password is null || password.Length is < 12 or > 128)
            throw new ArgumentException("Пароль резервної копії: 12–128 символів.");
    }
}
