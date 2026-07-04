using System.Text.Json;
using ArrDash.Models;

namespace ArrDash.Services;

public static class StreamingWorkloadHelper
{
    public static ServiceWorkload? FromTranscodeCount(int count) =>
        count > 0
            ? new ServiceWorkload(ServiceWorkloadKind.Transcoding, $"Transcoding ({count})", count)
            : null;

    public static bool IsEmbyFamilyTranscoding(JsonElement session)
    {
        if (!session.TryGetProperty("TranscodingInfo", out var info) ||
            info.ValueKind != JsonValueKind.Object)
            return false;

        if (info.TryGetProperty("IsVideoDirect", out var direct) && direct.ValueKind == JsonValueKind.True)
            return false;

        if (info.TryGetProperty("IsAudioDirect", out var audioDirect) &&
            audioDirect.ValueKind == JsonValueKind.True &&
            info.TryGetProperty("IsVideoDirect", out var videoDirect) &&
            videoDirect.ValueKind == JsonValueKind.True)
            return false;

        return true;
    }
}
