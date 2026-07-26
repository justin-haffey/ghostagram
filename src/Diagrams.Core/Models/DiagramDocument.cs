using System.Collections.Immutable;

namespace Diagrams.Core.Models;

public sealed record class DiagramMetadata(
    string Title,
    string Description,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public static DiagramMetadata Create(string title, string? description = null, TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        return new DiagramMetadata(title, description ?? string.Empty, now, now);
    }
}

public sealed record class DiagramCanvas(double Width, double Height, double GridSize)
{
    public static DiagramCanvas Default { get; } = new(6000, 4000, 24);
}

public sealed record class ViewportState(double Zoom, double ScrollLeft, double ScrollTop)
{
    public static ViewportState Default { get; } = new(1.0, 1400, 1000);
}

public sealed record class DiagramBounds(double X, double Y, double Width, double Height)
{
    public double CenterX => X + (Width / 2);
    public double CenterY => Y + (Height / 2);
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public sealed record class DiagramPoint(double X, double Y);

public sealed record class DiagramStyleToken(
    string Id,
    string Fill,
    string Border,
    string Text,
    string Edge,
    string Accent);

public sealed record class EdgeMarkers(MarkerKind Source, MarkerKind Target);

public sealed record class DiagramPort(
    string Id,
    string NodeId,
    PortSide Side,
    PortRole Role,
    EndpointKind EndpointKind,
    string Anchor,
    int MaxConnections,
    string Label);

public sealed record class DiagramNode(
    string Id,
    string StencilKey,
    DiagramBounds Bounds,
    string Label,
    ImmutableArray<string> PortIds,
    string? GroupId,
    ImmutableDictionary<string, string?> Properties,
    string StyleToken)
{
    public ImmutableHashSet<string> Tags { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);
    public string? LinkUri { get; init; }
    public string LayerId { get; init; } = DiagramLayerCatalog.DefaultLayerId;
    public int ZIndex { get; init; }
}

public sealed record class DiagramEdge(
    string Id,
    string SourcePortId,
    string TargetPortId,
    ConnectorKind ConnectorKind,
    string Label,
    EdgeMarkers Markers,
    EdgeAnimationKind Animation,
    string StyleToken)
{
    public ImmutableHashSet<string> Tags { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);
    public string? LinkUri { get; init; }
    public string LayerId { get; init; } = DiagramLayerCatalog.DefaultLayerId;
    public ImmutableDictionary<string, string?> Metadata { get; init; } = ImmutableDictionary<string, string?>.Empty;
    public ImmutableArray<DiagramPoint> Waypoints { get; init; } = [];
}

public sealed record class DiagramGroup(
    string Id,
    string Label,
    DiagramBounds Bounds,
    bool Collapsed,
    LayoutSpec LayoutSpec,
    ImmutableHashSet<string> ChildNodeIds)
{
    public ImmutableHashSet<string> Tags { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);
    public string? LinkUri { get; init; }
    public string LayerId { get; init; } = DiagramLayerCatalog.DefaultLayerId;
    public int ZIndex { get; init; }
    public ImmutableDictionary<string, string?> Metadata { get; init; } = ImmutableDictionary<string, string?>.Empty;
}

public sealed record class LayerDefinition(string Id, string Name, bool IsVisible, bool IsLocked, int Order);

public sealed record class AnnotationTarget(string Kind, string? Id, DiagramPoint? Position = null);

public sealed record class DiagramAnnotation(
    string Id,
    AnnotationTarget Target,
    string Text,
    DateTimeOffset CreatedUtc,
    string Author);

public sealed record class CommentEntry(
    string Id,
    string Author,
    string Message,
    DateTimeOffset CreatedUtc);

public sealed record class CommentThread(
    string Id,
    AnnotationTarget Target,
    string Title,
    ImmutableArray<CommentEntry> Comments,
    CommentStatus Status);

public sealed record class DocumentSummary(
    string DocumentId,
    string Title,
    TemplateKind TemplateKind,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset CreatedUtc,
    bool IsFavorite,
    long Revision,
    int NodeCount,
    int EdgeCount,
    int GroupCount);

public sealed record class DocumentSnapshotSummary(
    string SnapshotId,
    string DocumentId,
    string Name,
    DateTimeOffset CreatedUtc,
    long Revision);

public sealed record class LibraryItemDefinition(
    string Id,
    string Name,
    string Description,
    TemplateKind TemplateKind,
    DateTimeOffset CreatedUtc,
    DiagramDocument Fragment);

public sealed record class ValidationIssue(
    string Id,
    ValidationSeverity Severity,
    string Code,
    string Message,
    string TargetKind,
    string? TargetId);

public sealed record class SyncResult(
    bool IsSuccess,
    bool HasConflict,
    long ServerRevision,
    DiagramDocument? Document,
    string Message);

public sealed record class DiagramDocument
{
    public int SchemaVersion { get; init; } = 2;
    public string DocumentId { get; init; } = $"doc-{Guid.NewGuid():N}";
    public long Revision { get; init; }
    public DiagramMetadata Metadata { get; init; } = DiagramMetadata.Create("Untitled Diagram");
    public TemplateKind TemplateKind { get; init; } = TemplateKind.Flowchart;
    public DiagramCanvas Canvas { get; init; } = DiagramCanvas.Default;
    public ViewportState ViewportState { get; init; } = ViewportState.Default;
    public bool IsFavorite { get; init; }
    public ImmutableHashSet<string> Tags { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);
    public ImmutableArray<DiagramNode> Nodes { get; init; } = [];
    public ImmutableArray<DiagramPort> Ports { get; init; } = [];
    public ImmutableArray<DiagramEdge> Edges { get; init; } = [];
    public ImmutableArray<DiagramGroup> Groups { get; init; } = [];
    public ImmutableArray<LayerDefinition> Layers { get; init; } = DiagramLayerCatalog.Defaults;
    public ImmutableArray<DiagramAnnotation> Annotations { get; init; } = [];
    public ImmutableArray<CommentThread> CommentThreads { get; init; } = [];
    public ImmutableArray<DiagramStyleToken> Styles { get; init; } = DiagramStyleCatalog.Defaults;

