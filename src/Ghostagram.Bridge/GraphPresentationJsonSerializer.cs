using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ghostagram.Core;
using Ghostworx.System.Graph;

namespace Ghostagram.Bridge;

public sealed class GraphPresentationSerializationException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public sealed class GraphPresentationSerializationOptions
{
    public bool WriteIndented { get; init; }
    public int MaximumNodes { get; init; } = 100_000;
    public int MaximumGroups { get; init; } = 100_000;
    public int MaximumEdges { get; init; } = 200_000;
    public int MaximumWaypointsPerEdge { get; init; } = 10_000;
    public int MaximumSelection { get; init; } = 100_000;
}

/// <summary>Strict, deterministic serializer for the independently durable presentation sidecar.</summary>
public sealed class GraphPresentationJsonSerializer
{
    public const int CurrentSchemaVersion = 1;
    private readonly GraphPresentationSerializationOptions policy;
    private readonly JsonSerializerOptions json;

    public GraphPresentationJsonSerializer(GraphPresentationSerializationOptions? options = null)
    {
        policy = options ?? new GraphPresentationSerializationOptions();
        if (policy.MaximumNodes < 1 || policy.MaximumGroups < 1 || policy.MaximumEdges < 1 ||
            policy.MaximumWaypointsPerEdge < 1 || policy.MaximumSelection < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Presentation serialization limits must be positive.");
        json = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = policy.WriteIndented,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 32
        };
    }

    public string Serialize(GraphPresentationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        var document = new PresentationDocumentDto
        {
            SchemaVersion = CurrentSchemaVersion,
            Revision = snapshot.Revision,
            Nodes = snapshot.Nodes.OrderBy(pair => pair.Key.Value).Select(pair => new NodeDto(pair.Key.ToString(), BoundsDto.From(pair.Value.Bounds))).ToArray(),
            Groups = snapshot.Groups.OrderBy(pair => pair.Key.Value).Select(pair => new GroupDto(pair.Key.ToString(), BoundsDto.From(pair.Value.Bounds), pair.Value.Collapsed)).ToArray(),
            Edges = snapshot.EdgeWaypoints.OrderBy(pair => pair.Key.Value).Select(pair => new EdgeDto(pair.Key.ToString(), pair.Value.Select(PointDto.From).ToArray())).ToArray(),
            Viewport = new(snapshot.Viewport.X, snapshot.Viewport.Y, snapshot.Viewport.Zoom),
            Selection = snapshot.Selection.Order(StringComparer.Ordinal).ToArray()
        };
        return JsonSerializer.Serialize(document, json);
    }

    public GraphPresentationSnapshot Deserialize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new GraphPresentationSerializationException("Presentation JSON is required.");
        PresentationDocumentDto document;
        try
        {
            document = JsonSerializer.Deserialize<PresentationDocumentDto>(value, json)
                ?? throw new GraphPresentationSerializationException("Presentation JSON did not contain a document.");
        }
        catch (JsonException exception)
        {
            throw new GraphPresentationSerializationException("Presentation JSON is invalid.", exception);
        }
        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new GraphPresentationSerializationException($"Unsupported presentation schema version '{document.SchemaVersion}'.");
        if (document.Revision < 0) throw new GraphPresentationSerializationException("Presentation revision cannot be negative.");
        if (document.Nodes is null || document.Groups is null || document.Edges is null || document.Viewport is null || document.Selection is null)
            throw new GraphPresentationSerializationException("Presentation document collections and viewport are required.");
        if (document.Nodes.Any(item => item is null || item.Bounds is null) ||
            document.Groups.Any(item => item is null || item.Bounds is null) ||
            document.Edges.Any(item => item is null || item.Waypoints is null))
            throw new GraphPresentationSerializationException("Presentation document entries and nested values are required.");
        if (document.Nodes.Length > policy.MaximumNodes || document.Groups.Length > policy.MaximumGroups ||
            document.Edges.Length > policy.MaximumEdges || document.Selection.Length > policy.MaximumSelection)
            throw new GraphPresentationSerializationException("Presentation document exceeds configured collection limits.");

