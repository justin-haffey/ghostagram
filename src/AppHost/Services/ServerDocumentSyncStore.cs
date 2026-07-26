using Diagrams.Core.Abstractions;
using Diagrams.Core.Models;
using Editor.Client.Services;

namespace AppHost.Services;

public sealed class ServerDocumentSyncStore(IDocumentSerializer serializer, IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _documentsPath = Path.Combine(environment.ContentRootPath, "App_Data", "sync-documents");

    public async Task<SyncResult> PullAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetDocumentPath(documentId);
            if (!File.Exists(path))
            {
                return new SyncResult(false, false, 0, null, "Document does not exist on the server.");
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var document = serializer.Deserialize(json);
            return new SyncResult(true, false, document.Revision, document, "Pulled latest server revision.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SyncResult> PushAsync(string documentId, SyncPushRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_documentsPath);

            var path = GetDocumentPath(documentId);
            DiagramDocument? existing = null;
            if (File.Exists(path))
            {
                var existingJson = await File.ReadAllTextAsync(path, cancellationToken);
                existing = serializer.Deserialize(existingJson);
            }

            if (existing is not null && request.KnownServerRevision != existing.Revision)
            {
                return new SyncResult(false, true, existing.Revision, existing, "Server revision conflict detected.");
            }

            var nextRevision = Math.Max(existing?.Revision ?? 0, request.Document.Revision) + 1;
            var saved = request.Document with
            {
                Revision = nextRevision,
                Metadata = request.Document.Metadata with
                {
                    UpdatedUtc = DateTimeOffset.UtcNow
                }
            };

            await File.WriteAllTextAsync(path, serializer.Serialize(saved), cancellationToken);
            return new SyncResult(true, false, saved.Revision, saved, "Document synced to server.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetDocumentPath(string documentId)
        => Path.Combine(_documentsPath, $"{documentId}.json");
}
