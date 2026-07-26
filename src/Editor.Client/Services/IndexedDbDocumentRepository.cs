using System.Text.Json;
using System.Text.Json.Serialization;
using Diagrams.Core.Abstractions;
using Diagrams.Core.Models;
using Microsoft.JSInterop;

namespace Editor.Client.Services;

public sealed class BrowserDocumentCatalogRepository(
    IJSRuntime jsRuntime,
    IDocumentSerializer serializer) : IDocumentCatalogRepository, IAsyncDisposable
{
    private const string ModulePath = "./js/document-storage.js";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private IJSObjectReference? _module;

    /// <summary>Reads document summaries from IndexedDB and orders them by most recent update.</summary>
    public async Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var payload = await InvokeAsync<string[]>("listDocuments", cancellationToken);
        return payload.Select(Deserialize<DocumentSummary>).OrderByDescending(summary => summary.UpdatedUtc).ToArray();
    }

    public async Task<DiagramDocument?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var json = await InvokeAsync<string?>("getDocument", cancellationToken, documentId);
        return string.IsNullOrWhiteSpace(json) ? null : serializer.Deserialize(json);
    }

    public async Task<DiagramDocument> SaveDocumentAsync(DiagramDocument document, CancellationToken cancellationToken = default)
    {
        await InvokeVoidAsync("saveDocument", cancellationToken, document.DocumentId, serializer.Serialize(document), Serialize(document.ToSummary()));
        return document;
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
        => await InvokeVoidAsync("deleteDocument", cancellationToken, documentId);

    public async Task DuplicateDocumentAsync(string documentId, string title, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
        {
            // Missing IDs are treated as a no-op so a stale catalog entry cannot create a new document.
            return;
        }

        var duplicated = document with
        {
            DocumentId = $"doc-{Guid.NewGuid():N}",
            Revision = 0,
            IsFavorite = false,
            Metadata = DiagramMetadata.Create(title, document.Metadata.Description)
        };

        await SaveDocumentAsync(duplicated, cancellationToken);
    }

    public async Task RenameDocumentAsync(string documentId, string title, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
        {
            return;
        }

        var renamed = document with
        {
            Metadata = document.Metadata with { Title = title }
        };

        await SaveDocumentAsync(renamed.Touch(), cancellationToken);
    }

    public async Task SetFavoriteAsync(string documentId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
        {
            return;
        }

        await SaveDocumentAsync(document with { IsFavorite = isFavorite }, cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentSnapshotSummary>> ListSnapshotsAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var payload = await InvokeAsync<string[]>("listSnapshots", cancellationToken, documentId);
        return payload.Select(Deserialize<DocumentSnapshotSummary>).OrderByDescending(snapshot => snapshot.CreatedUtc).ToArray();
    }

    public async Task<DocumentSnapshotSummary> SaveSnapshotAsync(string documentId, string name, DiagramDocument document, CancellationToken cancellationToken = default)
    {
        var summary = new DocumentSnapshotSummary(
            $"snapshot-{Guid.NewGuid():N}",
            documentId,
            name,
            DateTimeOffset.UtcNow,
            document.Revision);

        await InvokeVoidAsync("saveSnapshot", cancellationToken, documentId, summary.SnapshotId, Serialize(summary), serializer.Serialize(document));
        return summary;
    }

    public async Task<DiagramDocument?> RestoreSnapshotAsync(string documentId, string snapshotId, CancellationToken cancellationToken = default)
    {
        var json = await InvokeAsync<string?>("getSnapshotDocument", cancellationToken, documentId, snapshotId);
        return string.IsNullOrWhiteSpace(json) ? null : serializer.Deserialize(json);
    }

    public async Task<IReadOnlyList<LibraryItemDefinition>> ListLibraryItemsAsync(CancellationToken cancellationToken = default)
    {
        var payload = await InvokeAsync<string[]>("listLibraryItems", cancellationToken);
        return payload.Select(Deserialize<LibraryItemDefinition>).OrderBy(item => item.Name).ToArray();
    }

    public async Task SaveLibraryItemAsync(LibraryItemDefinition libraryItem, CancellationToken cancellationToken = default)
        => await InvokeVoidAsync("saveLibraryItem", cancellationToken, libraryItem.Id, Serialize(libraryItem));

    public async Task DeleteLibraryItemAsync(string libraryItemId, CancellationToken cancellationToken = default)
        => await InvokeVoidAsync("deleteLibraryItem", cancellationToken, libraryItemId);

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }

    private async Task<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        var module = await EnsureModuleAsync(cancellationToken);
        return await module.InvokeAsync<T>(identifier, cancellationToken, args);
    }

    private async Task InvokeVoidAsync(string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        var module = await EnsureModuleAsync(cancellationToken);
        await module.InvokeVoidAsync(identifier, cancellationToken, args);
    }

    private async Task<IJSObjectReference> EnsureModuleAsync(CancellationToken cancellationToken)
        => _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    private static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidOperationException($"Unable to deserialize '{typeof(T).Name}'.");
}
