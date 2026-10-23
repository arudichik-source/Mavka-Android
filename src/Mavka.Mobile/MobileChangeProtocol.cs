using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Mavka.Mobile;
public sealed record MobileChangeOperation(string kind,string key,int locationId,bool remove,JsonElement payload);
public sealed record MobileChangePart(string format,string recipientClientId,string sourceId,string batchId,long sequence,long baseSequence,bool initial,string asOf,MobileAccessPolicy access,int index,int count,string manifestHash,MobileChangeOperation[] operations,bool full=false,string requestId="",long requestSequence=0,string requestHash="",string resultHash="")
{
    public const string Format="MAVKA_CHANGESET_V1";
    public static string Hash(IEnumerable<MobileChangeOperation> values)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values.ToArray())))).ToLowerInvariant();
    public void Validate(string client)
    {
        access.Validate(client);
        if(format!=Format || recipientClientId!=client || sourceId.Length!=64 || !Guid.TryParseExact(batchId,"N",out _) || baseSequence<0 || sequence!=baseSequence+1 || initial!=(baseSequence==0) || index<0 || index>=count || count is <1 or >2000 || !DateOnly.TryParseExact(asOf,"yyyy-MM-dd",out _) || manifestHash.Length!=64 || operations.Length>1000)
            throw new InvalidDataException("Некоректний пакет змін.");
        if(requestId.Length>0 && (!Guid.TryParseExact(requestId,"N",out _) || requestSequence<0 || requestSequence>baseSequence || requestHash.Length!=64 || resultHash.Length!=64 || full))throw new InvalidDataException("Некоректна відповідь на запит телефона.");
        foreach(var op in operations)
        {
            if(op.kind is not ("asset" or "document") || op.key.Length is <1 or >120 || !access.Allows(op.locationId,op.kind=="asset") || !op.key.StartsWith(op.kind+":"+op.locationId+":",StringComparison.Ordinal) || !op.remove && (op.payload.ValueKind!=JsonValueKind.Object || op.payload.GetProperty("locationId").GetInt32()!=op.locationId))
                throw new InvalidDataException("Зміни виходять за межі дозволених підрозділів.");
        }
        if(operations.Select(o=>o.key).Distinct(StringComparer.Ordinal).Count()!=operations.Length)throw new InvalidDataException("Повторення позиції змін.");
    }
}
