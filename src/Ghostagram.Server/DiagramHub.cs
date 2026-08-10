using System.Collections.Concurrent;
using Ghostagram.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace Ghostagram.Server;

/// <summary>Transient delivery only. Documents remain in IDocumentStore.</summary>
public sealed class DiagramHub : Hub
{
    public Task JoinDocument(string documentId)
        => Groups.AddToGroupAsync(Context.ConnectionId, Group(documentId));

    public Task LeaveDocument(string documentId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(documentId));

    internal static string Group(string documentId) => $"diagram:{DocumentIdRules.Require(documentId)}";
}

public interface IDocumentEventPublisher
{
    Task PublishCommittedAsync(DiagramChange change, CancellationToken cancellationToken);
}

/// <summary>Presence and rendered-revision information for integrated Laboratory browser views.</summary>
public sealed record DocumentBrowserPresence(int ViewCount, long? OldestRevision, long? LatestRevision);

public interface IDocumentChangeNotifier
{
    IDisposable Subscribe(string documentId, long currentRevision, Func<DiagramChange, CancellationToken, Task> handler);
    DocumentBrowserPresence GetPresence(string documentId);
}

/// <summary>
/// Process-local bridge from authoritative commits into interactive server-rendered Laboratory circuits.
/// SignalR remains the transport for external clients; this bridge gives the integrated Blazor host a
/// bounded render acknowledgement that agents can inspect through MCP.
/// </summary>
public sealed class DocumentChangeNotifier : IDocumentChangeNotifier
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Subscription>> _subscriptions = new(StringComparer.Ordinal);

    public IDisposable Subscribe(string documentId, long currentRevision, Func<DiagramChange, CancellationToken, Task> handler)
    {
        DocumentIdRules.Require(documentId);
        ArgumentNullException.ThrowIfNull(handler);
        if (currentRevision < 0) throw new ArgumentOutOfRangeException(nameof(currentRevision));
        var id = Guid.NewGuid();
        var subscription = new Subscription(this, documentId, id, currentRevision, handler);
        _subscriptions.GetOrAdd(documentId, _ => new()).TryAdd(id, subscription);
        return subscription;
    }

    public DocumentBrowserPresence GetPresence(string documentId)
    {
        DocumentIdRules.Require(documentId);
        if (!_subscriptions.TryGetValue(documentId, out var subscriptions) || subscriptions.IsEmpty)
            return new(0, null, null);
        var revisions = subscriptions.Values.Select(subscription => subscription.Revision).ToArray();
        return new(revisions.Length, revisions.Min(), revisions.Max());
    }

    public async Task NotifyAsync(DiagramChange change, CancellationToken cancellationToken)
    {
        if (!_subscriptions.TryGetValue(change.DocumentId, out var subscriptions) || subscriptions.IsEmpty) return;
        var deliveries = subscriptions.Values.Select(subscription => DeliverAsync(subscription, change, cancellationToken));
        await Task.WhenAll(deliveries);
    }

    /// <summary>
    /// Queues a browser notification without placing Laboratory rendering on the authoritative command path.
    /// Each view coalesces a burst to the latest pending revision while preserving its current render.
    /// </summary>
    public void Notify(DiagramChange change)
    {
        if (!_subscriptions.TryGetValue(change.DocumentId, out var subscriptions) || subscriptions.IsEmpty) return;
        foreach (var subscription in subscriptions.Values) subscription.Enqueue(change);
    }

    private static async Task DeliverAsync(Subscription subscription, DiagramChange change, CancellationToken cancellationToken)
    {
        try
        {
            await subscription.Handler(change, cancellationToken);
            subscription.Advance(change.Revision);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
        catch
        {
            // A failed or disconnected browser must not roll back an already durable commit.
            // Its acknowledged revision intentionally stays stale for agent-side verification.
        }
    }

    private void Remove(string documentId, Guid id)
    {
        if (!_subscriptions.TryGetValue(documentId, out var subscriptions)) return;
        subscriptions.TryRemove(id, out _);
        if (subscriptions.IsEmpty) _subscriptions.TryRemove(new KeyValuePair<string, ConcurrentDictionary<Guid, Subscription>>(documentId, subscriptions));
    }

    private sealed class Subscription(
        DocumentChangeNotifier owner,
        string documentId,
        Guid id,
        long currentRevision,
        Func<DiagramChange, CancellationToken, Task> handler) : IDisposable
    {
        private readonly object _deliveryGate = new();
        private long _revision = currentRevision;
        private int _disposed;
        private DiagramChange? _pending;
        private bool _pumping;

        public Func<DiagramChange, CancellationToken, Task> Handler { get; } = handler;
        public long Revision => Volatile.Read(ref _revision);

        public void Advance(long revision)
        {
            long current;
            while (revision > (current = Volatile.Read(ref _revision)) &&
                   Interlocked.CompareExchange(ref _revision, revision, current) != current) { }
        }

        public void Enqueue(DiagramChange change)
        {
            lock (_deliveryGate)
            {
                if (_disposed != 0) return;
                if (_pending is null || change.Revision >= _pending.Revision) _pending = change;
                if (_pumping) return;
                _pumping = true;
            }

            _ = PumpAsync();
        }

        private async Task PumpAsync()
        {
            while (true)
            {
                DiagramChange? change;
                lock (_deliveryGate)
                {
                    if (_disposed != 0 || _pending is null)
                    {
                        _pending = null;
                        _pumping = false;
                        return;
                    }

                    change = _pending;
                    _pending = null;
                }

                await DeliverAsync(this, change, CancellationToken.None);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_deliveryGate) _pending = null;
            owner.Remove(documentId, id);
        }
    }
}

public sealed class SignalRDocumentEventPublisher(IHubContext<DiagramHub> hub, DocumentChangeNotifier notifier) : IDocumentEventPublisher
{
    public Task PublishCommittedAsync(DiagramChange change, CancellationToken cancellationToken)
    {
        notifier.Notify(change);
        return hub.Clients.Group(DiagramHub.Group(change.DocumentId)).SendAsync("document.changed", change, cancellationToken);
    }
}