    public DiagramDocument Touch(TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        return this with
        {
            Revision = Revision + 1,
            Metadata = Metadata with
            {
                UpdatedUtc = now
            }
        };
    }

    public DiagramNode? FindNode(string nodeId) => Nodes.FirstOrDefault(node => node.Id == nodeId);

    public DiagramPort? FindPort(string portId) => Ports.FirstOrDefault(port => port.Id == portId);

    public DiagramEdge? FindEdge(string edgeId) => Edges.FirstOrDefault(edge => edge.Id == edgeId);

    public DiagramGroup? FindGroup(string groupId) => Groups.FirstOrDefault(group => group.Id == groupId);

    public string? FindNodeIdByPort(string portId) => FindPort(portId)?.NodeId;

    public LayerDefinition ResolveLayer(string? layerId)
        => Layers.FirstOrDefault(layer => layer.Id == layerId) ?? DiagramLayerCatalog.Defaults[0];

    public DocumentSummary ToSummary()
        => new(
            DocumentId,
            Metadata.Title,
            TemplateKind,
            Metadata.UpdatedUtc,
            Metadata.CreatedUtc,
            IsFavorite,
            Revision,
            Nodes.Length,
            Edges.Length,
            Groups.Length);

    public DiagramBounds GetPortBounds(DiagramPort port)
    {
        var node = FindNode(port.NodeId)
            ?? throw new InvalidOperationException($"Node '{port.NodeId}' was not found for port '{port.Id}'.");

        return port.Side switch
        {
            PortSide.Top => new DiagramBounds(node.Bounds.CenterX - 6, node.Bounds.Y - 6, 12, 12),
            PortSide.Right => new DiagramBounds(node.Bounds.Right - 6, node.Bounds.CenterY - 6, 12, 12),
            PortSide.Bottom => new DiagramBounds(node.Bounds.CenterX - 6, node.Bounds.Bottom - 6, 12, 12),
            PortSide.Left => new DiagramBounds(node.Bounds.X - 6, node.Bounds.CenterY - 6, 12, 12),
            _ => new DiagramBounds(node.Bounds.CenterX - 6, node.Bounds.CenterY - 6, 12, 12)
        };
    }
}

public static class DiagramLayerCatalog
{
    public const string DefaultLayerId = "layer-default";

    public static ImmutableArray<LayerDefinition> Defaults { get; } =
    [
        new(DefaultLayerId, "Base Layer", true, false, 0)
    ];
}

public static class DiagramStyleCatalog
{
    public static ImmutableArray<DiagramStyleToken> Defaults { get; } =
    [
        new("default", "#f8f4ea", "#314052", "#101723", "#1f827b", "#2ca8a0"),
        new("accent", "#e7f5f4", "#2ca8a0", "#0f2c32", "#2ca8a0", "#2ca8a0"),
        new("copper", "#f8eee5", "#c07a4a", "#39261c", "#c07a4a", "#c07a4a"),
        new("signal", "#fef5e7", "#d59a24", "#392d0a", "#d59a24", "#d59a24"),
        new("ink", "#232b36", "#7d94ad", "#faf9f5", "#8eb5d6", "#8eb5d6")
    ];

    public static DiagramStyleToken Resolve(this DiagramDocument document, string? styleToken)
        => document.Styles.FirstOrDefault(style => style.Id == styleToken) ?? Defaults[0];
}
