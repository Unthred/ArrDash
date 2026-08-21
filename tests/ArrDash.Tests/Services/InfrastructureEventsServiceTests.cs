using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public sealed class InfrastructureEventsServiceTests
{
    [Fact]
    public void Lifecycle_raised_then_cleared_moves_between_active_and_cleared()
    {
        var t0 = DateTimeOffset.Parse("2026-08-21T06:00:00Z");
        var t1 = DateTimeOffset.Parse("2026-08-21T06:05:00Z");
        var t2 = DateTimeOffset.Parse("2026-08-21T06:10:00Z");

        var events = new List<InfrastructureEventRecord>
        {
            Record(t0, "load_warning", InfrastructureEventLifecycle.Raised, InfrastructureEventSeverity.Warning, active: true),
            Record(t1, "load_warning", InfrastructureEventLifecycle.Updated, InfrastructureEventSeverity.Warning, active: true, count: 2),
            Record(t2, "load_warning", InfrastructureEventLifecycle.Cleared, InfrastructureEventSeverity.Warning, active: false),
        };

        var snap = InfrastructureEventAggregator.BuildSnapshot(events, t2.AddMinutes(1));

        Assert.Empty(snap.Active);
        Assert.Single(snap.RecentlyCleared);
        Assert.Equal(2, snap.RecentlyCleared[0].OccurrenceCount);
    }

    [Fact]
    public void Active_warning_not_cleared_by_stale_timeout_when_lifecycle_present()
    {
        var raised = DateTimeOffset.UtcNow.AddHours(-5);
        var events = new List<InfrastructureEventRecord>
        {
            Record(raised, "load_warning", InfrastructureEventLifecycle.Raised, InfrastructureEventSeverity.Warning, active: true),
        };

        var snap = InfrastructureEventAggregator.BuildSnapshot(events, DateTimeOffset.UtcNow);

        Assert.Single(snap.Active);
    }

    [Fact]
    public void Legacy_active_stales_after_two_hours_without_lifecycle()
    {
        var old = DateTimeOffset.UtcNow.AddHours(-3);
        var events = new List<InfrastructureEventRecord>
        {
            new(old, "tower-monitor", "load_warning", InfrastructureEventSeverity.Warning,
                InfrastructureEventLifecycle.Unknown, "Load Warning", "Load high", null,
                "tower-monitor:load_warning", null, old, old, true),
        };

        var snap = InfrastructureEventAggregator.BuildSnapshot(events, DateTimeOffset.UtcNow);

        Assert.Empty(snap.Active);
    }

    [Fact]
    public void Turbo_info_does_not_appear_in_active_badge()
    {
        var now = DateTimeOffset.UtcNow;
        var events = new List<InfrastructureEventRecord>
        {
            Record(now, "cpu_power_turbo_peak", InfrastructureEventLifecycle.Info, InfrastructureEventSeverity.Info, active: false),
            Record(now, "load_warning", InfrastructureEventLifecycle.Raised, InfrastructureEventSeverity.Warning, active: true),
        };

        var snap = InfrastructureEventAggregator.BuildSnapshot(events, now);

        Assert.Single(snap.Active);
        Assert.Equal("load_warning", snap.Active[0].Code);
        Assert.Contains(snap.InformationalHistory, e => e.Code == "cpu_power_turbo_peak");
    }

    [Fact]
    public void TryParseLine_reads_schema_v1()
    {
        var line = """
            {"schema":1,"ts":"2026-08-21T06:55:42Z","source":"tower-monitor","code":"load_warning","severity":"warning","lifecycle":"raised","title":"Load Warning","message":"Load avg 12.3","detail":"load1=12.3","instance":"tower-monitor:load_warning","count":1,"firstSeen":"2026-08-21T06:55:42Z","lastSeen":"2026-08-21T06:55:42Z","active":true}
            """;

        Assert.True(InfrastructureEventAggregator.TryParseLine(line, out var evt));
        Assert.Equal(InfrastructureEventLifecycle.Raised, evt.Lifecycle);
        Assert.Equal("Load Warning", evt.Title);
        Assert.Equal("tower-monitor:load_warning", evt.InstanceId);
    }

    private static InfrastructureEventRecord Record(
        DateTimeOffset at,
        string code,
        InfrastructureEventLifecycle lifecycle,
        InfrastructureEventSeverity severity,
        bool active,
        int count = 1) =>
        new(at, "tower-monitor", code, severity, lifecycle, code, $"{code} message", null,
            $"tower-monitor:{code}", count, at, at, active);
}
