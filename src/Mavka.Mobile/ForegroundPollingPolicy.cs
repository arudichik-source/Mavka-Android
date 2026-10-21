namespace Mavka.Mobile;
internal static class ForegroundPollingPolicy
{
    public static int Minutes(int value)=>value is 1 or 2 or 5 or 10?value:2;
    public static bool CanRun(bool enabled,bool resumed,bool busy)=>enabled && resumed && !busy;
    public static TimeSpan Delay(int minutes,int failures)=>TimeSpan.FromMinutes(Math.Min(10,Minutes(minutes)*Math.Pow(2,Math.Clamp(failures,0,4))));
}
