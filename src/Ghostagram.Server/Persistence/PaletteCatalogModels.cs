using System.Text.Json;

namespace Ghostagram.Server.Persistence;

/// <summary>Defines the durable JSON contract understood by palette catalog repositories.</summary>
public static class PaletteCatalogSchema
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// A complete, versioned palette workspace. Built-in groups and node types remain application-owned;
/// placements and presentation state may refer to either built-in or custom identifiers.
/// </summary>
public sealed record PaletteCatalogSnapshot(
    int SchemaVersion,
    string CatalogId,
    PaletteCatalogMetadata Catalog,
    IReadOnlyList<PaletteGroupSnapshot> CustomGroups,
    IReadOnlyList<PaletteNodeDefinitionSnapshot> CustomNodes,
    IReadOnlyList<PaletteItemPlacementSnapshot> Placements,
    IReadOnlyList<string> ExpandedGroupIds,
    bool IsOpen,
    bool IsPinned);

/// <summary>Human-facing catalog identity plus optimistic revision and extension metadata.</summary>
public sealed record PaletteCatalogMetadata(
    string Name,
    string? Description,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyDictionary<string, JsonElement> Attributes);

/// <summary>A user-created palette folder.</summary>
public sealed record PaletteGroupSnapshot(
    string Id,
    string Label,
    string? Description,
    string? Icon,
    int Order,
    IReadOnlyDictionary<string, JsonElement> Metadata);

/// <summary>A complete custom node type definition, independent of any diagram instance.</summary>
public sealed record PaletteNodeDefinitionSnapshot(
    string Id,
    string Label,
    string Description,
    double Width,
    double Height,
    bool IsGroup,
    string? Icon,
    PaletteNodeStyleSnapshot Style,
    IReadOnlyList<PalettePortDefinitionSnapshot> Ports,
    IReadOnlyList<PalettePropertyDefinitionSnapshot> Properties,
    IReadOnlyDictionary<string, JsonElement> Metadata);

public sealed record PaletteNodeStyleSnapshot(
    string BorderColor,
    string Background,
    string Color,
    string TextAlign);

public sealed record PalettePortDefinitionSnapshot(
    string Id,
    string Side,
    string Direction,
    string? Anchor,
    string? Label,
    string? PropertyId,
    int Order,
    PaletteEndpointSnapshot? Endpoint,
    IReadOnlyDictionary<string, JsonElement> Metadata);

public sealed record PaletteEndpointSnapshot(
    string Type,
    double Size,
    string Stroke,
    string Fill,
    double StrokeWidth);

public sealed record PalettePropertyDefinitionSnapshot(
    string Id,
    string Name,
    string Type,
    JsonElement? DefaultValue,
    string Mode,
    string? Label,
    bool Connectable,
    IReadOnlyList<string> Options,
    string Direction,
    int Order,
    IReadOnlyDictionary<string, JsonElement> Metadata);

/// <summary>Places either a custom or built-in palette item in a custom or built-in group.</summary>
public sealed record PaletteItemPlacementSnapshot(string ItemId, string GroupId, int Order);

/// <summary>Lightweight catalog projection used by open/recent-palette interfaces.</summary>
public sealed record PaletteCatalogSummary(
    string CatalogId,
    string Name,
    string? Description,
    long Revision,
    DateTimeOffset UpdatedAtUtc,
    int CustomGroupCount,
    int CustomNodeCount);
