using Microsoft.AspNetCore.Components.Server.Circuits;

namespace ArrDash.Services;

public sealed class DashboardCircuitNotifier
{
    public event Action? CircuitConnected;

    internal void NotifyCircuitConnected() => CircuitConnected?.Invoke();
}

public sealed class DashboardCircuitHandler(DashboardCircuitNotifier notifier) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        notifier.NotifyCircuitConnected();
        return base.OnConnectionUpAsync(circuit, cancellationToken);
    }
}
