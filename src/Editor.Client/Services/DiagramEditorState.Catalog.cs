using Diagrams.Core.Models;

namespace Editor.Client.Services;

public sealed partial class DiagramEditorState
{
    public async Task EnsureCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_catalogInitialized)
        {
            return;
        }

        await RefreshCatalogAsync(cancellationToken);
        _catalogInitialized = true;
        NotifyChanged(save: false);
    }

    public async Task InitializeAsync(string? documentId = null, CancellationToken cancellationToken = default)
    {
        await EnsureCatalogAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(documentId))
        {
            await OpenDocumentAsync(documentId, cancellationToken);
            return;
        }

        if (_store is not null)
        {
            return;
        }

        var summary = Documents.OrderByDescending(item => item.IsFavorite)
            .ThenByDescending(item => item.UpdatedUtc)
            .FirstOrDefault();

        if (summary is not null)
        {
            await OpenDocumentAsync(summary.DocumentId, cancellationToken);
            return;
        }

        await CreateNewAsync(TemplateKind.Flowchart, cancellationToken: cancellationToken);
    }

    public async Task RefreshCatalogAsync(CancellationToken cancellationToken = default)
    {
        Documents = await repository.ListDocumentsAsync(cancellationToken);
        LibraryItems = await repository.ListLibraryItemsAsync(cancellationToken);

        if (_store is not null)
        {
            Snapshots = await repository.ListSnapshotsAsync(Document.DocumentId, cancellationToken);
            ValidationIssues = validationEngine.Validate(Document);
        }
    }

    public async Task OpenDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
        {
            return;
        }

        LoadDocument(document);
        await RefreshCatalogAsync(cancellationToken);
        SyncStatus = ServerSyncEnabled ? $"Linked to server rev {KnownServerRevision}" : "Local-first";
        NotifyChanged(save: false);
    }

    public async Task CreateNewAsync(TemplateKind kind, string? title = null, CancellationToken cancellationToken = default)
    {
        var definition = templates.Get(kind);
        var created = templates.Create(kind) with
        {
            DocumentId = $"doc-{Guid.NewGuid():N}",
            Revision = 0,
            Metadata = DiagramMetadata.Create(string.IsNullOrWhiteSpace(title) ? $"{definition.DisplayName} Diagram" : title)
        };

        LoadDocument(created);
        await SaveAsync(cancellationToken);
    }

    public async Task ImportJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        var imported = serializer.Deserialize(json) with
        {
            DocumentId = $"doc-{Guid.NewGuid():N}",
            Revision = 0
        };

        LoadDocument(imported);
        await SaveAsync(cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var saved = await repository.SaveDocumentAsync(Document, cancellationToken);
        ReplaceStore(saved, resetHistory: false);
        await RefreshCatalogAsync(cancellationToken);

        if (ServerSyncEnabled)
        {
            await PushToServerAsync(cancellationToken);
        }
        else
        {
            SyncStatus = "Saved locally";
        }

        NotifyChanged(save: false);
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await repository.DeleteDocumentAsync(documentId, cancellationToken);
        if (string.Equals(Document.DocumentId, documentId, StringComparison.OrdinalIgnoreCase))
        {
            _store = null;
        }

        await RefreshCatalogAsync(cancellationToken);
        NotifyChanged(save: false);
    }

    public async Task DuplicateDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var summary = Documents.FirstOrDefault(item => item.DocumentId == documentId);
        var title = summary is null ? "Duplicated Diagram" : $"{summary.Title} Copy";
        await repository.DuplicateDocumentAsync(documentId, title, cancellationToken);
        await RefreshCatalogAsync(cancellationToken);
        NotifyChanged(save: false);
    }

    public async Task RenameCurrentAsync(string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var updated = Mutate(document => document with
        {
            Metadata = document.Metadata with { Title = title }
        });

        await repository.RenameDocumentAsync(updated.DocumentId, title, cancellationToken);
        await RefreshCatalogAsync(cancellationToken);
        NotifyChanged(save: false);
    }

    public async Task SetFavoriteAsync(string documentId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        if (string.Equals(Document.DocumentId, documentId, StringComparison.OrdinalIgnoreCase))
        {
            Mutate(document => document with { IsFavorite = isFavorite });
        }

        await repository.SetFavoriteAsync(documentId, isFavorite, cancellationToken);
        await RefreshCatalogAsync(cancellationToken);
        NotifyChanged(save: false);
    }

    public async Task SaveSnapshotAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await repository.SaveSnapshotAsync(Document.DocumentId, name, Document, cancellationToken);
        Snapshots = await repository.ListSnapshotsAsync(Document.DocumentId, cancellationToken);
        NotifyChanged(save: false);
    }

    public async Task RestoreSnapshotAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        var restored = await repository.RestoreSnapshotAsync(Document.DocumentId, snapshotId, cancellationToken);
        if (restored is null)
        {
            return;
        }

        LoadDocument(restored with { DocumentId = Document.DocumentId });
        await SaveAsync(cancellationToken);
    }

    public async Task PushToServerAsync(CancellationToken cancellationToken = default)
    {
        var result = await syncService.PushAsync(Document, KnownServerRevision, cancellationToken);
        if (result.IsSuccess && result.Document is not null)
        {
            KnownServerRevision = result.ServerRevision;
            ReplaceStore(result.Document, resetHistory: false);
            SyncStatus = $"Synced to server rev {result.ServerRevision}";
            await repository.SaveDocumentAsync(result.Document, cancellationToken);
            await RefreshCatalogAsync(cancellationToken);
        }
        else
        {
            SyncStatus = result.Message;
        }

        NotifyChanged(save: false);
    }

    public async Task PullFromServerAsync(CancellationToken cancellationToken = default)
    {
        var result = await syncService.PullAsync(Document.DocumentId, cancellationToken);
        if (result.IsSuccess && result.Document is not null)
        {
            KnownServerRevision = result.ServerRevision;
            LoadDocument(result.Document);
            await repository.SaveDocumentAsync(result.Document, cancellationToken);
            await RefreshCatalogAsync(cancellationToken);
            SyncStatus = $"Pulled server rev {result.ServerRevision}";
        }
        else
        {
            SyncStatus = result.Message;
        }

        NotifyChanged(save: false);
    }
}
