using ArrDash.Models;

namespace ArrDash.Services;

public static class StuckProcessDisplay
{
    private const int MaxCommandLineLength = 160;

    public static StuckProcess Create(int pid, string comm, string? containerName, string? commandLine)
    {
        var trimmedCommand = TrimCommandLine(commandLine);

        if (!string.IsNullOrWhiteSpace(containerName))
        {
            return new StuckProcess(
                pid,
                containerName,
                StuckProcessKind.DockerContainer,
                comm,
                containerName,
                trimmedCommand);
        }

        if (IsKernelThreadName(comm))
        {
            return new StuckProcess(
                pid,
                comm,
                StuckProcessKind.KernelWorker,
                comm,
                null,
                trimmedCommand);
        }

        return new StuckProcess(
            pid,
            comm,
            StuckProcessKind.SystemProcess,
            comm,
            null,
            trimmedCommand);
    }

    public static string KindLabel(StuckProcessKind kind) => kind switch
    {
        StuckProcessKind.DockerContainer => "Docker",
        StuckProcessKind.KernelWorker => "Kernel worker",
        _ => "System"
    };

    public static string KindGuidance(StuckProcessKind kind) => kind switch
    {
        StuckProcessKind.DockerContainer =>
            "Stop or restart this container from the Unraid Docker page once storage calms down.",
        StuckProcessKind.KernelWorker =>
            "Usually normal during parity, mover, or heavy array I/O — brief blocks are expected.",
        _ =>
            "Host daemon (often shfs, md, or btrfs). Usually clears when the disk catches up."
    };

    public static string ExpectedSeverity(StuckProcessKind kind) => kind switch
    {
        StuckProcessKind.KernelWorker => "Often expected",
        StuckProcessKind.DockerContainer => "Investigate container",
        _ => "Worth watching"
    };

    internal static bool IsKernelThreadName(string comm) =>
        comm.Length >= 2 && comm[0] == '[' && comm[^1] == ']';

    public static string? TrimCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;

        var normalized = commandLine.Trim();
        if (normalized.Length <= MaxCommandLineLength)
            return normalized;

        return normalized[..MaxCommandLineLength] + "…";
    }
}
