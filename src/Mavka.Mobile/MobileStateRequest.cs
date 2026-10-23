using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Mavka.Mobile;

// Hashes describe the applied mirror, not pending mail or accounting confirmations.
public sealed record MobileStateRequest(string format,string clientId,string requestId,DateTimeOffset createdAt,string sourceId,long sequence,long revision,int index,int count,string reportHash,Dictionary<string,string> hashes)
{
    public const string Format="MAVKA_STATE_REQUEST_V1";
    public static string Hash(Dictionary<string,string> hashes)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hashes.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(p=>p.Key,p=>p.Value))))).ToLowerInvariant();
    public static bool MatchesResponse(MobileChangePart part,string pendingId,string pendingSource,long current,string pendingHash)=>part.requestId.Length>0 && !part.full && part.requestId==pendingId && (pendingSource==part.sourceId || pendingSource.Length==0 && current==0) && part.requestSequence==current && part.sequence>current && part.requestHash==pendingHash;
    public void Validate(string client)
    {
        if(format!=Format || clientId!=client || !Guid.TryParseExact(requestId,"N",out _) || sourceId.Length is not (0 or 64) || sequence<0 || revision<0 || index<0 || index>=count || count is <1 or >400 || hashes.Count>500 || reportHash.Length!=64 || createdAt>DateTimeOffset.UtcNow.AddMinutes(10) || createdAt<DateTimeOffset.UtcNow.AddDays(-30))throw new InvalidDataException("Некоректний або застарілий звіт телефона.");
        foreach(var pair in hashes)
        {
            var fields=pair.Key.Split(':');
            if(fields.Length<3 || fields[0] is not ("asset" or "document") || !int.TryParse(fields[1],out var location) || location<=0 || pair.Key.Length>120 || pair.Value.Length!=64 || !pair.Value.All(Uri.IsHexDigit))throw new InvalidDataException("Некоректні записи звіту телефона.");
        }
        if(sourceId.Length==0 && (sequence!=0 || hashes.Count!=0))throw new InvalidDataException("Порожній облік має містити порожній звіт.");
    }
}
