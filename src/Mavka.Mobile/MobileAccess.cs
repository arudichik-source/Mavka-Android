using System.Text.Json;

namespace Mavka.Mobile;

public sealed record MobileUnitGrant(int locationId,string unit);
public sealed record MobileAccessPolicy(string format,string clientId,long revision,bool isMvo,MobileUnitGrant[] units,bool canViewAssets,bool canViewDocuments,bool canConfirmInvoices,bool autoPostInvoices)
{
    public const string Format="MAVKA_ACCESS_V1";
    public void Validate(string client)
    {
        if(format!=Format || clientId!=client || revision<1 || units.Length is <1 or >100 || units.Any(u=>u.locationId<1 || string.IsNullOrWhiteSpace(u.unit) || u.unit.Length>200) || units.Select(u=>u.locationId).Distinct().Count()!=units.Length || units.Select(u=>u.unit.Trim().ToUpperInvariant()).Distinct().Count()!=units.Length)
            throw new InvalidDataException("РџРµСЂРµРІС–СЂС‚Рµ РїС–РґСЂРѕР·РґС–Р»Рё Р№ РІРµСЂСЃС–СЋ РґРѕР·РІРѕР»С–РІ.");
        if(isMvo && (units.Length!=1 || !canConfirmInvoices || autoPostInvoices) || !isMvo && (canConfirmInvoices || !autoPostInvoices))
            throw new InvalidDataException("РњР’Рћ РїС–РґС‚РІРµСЂРґР¶СѓС” РЅР°РєР»Р°РґРЅС– СЃРІРѕРіРѕ РїС–РґСЂРѕР·РґС–Р»Сѓ; С–РЅС€С– РєРѕСЂРёСЃС‚СѓРІР°С‡С– РѕС‚СЂРёРјСѓСЋС‚СЊ С—С… Р°РІС‚РѕРјР°С‚РёС‡РЅРѕ.");
        if((canConfirmInvoices || autoPostInvoices) && !canViewDocuments)throw new InvalidDataException("Р”Р»СЏ РЅР°РєР»Р°РґРЅРёС… РїРѕС‚СЂС–Р±РµРЅ РґРѕСЃС‚СѓРї РґРѕ РґРѕРєСѓРјРµРЅС‚С–РІ.");
    }
    public bool Allows(int location,bool assets=false)=>units.Any(u=>u.locationId==location) && (assets?canViewAssets:canViewDocuments);
}
