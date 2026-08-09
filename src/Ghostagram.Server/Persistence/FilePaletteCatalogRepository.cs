using System.Collections.Concurrent;
using System.Text.Json;

namespace Ghostagram.Server.Persistence;

/// <summary>Atomic local-first palette catalog storage beneath App_Data.</summary>
public sealed class FilePaletteCatalogRepository : IPaletteCatalogRepository, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CommitGates = new(StringComparer.OrdinalIgnoreCase);

    public FilePaletteCatalogRepository(IHostEnvironment environment)
        : this(Path.Combine(environment.ContentRootPath, "App_Data", "ghostagram-palettes"))
    {
    }

    /// <summary>Creates a repository at an explicit directory, primarily for isolated hosts and tests.</summary>
    public FilePaletteCatalogRepository(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        _directory = Path.GetFullPath(storageDirectory);
    }

    public async Task<IReadOnlyList<PaletteCatalogSummary>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_directory)) return [];

            var summaries = new List<PaletteCatalogSummary>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var snapshot = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
                    summaries.Add(new(
                        snapshot.CatalogId,
                        snapshot.Catalog.Name,
                        snapshot.Catalog.Description,
                        snapshot.Catalog.Revision,
                        snapshot.Catalog.UpdatedAtUtc,
                        snapshot.CustomGroups.Count,
                        snapshot.CustomNodes.Count));
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    // A damaged catalog must not hide healthy catalogs or be overwritten as a side effect of listing.
                    // Keep the file in place as recovery evidence; direct GetAsync still surfaces its error.
                }
            }

            return summaries
                .OrderByDescending(summary => summary.UpdatedAtUtc)
                .ThenBy(summary => summary.CatalogId, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PaletteCatalogSnapshot?> GetAsync(string catalogId, CancellationToken cancellationToken)
    {
        var validId = PaletteCatalogIdRules.Require(catalogId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = PathFor(validId);
            if (!File.Exists(path)) return null;
            return await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<PaletteCatalogCommitResult> CreateAsync(
        PaletteCatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        PaletteCatalogValidator.Validate(snapshot);
        if (snapshot.Catalog.Revision != 1)
            throw new ArgumentException("A new palette catalog must start at revision 1.", nameof(snapshot));

        var detached = PaletteCatalogJson.DeepClone(snapshot);
        return CommitAsync(detached, expectedRevision: null, cancellationToken);
    }

    public Task<PaletteCatalogCommitResult> TrySaveAsync(
        PaletteCatalogSnapshot snapshot,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        PaletteCatalogValidator.Validate(snapshot);
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (snapshot.Catalog.Revision != expectedRevision + 1)
            throw new ArgumentException("The next palette catalog revision must equal expectedRevision + 1.", nameof(snapshot));

        var detached = PaletteCatalogJson.DeepClone(snapshot);
        return CommitAsync(detached, expectedRevision, cancellationToken);
    }

    private async Task<PaletteCatalogCommitResult> CommitAsync(
        PaletteCatalogSnapshot detached,
        long? expectedRevision,
        CancellationToken cancellationToken)
    {
        var target = PathFor(detached.CatalogId);
        var commitGate = CommitGates.GetOrAdd(target, static _ => new SemaphoreSlim(1, 1));
        await commitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            if (File.Exists(target))
            {
                var current = await ReadAsync(target, cancellationToken).ConfigureAwait(false);
                if (expectedRevision is null)
                    return new(false, "CATALOG_EXISTS", "A palette catalog with that ID already exists.", current.Catalog.Revision, current);
                if (current.Catalog.Revision != expectedRevision.Value)
                    return new(false, "REVISION_CONFLICT", $"Expected palette revision {current.Catalog.Revision}, received {expectedRevision.Value}.", current.Catalog.Revision, current);
            }
            else if (expectedRevision is not null)
            {
                return new(false, "CATALOG_NOT_FOUND", "The palette catalog no longer exists.", 0);
            }

            var temporary = string.Concat(target, ".", Guid.NewGuid().ToString("N"), ".tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, detached, SerializerOptions, cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporary, target, overwrite: true);
                return new(true, expectedRevision is null ? "CATALOG_CREATED" : "COMMITTED",
                    expectedRevision is null ? "Palette catalog created." : "Palette catalog saved.",
                    detached.Catalog.Revision,
                    PaletteCatalogJson.DeepClone(detached));
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally
        {
            commitGate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<PaletteCatalogSnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<PaletteCatalogSnapshot>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("Palette catalog '" + Path.GetFileName(path) + "' is empty.");
            PaletteCatalogValidator.Validate(snapshot);
            return PaletteCatalogJson.DeepClone(snapshot);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("Palette catalog '" + Path.GetFileName(path) + "' is invalid.", exception);
        }
    }

    private string PathFor(string catalogId) => Path.Combine(_directory, catalogId + ".json");

    private static class PaletteCatalogJson
    {
        public static PaletteCatalogSnapshot DeepClone(PaletteCatalogSnapshot snapshot)
        {
            var element = JsonSerializer.SerializeToElement(snapshot, SerializerOptions);
            return element.Deserialize<PaletteCatalogSnapshot>(SerializerOptions)
                ?? throw new InvalidDataException("The palette catalog could not be cloned.");
        }
    }
}

internal static class PaletteCatalogValidator
{
    public static void Validate(PaletteCatalogSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != PaletteCatalogSchema.CurrentVersion)
            throw new ArgumentException("Unsupported palette catalog schema version " + snapshot.SchemaVersion + ".", nameof(snapshot));

        PaletteCatalogIdRules.Require(snapshot.CatalogId);
        ArgumentNullException.ThrowIfNull(snapshot.Catalog);
        Required(snapshot.Catalog.Name, "catalog name", 256);
        if (snapshot.Catalog.Revision < 0)
            throw new ArgumentException("Palette catalog revision cannot be negative.", nameof(snapshot));
        if (snapshot.Catalog.CreatedAtUtc == default || snapshot.Catalog.UpdatedAtUtc == default)
            throw new ArgumentException("Palette catalog timestamps are required.", nameof(snapshot));
        if (snapshot.Catalog.UpdatedAtUtc < snapshot.Catalog.CreatedAtUtc)
            throw new ArgumentException("Palette catalog update time cannot precede its creation time.", nameof(snapshot));
        ValidateMetadata(snapshot.Catalog.Attributes, "catalog attributes");

        ArgumentNullException.ThrowIfNull(snapshot.CustomGroups);
        ArgumentNullException.ThrowIfNull(snapshot.CustomNodes);
        ArgumentNullException.ThrowIfNull(snapshot.Placements);
        ArgumentNullException.ThrowIfNull(snapshot.ExpandedGroupIds);

        Unique(snapshot.CustomGroups.Select(group => EntityId(group.Id, "group ID")), "custom group IDs");
        foreach (var group in snapshot.CustomGroups)
        {
            Required(group.Label, "group label", 256);
            if (group.Order < 0)
                throw new ArgumentException("Palette group order cannot be negative.", nameof(snapshot));
            ValidateMetadata(group.Metadata, "metadata for group '" + group.Id + "'");
        }

        Unique(snapshot.CustomNodes.Select(node => EntityId(node.Id, "node definition ID")), "custom node definition IDs");
        foreach (var node in snapshot.CustomNodes) ValidateNode(node, snapshot);

        Unique(snapshot.Placements.Select(placement => EntityId(placement.ItemId, "placement item ID")), "placement item IDs");
        foreach (var placement in snapshot.Placements)
        {
            EntityId(placement.GroupId, "placement group ID");
            if (placement.Order < 0)
                throw new ArgumentException("Palette item order cannot be negative.", nameof(snapshot));
        }

        Unique(snapshot.ExpandedGroupIds.Select(id => EntityId(id, "expanded group ID")), "expanded group IDs");
    }

    private static void ValidateNode(PaletteNodeDefinitionSnapshot node, PaletteCatalogSnapshot snapshot)
    {
        Required(node.Label, "node label", 256);
        if (!double.IsFinite(node.Width) || node.Width <= 0 || !double.IsFinite(node.Height) || node.Height <= 0)
            throw new ArgumentException("Node '" + node.Id + "' must have finite positive dimensions.", nameof(snapshot));
        ArgumentNullException.ThrowIfNull(node.Style);
        Required(node.Style.BorderColor, "node border color", 128);
        Required(node.Style.Background, "node background", 128);
        Required(node.Style.Color, "node text color", 128);
        Required(node.Style.TextAlign, "node text alignment", 32);
        ArgumentNullException.ThrowIfNull(node.Ports);
        ArgumentNullException.ThrowIfNull(node.Properties);
        Unique(node.Ports.Select(port => EntityId(port.Id, "port ID for node '" + node.Id + "'")), "port IDs for node '" + node.Id + "'");
        Unique(node.Properties.Select(property => EntityId(property.Id, "property ID for node '" + node.Id + "'")), "property IDs for node '" + node.Id + "'");
        var propertyIds = node.Properties.Select(property => property.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var port in node.Ports)
        {
            Required(port.Side, "port side", 32);
            Required(port.Direction, "port direction", 32);
            if (port.Order < 0)
                throw new ArgumentException("Port order cannot be negative for node '" + node.Id + "'.", nameof(snapshot));
            if (port.PropertyId is not null && !propertyIds.Contains(port.PropertyId))
                throw new ArgumentException("Port '" + port.Id + "' refers to missing property '" + port.PropertyId + "'.", nameof(snapshot));
            ValidateMetadata(port.Metadata, "metadata for port '" + port.Id + "'");
        }

        foreach (var property in node.Properties)
        {
            Required(property.Name, "property name", 256);
            Required(property.Type, "property type", 128);
            Required(property.Mode, "property mode", 32);
            Required(property.Direction, "property direction", 32);
            if (property.Order < 0)
                throw new ArgumentException("Property order cannot be negative for node '" + node.Id + "'.", nameof(snapshot));
            ArgumentNullException.ThrowIfNull(property.Options);
            ValidateMetadata(property.Metadata, "metadata for property '" + property.Id + "'");
            if (property.DefaultValue is { ValueKind: JsonValueKind.Undefined })
                throw new ArgumentException("Property '" + property.Id + "' has an undefined default value.", nameof(snapshot));
        }

        ValidateMetadata(node.Metadata, "metadata for node '" + node.Id + "'");
    }

    private static void ValidateMetadata(IReadOnlyDictionary<string, JsonElement>? metadata, string subject)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        foreach (var (key, value) in metadata)
        {
            Required(key, "key in " + subject, 256);
            if (value.ValueKind == JsonValueKind.Undefined)
                throw new ArgumentException(subject + " contains an undefined JSON value.");
        }
    }

    private static string EntityId(string? value, string subject)
    {
        Required(value, subject, 256);
        if (value!.Any(char.IsControl))
            throw new ArgumentException(subject + " cannot contain control characters.");
        return value!;
    }

    private static void Required(string? value, string subject, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new ArgumentException(subject + " must be 1-" + maximumLength + " non-whitespace characters.");
    }

    private static void Unique(IEnumerable<string> values, string subject)
    {
        var materialized = values.ToArray();
        if (materialized.Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new ArgumentException("Palette catalog contains duplicate " + subject + ".");
    }
}
