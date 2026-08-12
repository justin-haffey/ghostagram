using System.Collections.Immutable;
using System.Text.Json;
using Ghostagram.Core;

namespace Ghostagram.Server.Persistence;

/// <summary>
/// Creates a schema-v1 custom palette definition from an unregistered diagram node.
/// </summary>
public static class PaletteNodeDefinitionCapture
{
    private const string DefaultOutline = "#334155";
    private const string DefaultBackground = "#f8fafc";
    private const string DefaultText = "#0f172a";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Captures a node and the ports owned by it. Registered node types are application-owned
    /// definitions and deliberately cannot be copied into a custom palette.
    /// </summary>
    public static PaletteNodeDefinitionSnapshot Capture(
        DiagramNode node,
        IEnumerable<DiagramPort> ports,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(ports);
        if (!string.IsNullOrWhiteSpace(node.TypeId))
            throw new ArgumentException("Registered node types cannot be captured as custom palette definitions.", nameof(node));

        var nodeMetadata = CloneMetadata(node.ExtensionData).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (node.Sections is { Count: > 0 })
            nodeMetadata["ghostagram.sections"] = JsonSerializer.SerializeToElement(node.Sections, JsonOptions);
        if (node.Presentation is not null)
            nodeMetadata["ghostagram.presentation"] = JsonSerializer.SerializeToElement(node.Presentation, JsonOptions);
        AddExtensionMetadata(nodeMetadata, "ghostagram.styleExtensions", node.Style?.ExtensionData);

        var style = node.Style;
        var capturedPorts = ports.Where(port => string.Equals(port.NodeId, node.Id, StringComparison.Ordinal))
            .Select(CapturePort)
            .OrderBy(port => port.Order)
            .ThenBy(port => port.Id, StringComparer.Ordinal)
            .ToArray();
        var propertyDirections = capturedPorts.Where(port => !string.IsNullOrWhiteSpace(port.PropertyId))
            .GroupBy(port => port.PropertyId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => ResolvePropertyDirection(group.Select(port => port.Direction)), StringComparer.Ordinal);

        return new PaletteNodeDefinitionSnapshot(
            node.Id,
            string.IsNullOrWhiteSpace(node.Label) ? node.Id : node.Label,
            description ?? ReadDescription(node.ExtensionData) ?? string.Empty,
            node.Width,
            node.Height,
            IsGroup: false,
            node.Icon,
            new PaletteNodeStyleSnapshot(
                style?.BorderColor ?? DefaultOutline,
                style?.Background ?? DefaultBackground,
                style?.Color ?? DefaultText,
                style?.TextAlign ?? "left"),
            capturedPorts,
            node.Properties.Select((property, index) => CaptureProperty(
                property,
                propertyDirections.GetValueOrDefault(property.Id, "both"),
                index)).ToArray(),
            nodeMetadata.ToImmutableDictionary(StringComparer.Ordinal));
    }

    private static PalettePortDefinitionSnapshot CapturePort(DiagramPort port)
    {
        var anchor = NormalizeAnchor(port.Anchor, port.Direction);
        var metadata = CloneMetadata(port.ExtensionData).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        AddExtensionMetadata(metadata, "ghostagram.endpointExtensions", port.Endpoint?.ExtensionData);
        var endpoint = port.Endpoint is null
            ? null
            : new PaletteEndpointSnapshot(
                port.Endpoint.Type ?? "dot",
                port.Endpoint.Size ?? 10,
                port.Endpoint.Stroke ?? DefaultOutline,
                port.Endpoint.Fill ?? "#ffffff",
                port.Endpoint.StrokeWidth ?? 2);

        return new PalettePortDefinitionSnapshot(
            port.Id,
            anchor,
            port.Direction,
            anchor,
            port.Label,
            port.PropertyId,
            port.Order,
            endpoint,
            metadata.ToImmutableDictionary(StringComparer.Ordinal));
    }

    private static PalettePropertyDefinitionSnapshot CaptureProperty(DiagramNodeProperty property, string direction, int order) => new(
        property.Id,
        property.Name,
        property.Type,
        property.Value?.Clone(),
        property.Mode,
        property.Label,
        property.Connectable,
        property.Options?.ToArray() ?? [],
        Direction: direction,
        Order: order,
        Metadata: CapturePropertyMetadata(property));

    private static IReadOnlyDictionary<string, JsonElement> CapturePropertyMetadata(DiagramNodeProperty property)
    {
        var metadata = CloneMetadata(property.ExtensionData).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (property.Metadata is { } value)
            metadata["ghostagram.propertyMetadata"] = value.Clone();
        if (!string.IsNullOrWhiteSpace(property.SectionId))
            metadata["ghostagram.sectionId"] = JsonSerializer.SerializeToElement(property.SectionId, JsonOptions);
        if (property.Editor is not null)
            metadata["ghostagram.editor"] = JsonSerializer.SerializeToElement(property.Editor, JsonOptions);
        return metadata.ToImmutableDictionary(StringComparer.Ordinal);
    }

    private static string NormalizeAnchor(object? anchor, string direction)
    {
        if (anchor is string value && value is "left" or "right" or "top" or "bottom") return value;
        return direction.Equals("target", StringComparison.OrdinalIgnoreCase) ? "left" : "right";
    }

    private static string ResolvePropertyDirection(IEnumerable<string> directions)
    {
        var values = directions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return values.Contains("both") || values.Contains("source") && values.Contains("target")
            ? "both"
            : values.Contains("source") ? "source"
            : values.Contains("target") ? "target"
            : "both";
    }

    private static string? ReadDescription(IDictionary<string, JsonElement>? metadata) =>
        metadata is not null && metadata.TryGetValue("description", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyDictionary<string, JsonElement> CloneMetadata(IDictionary<string, JsonElement>? metadata) =>
        metadata is null
            ? ImmutableDictionary<string, JsonElement>.Empty
            : metadata.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);

    private static void AddExtensionMetadata(
        IDictionary<string, JsonElement> target,
        string key,
        IDictionary<string, JsonElement>? extensionData)
    {
        if (extensionData is { Count: > 0 })
            target[key] = JsonSerializer.SerializeToElement(CloneMetadata(extensionData), JsonOptions);
    }
}
