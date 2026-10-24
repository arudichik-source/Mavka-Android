using System.Globalization;
using System.Text.Json;

namespace Mavka.Mobile;

// Read-only PC chronology. It is independent of the phone's MVO receipt ledger.
internal static class AssetBalanceTimeline
{
    internal static decimal Quantity(JsonElement asset,DateOnly selected,DateOnly? applied)
    {
        if(applied is null)throw new InvalidDataException("Не вказано дату отриманих даних ПК. Оновіть базу.");
        if(selected>applied.Value)throw new InvalidDataException($"Дані ПК отримано лише станом на {applied:dd.MM.yyyy}. Оновіть облік для пізнішої дати.");
        var current=asset.GetProperty("quantity").GetDecimal();
        if(!asset.TryGetProperty("history",out var history))
        {
            if(selected==applied)return current;
            throw new InvalidDataException("У цьому пакеті ще немає історії залишків. Оновіть головну Мавку та отримайте зміни з ПК. Поточні залишки збережено.");
        }
        try
        {
            DateOnly Date(string? value)=>DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:throw new InvalidDataException("Некоректна дата історії майна.");
            if(history.GetProperty("version").GetInt32()!=1)throw new InvalidDataException("Непідтримувана версія історії майна. Оновіть додаток.");
            var from=Date(history.GetProperty("from").GetString());var complete=history.GetProperty("complete").GetBoolean();
            var opening=history.GetProperty("opening").GetDecimal();var changes=history.GetProperty("changes");
            if(from>applied || changes.GetArrayLength()>512 || complete && opening!=0)throw new InvalidDataException("Некоректні межі історії майна.");
            var sum=opening;var result=opening;DateOnly? previous=null;
            foreach(var change in changes.EnumerateArray())
            {
                var day=Date(change.GetProperty("date").GetString());var delta=change.GetProperty("delta").GetDecimal();
                if(day<from || day>applied || previous is {} p && day<=p)throw new InvalidDataException("Порушено послідовність історії майна.");
                previous=day;sum=checked(sum+delta);if(day<=selected)result=checked(result+delta);
            }
            if(sum!=current)throw new InvalidDataException("Історія не збігається з отриманим залишком ПК. Оновіть дані.");
            if(selected<from)
            {if(complete)return 0;throw new InvalidDataException($"Для цього майна історія доступна з {from:dd.MM.yyyy}. Залишок за ранішу дату не підтверджений.");}
            return result;
        }
        catch(Exception error) when(error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {throw new InvalidDataException("Некоректна історія залишків ПК. Оновіть дані.",error);}
    }
}
