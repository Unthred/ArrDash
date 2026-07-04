using ArrDash.Models;

namespace ArrDash.Services;

public static class ServiceHealthSnapshot
{
    public static ServiceHealth WithAttention(ServiceHealth health, ServiceWorkload? workload = null)
    {
        var merged = workload is not null ? health with { Workload = workload } : health;
        var attention = ResolveAttention(merged);
        return merged with
        {
            Attention = attention,
            AttentionLabel = ArrServiceDetailParser.AttentionLabelFor(attention)
        };
    }

    private static ServiceAttentionLevel ResolveAttention(ServiceHealth health)
    {
        if (!health.Configured)
            return ServiceAttentionLevel.NotConfigured;

        if (!health.Online)
            return ServiceAttentionLevel.Offline;

        if (health.Attention is ServiceAttentionLevel.NeedsAttention)
            return ServiceAttentionLevel.NeedsAttention;

        if (health.Workload is { Kind: not ServiceWorkloadKind.None })
            return ServiceAttentionLevel.Busy;

        return health.Attention is ServiceAttentionLevel.Healthy or ServiceAttentionLevel.Busy
            ? health.Attention
            : ServiceAttentionLevel.Healthy;
    }
}
