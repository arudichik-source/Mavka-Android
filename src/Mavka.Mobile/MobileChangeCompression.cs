using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mavka.Mobile;
// Compression is inside both password protection and per-user AES. It contains
// no password/key fields and never changes Relay/Worker envelopes or signatures.
public static class MobileChangeCompression
{
    public const string Format="MAVKA_CHANGESET_BROTLI_V1";
    private sealed record Compressed(string format,string recipientClientId,int size,string sha256,string data);
    public static string Pack(string json)
    {
        var bytes=Encoding.UTF8.GetBytes(json);
        if(bytes.Length>150000)throw new InvalidDataException("Пакет змін завеликий.");
        using var parsed=JsonDocument.Parse(json);
        if(parsed.RootElement.GetProperty("format").GetString()!=MobileChangePart.Format)throw new InvalidDataException("Непідтримуваний формат змін.");
        using var stream=new MemoryStream();using(var zip=new BrotliStream(stream,CompressionLevel.Optimal,true))zip.Write(bytes);
        var result=JsonSerializer.Serialize(new Compressed(Format,parsed.RootElement.GetProperty("recipientClientId").GetString()!,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),Convert.ToBase64String(stream.ToArray())));
        return Encoding.UTF8.GetByteCount(result)<bytes.Length?result:json;
    }
    public static string Open(string json,string client)
    {
        using var parsed=JsonDocument.Parse(json);
        if(!parsed.RootElement.TryGetProperty("format",out var format) || format.GetString()!=Format)return json;
        if(Encoding.UTF8.GetByteCount(json)>210000)throw new InvalidDataException("Стиснений пакет завеликий.");
        var data=parsed.RootElement.Deserialize<Compressed>()??throw new InvalidDataException("Порожній пакет.");
        if(data.recipientClientId!=client || data.size is <1 or >150000 || data.data.Length>200000 || data.sha256.Length!=64)throw new InvalidDataException("Невірні межі або одержувач пакета.");
        using var input=new MemoryStream(Convert.FromBase64String(data.data));using var zip=new BrotliStream(input,CompressionMode.Decompress);using var output=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=zip.Read(buffer))>0){if(output.Length+count>data.size)throw new InvalidDataException("Перевищено заявлений розмір змін.");output.Write(buffer,0,count);}
        var bytes=output.ToArray();if(bytes.Length!=data.size || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=data.sha256)throw new CryptographicException("Контрольна сума змін не збігається.");
        var result=Encoding.UTF8.GetString(bytes);using var original=JsonDocument.Parse(result);
        if(original.RootElement.GetProperty("format").GetString()!=MobileChangePart.Format || original.RootElement.GetProperty("recipientClientId").GetString()!=client)throw new CryptographicException("Інший одержувач або формат усередині змін.");
        return result;
    }
}
