using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;

/// <summary>
/// Reboot-persistent, encrypted read-only preview of the Gmail inbox.
/// Never caches Gmail credentials, installed key packages, or importable RawJson.
/// This cache is NOT an authoritative document store; the archive remains separate.
/// </summary>
internal sealed class GmailInboxCache
{
    private const string Alias = "mavka_inbox_preview_aes_v1";
    private const int MaxPlaintextBytes = 512_000;
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("MAVKA_INBOX_CACHE_V1");
    private readonly string _path;
    private sealed record CachePayload(string Account, string Sender, List<MavkaMailItem> Items);

    public GmailInboxCache(Context context)
    {
        _path = Path.Combine(context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("Private storage unavailable"),
            "mavka_inbox_preview_v1.bin");
    }

    public IReadOnlyList<MavkaMailItem> Load(string account, string sender)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(sender) ||
            !File.Exists(_path)) return Array.Empty<MavkaMailItem>();
        try
        {
            byte[] sealedBytes = File.ReadAllBytes(_path);
            if (sealedBytes.Length is < 29 or > 600_000)
                return Array.Empty<MavkaMailItem>();
            if (sealedBytes[0] != 1) return Array.Empty<MavkaMailItem>();
            byte[] nonce = sealedBytes.AsSpan(1, 12).ToArray();
            byte[] ciphertext = sealedBytes.AsSpan(13, sealedBytes.Length - 29).ToArray();
            byte[] tag = sealedBytes.AsSpan(sealedBytes.Length - 16, 16).ToArray();
            byte[] plaintext;
            using (var store = OpenStore())
            using (var key = store.GetKey(Alias, null))
            {
                if (key is null) return Array.Empty<MavkaMailItem>();
                using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
                cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key,
                    new GCMParameterSpec(128, nonce));
                cipher.UpdateAAD(Aad);
                plaintext = cipher.DoFinal(ciphertext.Concat(tag).ToArray())!;
            }
            try
            {
                if (sealedBytes[0] != 1 || plaintext.Length > MaxPlaintextBytes)
                    return Array.Empty<MavkaMailItem>();
                var payload = JsonSerializer.Deserialize<CachePayload>(plaintext);
                if (payload is null ||
                    !string.Equals(payload.Account, account, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(payload.Sender, sender, StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<MavkaMailItem>();
                return payload.Items.Take(30)
                    .Where(m => m.MessageUid != 0 && m.UidValidity != 0)
                    .Select(m => m with
                    {
                        AttachmentsComplete = false,
                        CachedPreview = true,
                        Attachments = m.Attachments.Select(a =>
                            a with { RawJson = null, InstalledKeyPackage = false,
                                SecureFilePart = null, SecureFileEnvelope = null, AuthenticatedJson = false }).ToArray()
                    })
                    .ToArray();
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception)
        {
            // A damaged cache must not interrupt startup or affect authoritative data.
            return Array.Empty<MavkaMailItem>();
        }
    }

    public void Save(string account, string sender, IReadOnlyList<MavkaMailItem> messages)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(sender)) return;
        var previews = new List<MavkaMailItem>();
        foreach (var mail in messages.Take(30))
        {
            // Cache only display text; never retain the raw decrypted importable JSON
            // or the installed cryptographic key package.
            var safeAttachments = mail.Attachments
                .Where(a => !a.InstalledKeyPackage)
                .Take(3)
                .Select(a => new MavkaTextAttachment(
                    a.FileName, a.Text.Length > 24_000
                        ? a.Text[..24_000] + "\n[Перегляд скорочено]"
                        : a.Text, null, false))
                .ToArray();
            var invoice = mail.Attachments
                .Select(a => MavkaExchangeParser.TryInvoice(a.RawJson, out var parsed)
                    ? parsed : null)
                .FirstOrDefault(x => x is not null);
            var candidate = mail with
            {
                Body = mail.Body.Length > 12_000 ? mail.Body[..12_000] : mail.Body,
                Attachments = safeAttachments,
                AttachmentsComplete = false,
                CachedPreview = true,
                SingleSecureFile = false,
                CachedInvoiceNumber = invoice?.Number,
                CachedInvoiceRecipient = invoice?.Recipient
            };
            var test = new CachePayload(account, sender, previews.Append(candidate).ToList());
            if (JsonSerializer.SerializeToUtf8Bytes(test).Length > MaxPlaintextBytes)
                break;
            previews.Add(candidate);
        }

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(
            new CachePayload(account, sender, previews));
        if (plaintext.Length > MaxPlaintextBytes) return;
        string pending = _path + ".tmp";
        try
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
            cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key);
            cipher.UpdateAAD(Aad);
            var nonce = cipher.GetIV()!;
            var encrypted = cipher.DoFinal(plaintext)!; // ciphertext + authentication tag
            if (nonce.Length != 12 || encrypted.Length < 17)
                throw new CryptographicException("Invalid cache encryption.");
            var output = new byte[1 + nonce.Length + encrypted.Length];
            output[0] = 1;
            Buffer.BlockCopy(nonce, 0, output, 1, nonce.Length);
            Buffer.BlockCopy(encrypted, 0, output, 13, encrypted.Length);
            File.WriteAllBytes(pending, output); // only ciphertext written to disk
            File.Move(pending, _path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (File.Exists(pending)) File.Delete(pending);
        }
    }

    public void Clear(bool strict=false)
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) when(!strict) { }
        // Do not delete the Android Keystore alias, keys or archive.
    }

    private static KeyStore OpenStore()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        return store;
    }
}

