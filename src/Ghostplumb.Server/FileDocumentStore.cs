using System.Text.Json;

namespace Ghostplumb.Server;

/// <summary>Small local-first persistence with the document, change log, and idempotency ledger in one committed file.</summary>
public sealed class FileDocumentStore(IHostEnvironment environment) : IDocumentStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _directory = Path.Combine(environment.ContentRootPath, "App_Data", "ghostplumb-documents");

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

    private string PathFor(string documentId) => Path.Combine(_directory, $"{documentId}.json");
}
