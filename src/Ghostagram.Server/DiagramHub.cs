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

public sealed class SignalRDocumentEventPublisher(IHubContext<DiagramHub> hub) : IDocumentEventPublisher
{
    public Task PublishCommittedAsync(DiagramChange change, CancellationToken cancellationToken)
        => hub.Clients.Group(DiagramHub.Group(change.DocumentId)).SendAsync("document.changed", change, cancellationToken);
}
