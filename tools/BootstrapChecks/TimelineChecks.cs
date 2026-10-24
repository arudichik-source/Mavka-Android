using System.Text.Json;
using System.Text.Json.Nodes;
using Mavka.Mobile;

internal static class TimelineChecks
{
    internal static void Run()
    {
        var count=0;var head=new DateOnly(2026,10,20);
        void Equal(decimal expected,JsonNode value,DateOnly date)
        {using var json=JsonDocument.Parse(value.ToJsonString());if(AssetBalanceTimeline.Quantity(json.RootElement,date,head)!=expected)throw new Exception("Historical quantity mismatch");count++;}
        void Invalid(JsonNode value,DateOnly date)
        {using var json=JsonDocument.Parse(value.ToJsonString());try{AssetBalanceTimeline.Quantity(json.RootElement,date,head);}catch(InvalidDataException){count++;return;}throw new Exception("Invalid timeline accepted");}
        JsonNode Asset(decimal quantity,decimal opening=0,bool complete=true)=>JsonNode.Parse(JsonSerializer.Serialize(new{quantity,history=new{version=1,from="2026-10-01",complete,opening,changes=new[]{new{date="2026-10-01",delta=10.123456789012345678m},new{date="2026-10-20",delta=-3.123456789012345678m}}}}))!;
        var asset=Asset(7);Equal(0,asset,new(2026,9,30));Equal(10.123456789012345678m,asset,new(2026,10,1));Equal(10.123456789012345678m,asset,new(2026,10,19));Equal(7,asset,head);
        Invalid(asset,head.AddDays(1));Invalid(Asset(8),head);
        var zero=asset.DeepClone();zero["quantity"]=0;zero["history"]!["changes"]![1]!["delta"]=-10.123456789012345678m;
        Equal(10.123456789012345678m,zero,new(2026,10,19));Equal(0,zero,head);
        var correction=asset.DeepClone();correction["quantity"]=-2;correction["history"]!["changes"]![1]!["delta"]=-12.123456789012345678m;Equal(-2,correction,head);
        var trimmed=Asset(12,5,false);Equal(15.123456789012345678m,trimmed,new(2026,10,19));Invalid(trimmed,new(2026,9,30));
        Invalid(Asset(12,5),head);
        foreach(var mutate in new Action<JsonNode>[] {
            n=>n["history"]!["version"]=2,n=>n["history"]!["from"]="2026-10-21",
            n=>n["history"]!["changes"]![1]!["date"]="2026-10-01",n=>n["history"]!["changes"]![1]!["date"]="2026-10-21",
            n=>n["history"]!["changes"]![0]!["date"]="not-a-date",n=>n["history"]!["changes"]![0]!["delta"]="oops",
            n=>n["history"]!["changes"]=new JsonArray(Enumerable.Range(0,513).Select(_=>(JsonNode)new JsonObject{["date"]="2026-10-01",["delta"]=0}).ToArray())})
        {var invalid=asset.DeepClone();mutate(invalid);Invalid(invalid,head);}
        var legacy=new JsonObject{["quantity"]=9};Equal(9,legacy,head);Invalid(legacy,head.AddDays(-1));
        var empty=Asset(0);empty["history"]!["changes"]=new JsonArray();Equal(0,empty,head);
        Console.WriteLine($"PASS {count} exact PC timeline, old-package, zero-current, corrections and invalid-history checks");
    }
}
