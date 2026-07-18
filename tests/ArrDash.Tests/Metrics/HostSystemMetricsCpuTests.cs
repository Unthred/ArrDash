using ArrDash.Services;

namespace ArrDash.Tests.Metrics;

public sealed class HostSystemMetricsCpuTests : IDisposable
{
    private readonly string _procDir;
    private readonly string? _originalProcRoot;

    public HostSystemMetricsCpuTests()
    {
        _procDir = Path.Combine(Path.GetTempPath(), "arrdash-cpu-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_procDir);
        _originalProcRoot = Environment.GetEnvironmentVariable("ARRDASH_PROC_ROOT");
        Environment.SetEnvironmentVariable("ARRDASH_PROC_ROOT", _procDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ARRDASH_PROC_ROOT", _originalProcRoot);
        Directory.Delete(_procDir, recursive: true);
    }

    // /proc/stat "cpu " fields: user nice system idle iowait irq softirq steal guest guest_nice
    private void WriteStat(long user, long nice, long system, long idle, long iowait) =>
        File.WriteAllText(Path.Combine(_procDir, "stat"),
            $"cpu  {user} {nice} {system} {idle} {iowait} 0 0 0 0 0\n");

    [Fact]
    public void ReadCpuPercent_keeps_iowait_separate_from_genuine_busy_time()
    {
        var service = new HostSystemMetricsService(null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<HostSystemMetricsService>.Instance);

        // Baseline sample — first call always returns nulls while it establishes prev state.
        WriteStat(user: 100, nice: 0, system: 0, idle: 900, iowait: 0);
        var baseline = service.ReadCpuPercent();
        Assert.Null(baseline.CpuPercent);
        Assert.Null(baseline.IoWaitPercent);

        // Next sample: +100 user (genuine work), +0 idle, +100 iowait, over a total delta of 200.
        // Before the fix, iowait was folded into "busy" and this would have reported 100% CPU-busy.
        // After the fix: busy and iowait are each their own 50% share of the elapsed time.
        WriteStat(user: 200, nice: 0, system: 0, idle: 900, iowait: 100);
        var (cpuPercent, ioWaitPercent) = service.ReadCpuPercent();

        Assert.NotNull(cpuPercent);
        Assert.NotNull(ioWaitPercent);
        Assert.Equal(50.0, cpuPercent!.Value, precision: 1);
        Assert.Equal(50.0, ioWaitPercent!.Value, precision: 1);
    }

    [Fact]
    public void ReadCpuPercent_reports_high_iowait_when_host_is_stuck_on_disk()
    {
        var service = new HostSystemMetricsService(null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<HostSystemMetricsService>.Instance);

        WriteStat(user: 100, nice: 0, system: 0, idle: 900, iowait: 0);
        service.ReadCpuPercent();

        // Almost the entire delta is iowait, almost none is genuine compute — the exact "stuck on
        // disk, not CPU-bound" scenario this fix exists to distinguish.
        WriteStat(user: 110, nice: 0, system: 0, idle: 910, iowait: 480);
        var (cpuPercent, ioWaitPercent) = service.ReadCpuPercent();

        Assert.NotNull(cpuPercent);
        Assert.NotNull(ioWaitPercent);
        Assert.True(ioWaitPercent!.Value > 90, $"expected high iowait, got {ioWaitPercent.Value}");
        Assert.True(cpuPercent!.Value < 10, $"expected low genuine CPU busy, got {cpuPercent.Value}");
    }
}
