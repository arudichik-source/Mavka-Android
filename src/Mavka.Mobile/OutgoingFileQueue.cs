using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Mavka.Mobile;
internal sealed class OutgoingFileQueue(string root, Func<byte[], byte[]> seal, Func<byte[], byte[]> open)
{
    internal sealed record Batch(MavkaFileParts.Header Metadata, int NextIndex);
    private string Folder(string id)
    { if (!MavkaFileParts.IsId(id)) throw new ArgumentException("Invalid outgoing ID."); return Path.Combine(root,id); }
    private void Write<T>(string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value,MavkaFileParts.Json);
        try { File.WriteAllBytes(path + ".tmp",seal(bytes)); File.Move(path + ".tmp",path,true); }
        finally { CryptographicOperations.ZeroMemory(bytes); if(File.Exists(path+".tmp")) File.Delete(path+".tmp"); }
    }
    private T Read<T>(string path)
    {
        var encrypted = File.ReadAllBytes(path);
        if(encrypted.Length > 400_000) throw new IOException("Outgoing record too large.");
        var bytes = open(encrypted);
        try { return JsonSerializer.Deserialize<T>(bytes,MavkaFileParts.Json) ?? throw new IOException("Invalid outbox."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public IReadOnlyList<Batch> List()
    {
        Directory.CreateDirectory(root);
        var result = new List<Batch>();
        foreach(var folder in Directory.EnumerateDirectories(root).Take(6))
        {
            var path = Path.Combine(folder,"queue.bin"); if(!File.Exists(path)) continue;
            var b = Read<Batch>(path); MavkaFileParts.Validate(b.Metadata);
            if (Path.GetFileName(folder) != b.Metadata.TransferId || b.NextIndex < 0 || b.NextIndex > b.Metadata.Count)
                throw new CryptographicException("Outgoing queue identity mismatch.");
            result.Add(b);
        }
        return result;
    }
    public Batch Enqueue(MavkaFileParts.Header header, Func<int,string> encrypt)
    {
        var previous = List().FirstOrDefault(b => b.Metadata.FileName == header.FileName &&
            b.Metadata.Sha256 == header.Sha256 && b.Metadata.Size == header.Size);
        if(previous is not null) return previous;
        if(Directory.EnumerateDirectories(root).Take(6).Count() >= 6) throw new IOException("Черга файлів заповнена.");
        var folder = Folder(header.TransferId); Directory.CreateDirectory(folder);
        try
        {
            for(int i=0;i<header.Count;i++) Write(Path.Combine(folder,i+".bin"),encrypt(i));
            var batch = new Batch(header,0); Write(Path.Combine(folder,"queue.bin"),batch); return batch;
        }
        catch { Directory.Delete(folder,true); throw; }
    }
    public string Part(Batch batch) => Read<string>(Path.Combine(Folder(batch.Metadata.TransferId),batch.NextIndex+".bin"));
    public Batch Advance(Batch batch)
    {
        if(batch.NextIndex >= batch.Metadata.Count) throw new IOException("Batch already sent.");
        var next = batch with { NextIndex = batch.NextIndex + 1 };
        Write(Path.Combine(Folder(batch.Metadata.TransferId),"queue.bin"),next); return next;
    }
    public void Delete(string id) { var folder=Folder(id); if(Directory.Exists(folder)) Directory.Delete(folder,true); }
}
