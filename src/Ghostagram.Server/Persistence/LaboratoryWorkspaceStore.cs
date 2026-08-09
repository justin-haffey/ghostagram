using System.Text.Json;

namespace Ghostagram.Server.Persistence;

public sealed record LaboratoryWorkspaceState(string DocumentId, string PaletteCatalogId);

/// <summary>Persists the local Laboratory's active durable aggregates without coupling either catalog to the UI.</summary>
public interface ILaboratoryWorkspaceStore
{
    Task<LaboratoryWorkspaceState?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(LaboratoryWorkspaceState state, CancellationToken cancellationToken);
}

public sealed class FileLaboratoryWorkspaceStore(IHostEnvironment environment) : ILaboratoryWorkspaceStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _path = Path.Combine(environment.ContentRootPath, "App_Data", "laboratory-workspace.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<LaboratoryWorkspaceState?> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return null;
            await using var stream = File.OpenRead(_path);
            var state = await JsonSerializer.DeserializeAsync<LaboratoryWorkspaceState>(stream, Options, cancellationToken);
            if (state is null) return null;
            DocumentIdRules.Require(state.DocumentId);
            PaletteCatalogIdRules.Require(state.PaletteCatalogId);
            return state;
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException)
        {
            return null;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(LaboratoryWorkspaceState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        DocumentIdRules.Require(state.DocumentId);
        PaletteCatalogIdRules.Require(state.PaletteCatalogId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = File.Create(temporary))
                    await JsonSerializer.SerializeAsync(stream, state, Options, cancellationToken);
                File.Move(temporary, _path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally { _gate.Release(); }
    }
}
