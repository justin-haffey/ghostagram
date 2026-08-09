namespace Ghostagram.Server;

public sealed record DiagramDocumentSummary(
    string DocumentId,
    string DisplayName,
    long Revision,
    DateTimeOffset UpdatedUtc);

/// <summary>Read-only document discovery kept separate from graph command authority.</summary>
public interface IDocumentCatalog
{
    Task<IReadOnlyList<DiagramDocumentSummary>> ListAsync(CancellationToken cancellationToken);
}
