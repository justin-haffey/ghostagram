using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Ghostagram.Server;

/// <summary>
/// Bridges Blazor circuit connectivity into the Laboratory's process-local browser presence.
/// Disconnected circuits are removed immediately instead of lingering for the framework's
/// reconnection-retention window; a reconnected circuit can subscribe again at its current revision.
/// </summary>
internal sealed class LaboratoryCircuitState : CircuitHandler
{
    public event Func<bool, CancellationToken, Task>? ConnectionChanged;

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        => NotifyAsync(true, cancellationToken);

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        => NotifyAsync(false, cancellationToken);

    private async Task NotifyAsync(bool connected, CancellationToken cancellationToken)
    {
        var handlers = ConnectionChanged;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<bool, CancellationToken, Task>>())
        {
            try
            {
                await handler(connected, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (ObjectDisposedException) { }
            catch
            {
                // Presence is diagnostic; a view lifecycle callback must not destabilize the circuit.
            }
        }
    }
}
