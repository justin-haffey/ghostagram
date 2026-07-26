using Diagrams.Core.Models;

namespace Diagrams.Core.Abstractions;

public interface IDocumentSerializer
{
    string Serialize(DiagramDocument document);
    DiagramDocument Deserialize(string json);
}

public interface IDocumentCatalogRepository
{
    Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(CancellationToken cancellationToken = default);
    Task<DiagramDocument?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<DiagramDocument> SaveDocumentAsync(DiagramDocument document, CancellationToken cancellationToken = default);
    Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task DuplicateDocumentAsync(string documentId, string title, CancellationToken cancellationToken = default);
    Task RenameDocumentAsync(string documentId, string title, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(string documentId, bool isFavorite, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentSnapshotSummary>> ListSnapshotsAsync(string documentId, CancellationToken cancellationToken = default);
    Task<DocumentSnapshotSummary> SaveSnapshotAsync(string documentId, string name, DiagramDocument document, CancellationToken cancellationToken = default);
    Task<DiagramDocument?> RestoreSnapshotAsync(string documentId, string snapshotId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LibraryItemDefinition>> ListLibraryItemsAsync(CancellationToken cancellationToken = default);
    Task SaveLibraryItemAsync(LibraryItemDefinition libraryItem, CancellationToken cancellationToken = default);
    Task DeleteLibraryItemAsync(string libraryItemId, CancellationToken cancellationToken = default);
}

public interface IDiagramLayoutEngine
{
    DiagramDocument ApplyLayout(DiagramDocument document, LayoutSpec spec, string? groupId = null);
    IReadOnlyList<LayoutDescriptor> GetSupportedLayouts();
}

public sealed record LayoutDescriptor(LayoutKind Kind, string DisplayName, bool SupportsGroups);

public interface IDocumentSyncService
{
    Task<SyncResult> PushAsync(DiagramDocument document, long knownServerRevision, CancellationToken cancellationToken = default);
    Task<SyncResult> PullAsync(string documentId, CancellationToken cancellationToken = default);
}

public interface ICommandHistory
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    void Reset(DiagramDocument document);
    void Record(DiagramDocument previous, DiagramDocument current);
    DiagramDocument? Undo();
    DiagramDocument? Redo();
}

public interface IValidationEngine
{
    IReadOnlyList<ValidationIssue> Validate(DiagramDocument document);
}
