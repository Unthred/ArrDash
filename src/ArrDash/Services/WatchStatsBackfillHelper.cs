namespace ArrDash.Services;

public static class WatchStatsBackfillHelper
{
    public static int Resolve(int prefDays, int defaultDays)
    {
        var days = prefDays <= 0 ? defaultDays : prefDays;
        return Math.Clamp(days, 1, 730);
    }

    public static int ResolveRetention(int prefDays, int defaultDays)
    {
        var days = prefDays <= 0 ? defaultDays : prefDays;
        return Math.Clamp(days, 30, 1825);
    }
}
