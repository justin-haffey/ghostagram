using System.Net.Http.Json;
using Diagrams.Core.Abstractions;
using Diagrams.Core.Models;

namespace Editor.Client.Services;

public sealed record SyncPushRequest(long KnownServerRevision, DiagramDocument Document);

public sealed class HttpDocumentSyncService(HttpClient httpClient) : IDocumentSyncService
{
    public async Task<SyncResult> PushAsync(DiagramDocument document, long knownServerRevision, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"api/sync/documents/{document.DocumentId}",
            new SyncPushRequest(knownServerRevision, document),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SyncResult>(cancellationToken: cancellationToken)
            ?? new SyncResult(false, true, 0, null, "Server returned an empty sync response.");
    }

    public async Task<SyncResult> PullAsync(string documentId, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<SyncResult>($"api/sync/documents/{documentId}", cancellationToken)
            ?? new SyncResult(false, true, 0, null, "Server returned an empty pull response.");
    }
}
