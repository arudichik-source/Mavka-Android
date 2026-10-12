using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Security.Cryptography;
using System.Text;

namespace Mavka.Mobile;

/// <summary>
/// Separate, authenticated private file vault. Does not write to the accounting
/// database or change the exchange-key scheme. AES key never leaves Keystore.
/// Original PDF/XLSX bytes are only exported following an explicit SAF action.
/// </summary>
internal sealed class PrivateAttachmentStore
{
    private const string Alias = "mavka_private_attachments_aes_v1";
    private static readonly byte[] Aad = "MAVKA_PRIVATE_FILE_V1"u8.ToArray();
    private const int MaxStored = MavkaEncryptedFileEnvelope.MaxFileBytes + 1024;
    private readonly string _folder;
    public sealed record StoredFile(string Id, string Name, string Mime, int Size);

    public PrivateAttachmentStore(Context context)
    {
        _folder = Path.Combine(context.FilesDir?.AbsolutePath ??
            throw new InvalidOperationException("Private storage unavailable"),
            "secure_received_files_v1");
    }

    // Keyed by authenticated filename + content hash: repeat delivery does not duplicate.
    public StoredFile Save(MavkaEncryptedFileEnvelope.ReceivedFile file)
    {
        string mime = MavkaEncryptedFileEnvelope.MimeFor(file.FileName);
        if (mime != file.MimeType || file.Length > MavkaEncryptedFileEnvelope.MaxFileBytes)
            throw new ArgumentException("Invalid authenticated file metadata.");
        byte[] name = Encoding.UTF8.GetBytes(file.FileName);
        if (name.Length > 640 || name.Length == 0)
            throw new ArgumentException("Filename too long.");
        var checksum = SHA256.HashData(file.Content);
        if (!string.Equals(Convert.ToHexString(checksum), file.Sha256,
                StringComparison.Ordinal))
            throw new CryptographicException("File checksum mismatch.");

        string id = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(file.FileName + ":" + file.Sha256))).ToLowerInvariant();
        byte[] plain = new byte[2 + name.Length + checksum.Length + file.Content.Length];
        plain[0] = (byte)(name.Length & 255);
        plain[1] = (byte)(name.Length >> 8);
        Buffer.BlockCopy(name, 0, plain, 2, name.Length);
        Buffer.BlockCopy(checksum, 0, plain, 2 + name.Length, checksum.Length);
        Buffer.BlockCopy(file.Content, 0, plain, 2 + name.Length + checksum.Length, file.Content.Length);

        try
        {
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, id + ".bin");
            if (File.Exists(path))
            {
                using var existing = Load(id);
                if (existing.FileName != file.FileName ||
                    existing.Sha256 != file.Sha256)
                    throw new CryptographicException("Local document ID collision.");
                return new StoredFile(id, file.FileName, mime, file.Content.Length);
            }

            byte[] nonce, sealedData;
            using (var keys = OpenKeyStore())
            {
                EnsureKey(keys);
                using var key = keys.GetKey(Alias, null)!;
                using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
                cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key);
                cipher.UpdateAAD(Aad);
                nonce = cipher.GetIV()!;
                sealedData = cipher.DoFinal(plain)!;
            }
            if (nonce.Length != 12)
                throw new CryptographicException("Invalid Android Keystore IV.");
            var output = new byte[1 + 12 + sealedData.Length];
            output[0] = 1;
            Buffer.BlockCopy(nonce, 0, output, 1, 12);
            Buffer.BlockCopy(sealedData, 0, output, 13, sealedData.Length);
            var pending = path + ".tmp";
            try
            {
                File.WriteAllBytes(pending, output);
                File.Move(pending, path, false);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }

            using (var verified = Load(id))
            {
                if (verified.Sha256 != file.Sha256 ||
                    verified.FileName != file.FileName)
                    throw new CryptographicException("Stored attachment read-back failed.");
            }
            return new StoredFile(id, file.FileName, mime, file.Content.Length);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public MavkaEncryptedFileEnvelope.ReceivedFile Load(string id)
    {
        if (!IsId(id)) throw new ArgumentException("Invalid file identifier.");
        var path = Path.Combine(_folder, id + ".bin");
        var sealedBytes = File.ReadAllBytes(path);
        if (sealedBytes.Length < 64 || sealedBytes.Length > MaxStored ||
            sealedBytes[0] != 1)
            throw new CryptographicException("Invalid local file version or length.");
        byte[] plain;
        using (var store = OpenKeyStore())
        using (var key = store.GetKey(Alias, null) ??
            throw new CryptographicException("Local Keystore key unavailable"))
        using (var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!)
        {
            cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key,
                new GCMParameterSpec(128, sealedBytes.AsSpan(1, 12).ToArray()));
            cipher.UpdateAAD(Aad);
            plain = cipher.DoFinal(sealedBytes.AsSpan(13).ToArray())!;
        }
        try
        {
            if (plain.Length < 43) throw new CryptographicException("Truncated file.");
            int n = plain[0] | (plain[1] << 8);
            if (n is < 1 or > 640 || plain.Length < 2 + n + 32 + 8)
                throw new CryptographicException("Invalid local file record.");
            string name = new UTF8Encoding(false, true).GetString(plain, 2, n);
            var mime = MavkaEncryptedFileEnvelope.MimeFor(name);
            byte[] content = plain.AsSpan(2 + n + 32).ToArray();
            var digest = SHA256.HashData(content);
            if (!CryptographicOperations.FixedTimeEquals(
                digest, plain.AsSpan(2 + n, 32)))
            {
                CryptographicOperations.ZeroMemory(content);
                throw new CryptographicException("Local content checksum mismatch.");
            }
            var hash = Convert.ToHexString(digest);
            var actualId = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(name + ":" + hash))).ToLowerInvariant();
            if (!string.Equals(id, actualId, StringComparison.Ordinal))
            {
                CryptographicOperations.ZeroMemory(content);
                throw new CryptographicException("Local file identifier mismatch.");
            }
            return new MavkaEncryptedFileEnvelope.ReceivedFile(name, mime, content, hash);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public IReadOnlyList<StoredFile> List()
    {
        if (!Directory.Exists(_folder)) return Array.Empty<StoredFile>();
        var result = new List<StoredFile>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.bin").Take(60))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            if (!IsId(id)) continue;
            try
            {
                using var file = Load(id);
                result.Add(new StoredFile(id, file.FileName, file.MimeType,
                    checked((int)file.Length)));
            }
            catch (Exception) { /* Corrupt entry never blocks other files. */ }
        }
        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static bool IsId(string? id) => id is { Length: 64 } &&
        id.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static KeyStore OpenKeyStore()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load((KeyStore.ILoadStoreParameter?)null);
        return store;
    }

    private static void EnsureKey(KeyStore store)
    {
        if (store.ContainsAlias(Alias)) return;
        using var generator = KeyGenerator.GetInstance("AES", "AndroidKeyStore")!;
        using var spec = new KeyGenParameterSpec.Builder(Alias,
            KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes("GCM")
            .SetEncryptionPaddings("NoPadding").Build();
        generator.Init(spec);
        generator.GenerateKey();
    }
}
