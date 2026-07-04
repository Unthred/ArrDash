using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Metrics;

public class StuckProcessDisplayTests
{
    [Fact]
    public void Create_classifies_docker_container()
    {
        var stuck = StuckProcessDisplay.Create(42, "Plex Transcoder", "plex", "/usr/lib/plex/Plex Transcoder");

        Assert.Equal(StuckProcessKind.DockerContainer, stuck.Kind);
        Assert.Equal("plex", stuck.DisplayName);
        Assert.Equal("plex", stuck.ContainerName);
        Assert.Equal("Plex Transcoder", stuck.ProcessName);
    }

    [Fact]
    public void Create_classifies_kernel_worker()
    {
        var stuck = StuckProcessDisplay.Create(7, "[kworker/u16:1-flush-btrfs-(null)]", null, null);

        Assert.Equal(StuckProcessKind.KernelWorker, stuck.Kind);
        Assert.Equal("Often expected", StuckProcessDisplay.ExpectedSeverity(stuck.Kind));
    }

    [Fact]
    public void Create_classifies_system_process()
    {
        var stuck = StuckProcessDisplay.Create(99, "shfs", null, "shfs /mnt/user -o max_read=131072");

        Assert.Equal(StuckProcessKind.SystemProcess, stuck.Kind);
        Assert.Equal("shfs", stuck.DisplayName);
    }

    [Fact]
    public void TrimCommandLine_truncates_long_values()
    {
        var longCommand = new string('a', 200);
        var trimmed = StuckProcessDisplay.TrimCommandLine(longCommand);

        Assert.NotNull(trimmed);
        Assert.True(trimmed!.Length <= 161);
        Assert.EndsWith("…", trimmed);
    }
}
