using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Definitions;
using Ghostworx.System.Composition.Exchange;
using Ghostworx.System.Graph;

namespace Ghostagram.Bridge.DeclarativeCompositionProjection;

public enum CompositionProjectionStatus { Fresh, Stale, Unsupported, Skewed, Failed }

/// <summary>Consumer diagnostics never replace or rename producer diagnostics.</summary>
public sealed record CompositionProjectionDiagnostic(string Code, string Path, string Message);

public static class CompositionPresentationProfile
{
    public const string Identity = "ghostagram.composition.projection/1";
    public const int MaximumRawSidecarEntries = 2048;
    public const double MaximumCoordinateMagnitude = 1_000_000;
    public const int MaximumProjectionUtf8Bytes = 1_048_576;
}

/// <summary>
/// A copied comparison anchor, not a newly minted semantic identity. The caller supplies
/// the root revision from the producer public view; no private definitions are traversed.
/// </summary>
public sealed class CompositionProjectionSourceAnchor : IEquatable<CompositionProjectionSourceAnchor>
{
    private readonly string comparison;

    public CompositionProjectionSourceAnchor(SemanticAddress rootIdentity, long rootRevision,
        CompositionContentIdentity contentIdentity, CompatibilityTuple compatibility,
        IEnumerable<DefinitionProvenance> provenance, MigrationLineage? lineage = null,
        IEnumerable<CompositionReferenceProvenance>? referenceProvenance = null)
    {
        if (rootIdentity.IsEmpty || rootRevision < 0 ||
            rootIdentity.Revision is { } pinned && pinned != rootRevision)
            throw new ArgumentException("Root identity and revision must agree.");
        ArgumentNullException.ThrowIfNull(contentIdentity);
        ArgumentNullException.ThrowIfNull(compatibility);
        ArgumentNullException.ThrowIfNull(provenance);
        if (compatibility.RecordKind != CompositionExchangeRecordKind.Ir ||
            compatibility.ContractVersion != contentIdentity.ContractVersion.Value ||
            compatibility.CompilerVersion != contentIdentity.CompilerVersion.Value ||
            compatibility.Profile != contentIdentity.Profile.Value ||
            compatibility.IdentityProfile != contentIdentity.Algorithm)
            throw new ArgumentException("The IR identity and source compatibility tuple must agree.");

        RootIdentity = rootIdentity;
        RootRevision = rootRevision;
        ContentDigest = contentIdentity.DigestHex;
        CanonicalizationProfile = contentIdentity.CanonicalizationProfile;
        SemanticDomain = contentIdentity.SemanticDomain;
        Compatibility = compatibility with { };
        // Only admitted producer facts belong here. Snapshot nested maps before they can
        // influence a later equality check; never retain arbitrary mutable source storage.
        var facts = provenance.Select(item => new
        {
            item.SourceIdentity,
            item.SourceRevision,
            Predecessors = item.Predecessors.Order(StringComparer.Ordinal).ToArray(),
            Metadata = item.Metadata.OrderBy(pair => pair.Key.CanonicalText, StringComparer.Ordinal)
                .Select(pair => new { Key = pair.Key.CanonicalText, pair.Value }).ToArray()
        }).OrderBy(item => item.SourceIdentity, StringComparer.Ordinal)
          .ThenBy(item => item.SourceRevision, StringComparer.Ordinal).ToArray();
        var options = CompositionPresentationJson.CreateOptions();
        Provenance = JsonSerializer.SerializeToElement(facts, options);
        // Reference traversal order belongs to the producer. Copy it without sorting,
        // provider calls, address resolution, or definition traversal.
        ReferenceProvenance = JsonSerializer.SerializeToElement((referenceProvenance ?? []).ToArray(), options);
        LineageCatalog = lineage?.CatalogIdentity;
        Lineage = lineage is null ? null : JsonSerializer.SerializeToElement(lineage.Entries.ToArray(), options);
        comparison = JsonSerializer.Serialize(new
        {
            Root = rootIdentity.CanonicalText,
            RootRevision,
            ContentDigest,
            CanonicalizationProfile,
            SemanticDomain,
            Compatibility,
            Provenance,
            ReferenceProvenance,
            LineageCatalog,
            // Migration order is significant; do not sort the lineage chain.
            Lineage
        }, options);
    }

    [JsonIgnore]
    public SemanticAddress RootIdentity { get; }
    [JsonPropertyName("rootIdentity")]
    public string RootIdentityText => RootIdentity.CanonicalText;
    public long RootRevision { get; }
    public string ContentDigest { get; }
    public string CanonicalizationProfile { get; }
    public string SemanticDomain { get; }
    public CompatibilityTuple Compatibility { get; }
    public JsonElement Provenance { get; }
    public JsonElement ReferenceProvenance { get; }
    public string? LineageCatalog { get; }
    public JsonElement? Lineage { get; }
    public bool Equals(CompositionProjectionSourceAnchor? other) =>
        other is not null && StringComparer.Ordinal.Equals(comparison, other.comparison);
    public override bool Equals(object? value) => value is CompositionProjectionSourceAnchor other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(comparison);
}

public enum CompositionRefreshDisposition { SameSource, FullReprojectionRequired, InvalidPresentationRevision }

/// <summary>SameSource is permission to compute a presentation diff, never a Fresh result.</summary>
public static class CompositionProjectionFreshness
{
    public static CompositionRefreshDisposition Compare(CompositionProjectionSourceAnchor previous,
        long previousPresentationRevision, CompositionProjectionSourceAnchor source, long presentationRevision)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(source);
        if (!previous.Equals(source)) return CompositionRefreshDisposition.FullReprojectionRequired;
        if (previousPresentationRevision < 0 || presentationRevision < 0 ||
            presentationRevision < previousPresentationRevision)
            return CompositionRefreshDisposition.InvalidPresentationRevision;
        return CompositionRefreshDisposition.SameSource;
    }
}

public static class CompositionDiagramIdentity
{
    // Presentation keys encode a bounded owner path and occurrence. They are not one
    // SemanticAddress, so their byte bound is derived from the admitted tuple dimensions.
    internal static int MaximumSourceKeyUtf8Bytes(CompositionLimitProfile profile) =>
        checked(10 + 4 * ((checked((profile.MaxRecursionDepth + 2) * (profile.MaxIdentifierUtf8Bytes + 4)) + 2) / 3));

    /// <summary>Length-prefix UTF-8 tuple members to avoid delimiter and prefix collisions.</summary>
    public static string Encode(string kind, params string[] canonicalIdentityTuple)
    {
        if (string.IsNullOrEmpty(kind) || kind.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("A simple diagram kind prefix is required.", nameof(kind));
        ArgumentNullException.ThrowIfNull(canonicalIdentityTuple);
        if (canonicalIdentityTuple.Length == 0) throw new ArgumentException("An identity tuple is required.");
        var fields = canonicalIdentityTuple.Select(value => Encoding.UTF8.GetBytes(
            string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Identity members must be nonempty.") : value)).ToArray();
        var length = fields.Aggregate(0, (sum, field) => checked(sum + 4 + field.Length));
        var bytes = new byte[length];
        var offset = 0;
        foreach (var field in fields)
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, 4), field.Length);
            offset += 4;
            field.CopyTo(bytes, offset);
            offset += field.Length;
        }
        return kind + ":" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
