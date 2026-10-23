using MailKit;
using MimeKit;
using System.Globalization;
namespace Mavka.Mobile;

internal static class GmailChangeMessageCache
{
    // Only the known flat changeset MIME format, with a bounded total size.
    // Ordinary mail and unknown/nested MIME retain on-demand retrieval.
    internal static bool Eligible(IMessageSummary summary,bool changes)
    {
        if(!changes || summary.Size is null or 0 or >4_000_000 || summary.Body is not BodyPartMultipart multipart)return false;
        var parts=multipart.BodyParts.ToArray();var attachments=summary.Attachments.ToArray();
        return parts.Length is >1 and <=51 && parts.All(p=>p is BodyPartBasic && !p.PartSpecifier.Contains('.')) &&
            attachments.Length is >0 and <=50 && attachments.All(p=>p.Octets<=370000 && p.FileName?.EndsWith(".json",StringComparison.OrdinalIgnoreCase)==true);
    }
    internal static void Validate(MimeMessage message,string sender,string subject)
    {
        var from=message.From.Mailboxes.ToArray();
        if(from.Length!=1 || !from[0].Address.Equals(sender,StringComparison.OrdinalIgnoreCase) || message.Subject!=subject ||
            message.Body is not Multipart multipart || multipart.Count is <2 or >51 || multipart.Any(p=>p is not MimePart))
            throw new InvalidDataException("Змінилися відправник або структура службового листа.");
    }
    internal static MimeEntity? Part(MimeMessage? message,BodyPart part)=>
        message?.Body is Multipart multipart && int.TryParse(part.PartSpecifier,NumberStyles.None,CultureInfo.InvariantCulture,out var index) && index>0 && index<=multipart.Count?multipart[index-1]:null;
}
