namespace Mavka.Mobile;
internal static class ForegroundPollingPolicy
{
    public static readonly int[] Intervals=[1,2,5,10,15,30,60,120,240];
    public static int Minutes(int value)=>Intervals.Contains(value)?value:60;
    public static bool CanRun(bool enabled,bool resumed,bool busy)=>enabled && resumed && !busy;
    public static TimeSpan Delay(int minutes,int failures)=>TimeSpan.FromMinutes(Math.Min(1440,Minutes(minutes)*Math.Pow(2,Math.Clamp(failures,0,4))));
}
