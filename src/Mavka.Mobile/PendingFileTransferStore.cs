using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Mavka.Mobile;

// Pure persistent store; Android supplies Keystore protection. No plaintext disk writes.
internal sealed class PendingFileTransferStore
{
    internal sealed record MailRef(uint Validity, uint Uid);
    internal sealed record Entry(MavkaFileParts.Header Metadata, byte[] Content, List<MailRef> Mail);
    internal sealed record Completion(string FileId, MavkaFileParts.Header Metadata);
    internal sealed record Progress(string Id, string Name, int Received, int Total, bool Saved);
    private readonly string _root;
    private readonly Func<byte[], byte[]> _seal, _open;
    public PendingFileTransferStore(string root, Func<byte[], byte[]> seal, Func<byte[], byte[]> open)
    { _root = root; _seal = seal; _open = open; Directory.CreateDirectory(root); }
    private string Folder(string id)
    { if (!MavkaFileParts.IsId(id)) throw new ArgumentException("Invalid transfer ID."); return Path.Combine(_root, id); }
    private T Read<T>(string path)
    {
        var sealedBytes = File.ReadAllBytes(path);
        if (sealedBytes.Length > 400_000) throw new IOException("Transfer record too large.");
        var bytes = _open(sealedBytes);
        try { return JsonSerializer.Deserialize<T>(bytes, MavkaFileParts.Json) ?? throw new IOException("Invalid transfer record."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private void Write<T>(string path, T value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value, MavkaFileParts.Json);
        string pending = path + ".tmp";
        try { File.WriteAllBytes(pending, _seal(plain)); File.Move(pending, path, true); }
        finally { CryptographicOperations.ZeroMemory(plain); if (File.Exists(pending)) File.Delete(pending); }
    }
    private IReadOnlyList<Entry> Entries(string id)
    {
        var folder = Folder(id);
        if (!Directory.Exists(folder)) return [];
        var result = new List<Entry>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.part").Take(126))
            {
                var e = Read<Entry>(path); result.Add(e);
                MavkaFileParts.Validate(e.Metadata);
                if (e.Metadata.TransferId != id || Path.GetFileName(path) != e.Metadata.Index + ".part" ||
                    e.Content.Length != Math.Min(MavkaFileParts.ChunkBytes, e.Metadata.Size - e.Metadata.Index * MavkaFileParts.ChunkBytes))
                    throw new CryptographicException("Transfer record identity mismatch.");
            }
            return result;
        }
        catch { foreach (var e in result) CryptographicOperations.ZeroMemory(e.Content); throw; }
    }
    public Progress Accept(string envelope, Func<string, byte[]?> resolver, MailRef? mail)
    {
        using var part = MavkaFileParts.Decrypt(envelope, resolver);
        var h = part.Metadata;
        var folder = Folder(h.TransferId);
        if (!Directory.Exists(folder) && Directory.EnumerateDirectories(_root).Take(6).Count() >= 6)
            throw new IOException("Незавершених передач забагато. Видали непотрібні.");
        Directory.CreateDirectory(folder);
        var entries = Entries(h.TransferId);
        try
        {
            if (entries.Any(e => !MavkaFileParts.SameFile(h, e.Metadata)))
                throw new CryptographicException("Conflicting transfer metadata.");
            var existing = entries.FirstOrDefault(e => e.Metadata.Index == h.Index);
            if (existing is not null && !CryptographicOperations.FixedTimeEquals(existing.Content, part.Content))
                throw new CryptographicException("Conflicting duplicate part.");
            var refs = existing?.Mail ?? [];
            if (mail is not null && !refs.Contains(mail)) refs.Add(mail);
            if (refs.Count > 20) throw new IOException("Too many duplicate messages.");
            Write(Path.Combine(folder, h.Index + ".part"), new Entry(h, part.Content, refs));
            // Authenticate local read-back before accepting the chunk.
            var verified = Read<Entry>(Path.Combine(folder, h.Index + ".part"));
            try { if (verified.Metadata != h || !verified.Content.SequenceEqual(part.Content)) throw new CryptographicException("Part read-back failed."); }
            finally { CryptographicOperations.ZeroMemory(verified.Content); }
            return new Progress(h.TransferId, h.FileName, entries.Count + (existing is null ? 1 : 0), h.Count,
                File.Exists(Path.Combine(folder, "saved.bin")));
        }
        finally { foreach (var e in entries) CryptographicOperations.ZeroMemory(e.Content); }
    }
    public MavkaEncryptedFileEnvelope.ReceivedFile? Assemble(string id)
    {
        var entries = Entries(id);
        byte[]? content = null;
        try
        {
            if (entries.Count == 0) return null;
            var h = entries[0].Metadata;
            if (entries.Count != h.Count) return null;
            if (entries.Any(e => !MavkaFileParts.SameFile(h, e.Metadata)) ||
                entries.Select(e => e.Metadata.Index).Distinct().Count() != h.Count)
                throw new CryptographicException("Invalid completed transfer.");
            content = new byte[h.Size];
            foreach (var e in entries) Buffer.BlockCopy(e.Content, 0, content,
                e.Metadata.Index * MavkaFileParts.ChunkBytes, e.Content.Length);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(content), Convert.FromHexString(h.Sha256)))
                throw new CryptographicException("Whole-file checksum mismatch.");
            MavkaEncryptedFileEnvelope.ValidateContents(h.Mime, content);
            var file = new MavkaEncryptedFileEnvelope.ReceivedFile(h.FileName, h.Mime, content, h.Sha256);
            content = null; return file;
        }
        finally
        { if (content is not null) CryptographicOperations.ZeroMemory(content); foreach (var e in entries) CryptographicOperations.ZeroMemory(e.Content); }
    }
    public void MarkSaved(string id, string fileId)
    {
        var entries = Entries(id);
        try
        {
            if (entries.Count == 0 || entries.Count != entries[0].Metadata.Count) throw new IOException("Transfer incomplete.");
            Write(Path.Combine(Folder(id), "saved.bin"), new Completion(fileId, entries[0].Metadata));
            if (Read<Completion>(Path.Combine(Folder(id), "saved.bin")).FileId != fileId) throw new IOException("Completion read-back failed.");
        }
        finally { foreach (var e in entries) CryptographicOperations.ZeroMemory(e.Content); }
    }
    public Completion? Saved(string id)
    {
        var path = Path.Combine(Folder(id), "saved.bin");
        return File.Exists(path) ? Read<Completion>(path) : null;
    }
    public void AcknowledgeMail(string id, MailRef mail)
    {
        var entries = Entries(id);
        try
        {
            foreach (var e in entries.Where(e => e.Mail.Contains(mail)))
            {
                e.Mail.RemoveAll(m => m == mail);
                Write(Path.Combine(Folder(id), e.Metadata.Index + ".part"), e);
            }
        }
        finally { foreach (var e in entries) CryptographicOperations.ZeroMemory(e.Content); }
    }
    public IReadOnlyList<MailRef> CleanupCandidates(string id)
    {
        if (!File.Exists(Path.Combine(Folder(id), "saved.bin"))) return [];
        var entries = Entries(id);
        try { return entries.SelectMany(e => e.Mail).Distinct().ToArray(); }
        finally { foreach (var e in entries) CryptographicOperations.ZeroMemory(e.Content); }
    }
    public IReadOnlyList<Progress> List()
    {
        var result = new List<Progress>();
        foreach (var folder in Directory.EnumerateDirectories(_root).Take(6))
        {
            var id = Path.GetFileName(folder);
            if (!MavkaFileParts.IsId(id)) continue;
            var entries = Entries(id);
            try { if (entries.Count > 0) result.Add(new Progress(id, entries[0].Metadata.FileName, entries.Count,
                entries[0].Metadata.Count, File.Exists(Path.Combine(folder, "saved.bin")))); }
            finally { foreach (var e in entries) CryptographicOperations.ZeroMemory(e.Content); }
        }
        return result;
    }
    public void Delete(string id) { var path = Folder(id); if (Directory.Exists(path)) Directory.Delete(path, true); }
}
