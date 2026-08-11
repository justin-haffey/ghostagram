using System.Text.Json;

namespace Ghostagram.Server;

/// <summary>Small local-first persistence with the document, change log, and idempotency ledger in one committed file.</summary>
public sealed class FileDocumentStore : IDocumentStore, IDocumentCatalog
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _directory;

    public FileDocumentStore(IHostEnvironment environment)
        : this(Path.Combine(environment.ContentRootPath, "App_Data", "ghostagram-documents"))
    {
    }

    /// <summary>Creates a file store at an explicit directory for isolated hosts and persistence verification.</summary>
    public FileDocumentStore(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        _directory = Path.GetFullPath(storageDirectory);
    }

    public async Task<StoredDocument?> LoadAsync(string documentId, CancellationToken cancellationToken)
    {
        var path = PathFor(DocumentIdRules.Require(documentId));
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<StoredDocument>(stream, SerializerOptions, cancellationToken);
    }

    public async Task SaveAsync(StoredDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var target = PathFor(DocumentIdRules.Require(document.DocumentId));
        var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<IReadOnlyList<DiagramDocumentSummary>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_directory)) return [];
        var results = new List<DiagramDocumentSummary>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var document = await JsonSerializer.DeserializeAsync<StoredDocument>(stream, SerializerOptions, cancellationToken);
                if (document is null) continue;
                results.Add(new(
                    document.DocumentId,
                    string.IsNullOrWhiteSpace(document.DisplayName) ? document.DocumentId : document.DisplayName,
                    document.Revision,
                    document.UpdatedUtc == default ? File.GetLastWriteTimeUtc(path) : document.UpdatedUtc));
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return results.OrderByDescending(item => item.UpdatedUtc).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task<bool> DeleteAsync(string documentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = PathFor(DocumentIdRules.Require(documentId));
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    private string PathFor(string documentId) => Path.Combine(_directory, $"{documentId}.json");
}
