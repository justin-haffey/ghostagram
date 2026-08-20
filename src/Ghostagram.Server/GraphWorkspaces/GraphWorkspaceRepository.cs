using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Ghostagram.Server.GraphWorkspaces;

public sealed record GraphWorkspacePersistedState(string WorkspaceId, string GraphJson, string PresentationJson);

public interface IGraphWorkspaceRepository
{
    Task<GraphWorkspacePersistedState?> LoadAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task SaveAsync(GraphWorkspacePersistedState state, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string workspaceId, CancellationToken cancellationToken = default);
}

/// <summary>Versioned, atomic file persistence for graph semantics plus their independent presentation sidecar.</summary>
public sealed class FileGraphWorkspaceRepository : IGraphWorkspaceRepository
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);

    public FileGraphWorkspaceRepository(IOptions<GraphWorkspaceOptions> options, IHostEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        directory = options.Value.StorageDirectory ?? Path.Combine(environment?.ContentRootPath ?? AppContext.BaseDirectory, "App_Data", "graph-workspaces");
        directory = Path.GetFullPath(directory);
    }

    public async Task<GraphWorkspacePersistedState?> LoadAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(workspaceId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return null;
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<WorkspaceDocumentDto>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException($"Graph workspace '{workspaceId}' did not contain a document.");
            if (document.SchemaVersion != CurrentSchemaVersion)
                throw new InvalidDataException($"Graph workspace '{workspaceId}' has unsupported schema version '{document.SchemaVersion}'.");
            if (!string.Equals(document.WorkspaceId, workspaceId, StringComparison.Ordinal))
                throw new InvalidDataException($"Graph workspace file identity '{document.WorkspaceId}' does not match '{workspaceId}'.");
            if (string.IsNullOrWhiteSpace(document.GraphJson) || string.IsNullOrWhiteSpace(document.PresentationJson))
                throw new InvalidDataException($"Graph workspace '{workspaceId}' is missing semantic or presentation state.");
            return new(document.WorkspaceId, document.GraphJson, document.PresentationJson);
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(GraphWorkspacePersistedState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = PathFor(state.WorkspaceId);
        if (string.IsNullOrWhiteSpace(state.GraphJson) || string.IsNullOrWhiteSpace(state.PresentationJson))
            throw new ArgumentException("Graph and presentation JSON are required.", nameof(state));
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{state.WorkspaceId}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = File.Create(temporary))
                    await JsonSerializer.SerializeAsync(stream,
                        new WorkspaceDocumentDto(CurrentSchemaVersion, state.WorkspaceId, state.GraphJson, state.PresentationJson),
                        JsonOptions,
                        cancellationToken);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally { gate.Release(); }
    }

    public async Task<bool> DeleteAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(workspaceId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        finally { gate.Release(); }
    }

    private string PathFor(string workspaceId)
    {
        GraphWorkspaceIds.Require(workspaceId);
        return Path.Combine(directory, $"{workspaceId}.json");
    }

    private sealed record WorkspaceDocumentDto(
        int SchemaVersion = 0,
        string WorkspaceId = "",
        string GraphJson = "",
        string PresentationJson = "");
}
