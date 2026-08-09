namespace Ghostagram.Server.Persistence;

/// <summary>
/// Storage-neutral palette catalog boundary. A SQLite implementation can replace the file-backed
/// repository without changing callers or the serialized workspace contract.
/// </summary>
public interface IPaletteCatalogRepository
{
    Task<IReadOnlyList<PaletteCatalogSummary>> ListAsync(CancellationToken cancellationToken);
    Task<PaletteCatalogSnapshot?> GetAsync(string catalogId, CancellationToken cancellationToken);
    Task<PaletteCatalogCommitResult> CreateAsync(PaletteCatalogSnapshot snapshot, CancellationToken cancellationToken);
    Task<PaletteCatalogCommitResult> TrySaveAsync(
        PaletteCatalogSnapshot snapshot,
        long expectedRevision,
        CancellationToken cancellationToken);
}

/// <summary>Result of a create-only or expected-revision palette catalog commit.</summary>
public sealed record PaletteCatalogCommitResult(
    bool Accepted,
    string Code,
    string Message,
    long Revision,
    PaletteCatalogSnapshot? Snapshot = null);

public static class PaletteCatalogIdRules
{
    public static string Require(string catalogId)
    {
        if (string.IsNullOrWhiteSpace(catalogId)
            || catalogId.Length > 128
            || catalogId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException(
                "Palette catalog IDs must be 1-128 ASCII letters, numbers, hyphens, or underscores.",
                nameof(catalogId));
        }

        return catalogId;
    }
}