        var nodes = Unique(document.Nodes, item => ParseNode(item.NodeId), "node")
            .ToDictionary(item => ParseNode(item.NodeId), item => new NodePresentationState(item.Bounds.ToBounds("node")));
        var groups = Unique(document.Groups, item => ParseNode(item.NodeId), "group")
            .ToDictionary(item => ParseNode(item.NodeId), item => new GroupPresentationState(item.Bounds.ToBounds("group"), item.Collapsed));
        var edges = Unique(document.Edges, item => ParseEdge(item.EdgeId), "edge").ToDictionary(
            item => ParseEdge(item.EdgeId),
            item => (IReadOnlyList<DiagramPoint>)ReadPoints(item).ToArray());
        var selection = document.Selection.Select(RequireSelection).ToArray();
        if (selection.Distinct(StringComparer.Ordinal).Count() != selection.Length)
            throw new GraphPresentationSerializationException("Presentation selection contains duplicate ids.");
        var viewport = new DiagramViewport(document.Viewport.X, document.Viewport.Y, document.Viewport.Zoom);
        ValidateViewport(viewport);
        return new(document.Revision,
            new ReadOnlyDictionary<NodeId, NodePresentationState>(nodes),
            new ReadOnlyDictionary<NodeId, GroupPresentationState>(groups),
            new ReadOnlyDictionary<EdgeId, IReadOnlyList<DiagramPoint>>(edges),
            viewport,
            Array.AsReadOnly(selection));
    }

    private IEnumerable<DiagramPoint> ReadPoints(EdgeDto edge)
    {
        if (edge.Waypoints.Length > policy.MaximumWaypointsPerEdge)
            throw new GraphPresentationSerializationException($"Edge '{edge.EdgeId}' exceeds the waypoint limit.");
        foreach (var point in edge.Waypoints)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new GraphPresentationSerializationException($"Edge '{edge.EdgeId}' has a non-finite waypoint.");
            yield return new(point.X, point.Y);
        }
    }

    private void ValidateSnapshot(GraphPresentationSnapshot snapshot)
    {
        if (snapshot.Revision < 0) throw new GraphPresentationSerializationException("Presentation revision cannot be negative.");
        if (snapshot.Nodes.Count > policy.MaximumNodes || snapshot.Groups.Count > policy.MaximumGroups ||
            snapshot.EdgeWaypoints.Count > policy.MaximumEdges || snapshot.Selection.Count > policy.MaximumSelection)
            throw new GraphPresentationSerializationException("Presentation snapshot exceeds configured collection limits.");
        foreach (var value in snapshot.Nodes.Values) ValidateBounds(value.Bounds, "node");
        foreach (var value in snapshot.Groups.Values) ValidateBounds(value.Bounds, "group");
        foreach (var pair in snapshot.EdgeWaypoints)
        {
            if (pair.Value.Count > policy.MaximumWaypointsPerEdge)
                throw new GraphPresentationSerializationException($"Edge '{pair.Key}' exceeds the waypoint limit.");
            if (pair.Value.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new GraphPresentationSerializationException($"Edge '{pair.Key}' has a non-finite waypoint.");
        }
        ValidateViewport(snapshot.Viewport);
        if (snapshot.Selection.Any(string.IsNullOrWhiteSpace) || snapshot.Selection.Distinct(StringComparer.Ordinal).Count() != snapshot.Selection.Count)
            throw new GraphPresentationSerializationException("Presentation selection must contain unique, non-empty ids.");
    }

    private static void ValidateBounds(DiagramBounds value, string kind)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Width) || !double.IsFinite(value.Height) ||
            !double.IsFinite(value.Rotation) || value.Width <= 0 || value.Height <= 0)
            throw new GraphPresentationSerializationException($"Presentation {kind} bounds must be finite with positive dimensions.");
    }

    private static void ValidateViewport(DiagramViewport value)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Zoom) || value.Zoom <= 0)
            throw new GraphPresentationSerializationException("Presentation viewport must be finite with positive zoom.");
    }

    private static NodeId ParseNode(string value) => GraphDiagramIds.TryNode(value, out var id)
        ? id : throw new GraphPresentationSerializationException($"'{value}' is not a stable node id.");
    private static EdgeId ParseEdge(string value) => GraphDiagramIds.TryEdge(value, out var id)
        ? id : throw new GraphPresentationSerializationException($"'{value}' is not a stable edge id.");
    private static string RequireSelection(string value) => !string.IsNullOrWhiteSpace(value)
        ? value : throw new GraphPresentationSerializationException("Presentation selection contains an empty id.");

    private static IEnumerable<T> Unique<T, TKey>(IEnumerable<T> source, Func<T, TKey> key, string kind) where TKey : notnull
    {
        var values = source.ToArray();
        if (values.GroupBy(key).Any(group => group.Count() > 1))
            throw new GraphPresentationSerializationException($"Presentation document contains duplicate {kind} ids.");
        return values;
    }

    private sealed class PresentationDocumentDto
    {
        public int SchemaVersion { get; set; }
        public long Revision { get; set; }
        public NodeDto[] Nodes { get; set; } = [];
        public GroupDto[] Groups { get; set; } = [];
        public EdgeDto[] Edges { get; set; } = [];
        public ViewportDto Viewport { get; set; } = new(0, 0, 1);
        public string[] Selection { get; set; } = [];
    }

    private sealed record NodeDto(string NodeId, BoundsDto Bounds);
    private sealed record GroupDto(string NodeId, BoundsDto Bounds, bool Collapsed);
    private sealed record EdgeDto(string EdgeId, PointDto[] Waypoints);
    private sealed record ViewportDto(double X, double Y, double Zoom);
    private sealed record PointDto(double X, double Y)
    {
        public static PointDto From(DiagramPoint point) => new(point.X, point.Y);
    }
    private sealed record BoundsDto(double X, double Y, double Width, double Height, double Rotation)
    {
        public static BoundsDto From(DiagramBounds value) => new(value.X, value.Y, value.Width, value.Height, value.Rotation);
        public DiagramBounds ToBounds(string kind)
        {
            var value = new DiagramBounds(X, Y, Width, Height, Rotation);
            ValidateBounds(value, kind);
            return value;
        }
    }
}
