using System.Text.Json;
using System.Text.Json.Serialization;
using Ghostagram.Contracts;

namespace Ghostagram.Core;

public sealed record DiagramDocument(
    string DocumentId,
    IReadOnlyList<DiagramNode> Nodes,
    IReadOnlyList<DiagramPort> Ports,
    IReadOnlyList<DiagramEdge> Edges,
    IReadOnlyList<DiagramGroup>? Groups = null,
    DiagramViewport? Viewport = null,
    IReadOnlyList<DiagramEdgeType>? EdgeTypes = null,
    IReadOnlyList<string>? Selection = null)
{
    public IReadOnlyList<DiagramGroup> Groups { get; init; } = Groups ?? [];
    public IReadOnlyList<DiagramEdgeType> EdgeTypes { get; init; } = EdgeTypes ?? [];
    public IReadOnlyList<string> Selection { get; init; } = Selection ?? [];
    public DiagramViewport Viewport { get; init; } = Viewport ?? new();
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record DiagramNode(
    string Id,
    double X,
    double Y,
    double Width = 160,
    double Height = 80,
    string? Label = null,
    string? GroupId = null,
    double Rotation = 0,
    bool Resizable = true,
    bool Rotatable = true,
    bool LabelEditable = true,
    string? Icon = null,
    DiagramNodeStyle? Style = null,
    string? TypeId = null,
    int TypeVersion = 1,
    IReadOnlyList<DiagramNodeProperty>? Properties = null)
{
    public IReadOnlyList<DiagramNodeProperty> Properties { get; init; } = Properties ?? [];
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// A serializable property value attached to a node. <see cref="JsonElement"/> deliberately
/// preserves primitive, null, array, and custom object values without coupling the diagram
/// datatype to an application's runtime CLR type.
/// </summary>
public sealed record DiagramNodeProperty(
    string Id,
    string Name,
    string Type = DiagramPropertyTypes.String,
    JsonElement? Value = null,
    string Mode = DiagramPropertyModes.Display,
    string? Label = null,
    string? Description = null,
    bool Required = false,
    bool Connectable = false,
    IReadOnlyList<string>? Options = null,
    JsonElement? Metadata = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public static class DiagramPropertyTypes
{
    public const string String = "string";
    public const string Boolean = "boolean";
    public const string Integer = "integer";
    public const string Decimal = "decimal";
    public const string Date = "date";
    public const string DateTime = "dateTime";
    public const string Enum = "enum";
    public const string Json = "json";
}

public static class DiagramPropertyModes
{
    public const string Display = "display";
    public const string Edit = "edit";
    public const string DisplayAndEdit = "displayAndEdit";
    public const string Hidden = "hidden";
}

public sealed record DiagramPort(
    string Id,
    string NodeId,
    string Direction = "both",
    string Scope = "*",
    int MaxConnections = -1,
    bool Enabled = true,
    object? Anchor = null,
    DiagramEndpoint? Endpoint = null,
    DiagramConnectionPolicy? ConnectionPolicy = null,
    string? PropertyId = null,
    string? Label = null,
    int Order = 0)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record DiagramEdge(
    string Id,
    string SourcePortId,
    string TargetPortId,
    string? Label = null,
    string Connector = "flowchart",
    string? Type = null,
    IReadOnlyList<DiagramOverlay>? Overlays = null,
    IReadOnlyList<DiagramPoint>? Waypoints = null,
    DiagramFlowchartOptions? ConnectorOptions = null,
    DiagramEdgeStyle? Style = null,
    object? Animation = null,
    bool Detachable = true,
    bool Reconnectable = true,
    bool LabelEditable = true,
    double? LabelOffsetX = null,
    double? LabelOffsetY = null);

public sealed record DiagramGroup(
    string Id,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label = null,
    string? ParentGroupId = null,
    bool Collapsed = false,
    bool Resizable = true,
    bool LabelEditable = true,
    string? Icon = null);

public sealed record DiagramViewport(double X = 0, double Y = 0, double Zoom = 1);
public sealed record DiagramPoint(double X, double Y);
public sealed record DiagramNodeStyle(
    string? Background = null,
    string? BorderColor = null,
    string? Color = null,
    string? TextAlign = null);
public sealed record DiagramEdgeStyle(string? Stroke = null, double? StrokeWidth = null, string? Dash = null, double? Opacity = null, string? LabelColor = null);
public sealed record DiagramEndpoint(string Type = "dot", double? Size = null, string? Fill = null, string? Stroke = null, double? StrokeWidth = null);
public sealed record DiagramConnectionPolicy(IReadOnlyList<string>? AllowPortIds = null, IReadOnlyList<string>? DenyPortIds = null, IReadOnlyList<string>? AllowNodeIds = null, IReadOnlyList<string>? DenyNodeIds = null);
public sealed record DiagramFlowchartOptions(double Stub = 32, double CornerRadius = 0);
public sealed record DiagramOverlay(string Type, string? Label = null, double? Location = null, double? OffsetX = null, double? OffsetY = null, double? FontSize = null);
public sealed record DiagramEdgeType(string Id, string Connector = "flowchart", DiagramEdgeStyle? Style = null, IReadOnlyList<DiagramOverlay>? Overlays = null, object? Animation = null, bool? Detachable = null, bool? Reconnectable = null);

/// <summary>
/// Resolves the direct group for a node by the node center. This is the shared
/// C# counterpart of the browser drop rule, so palette-created nodes and
/// browser-dragged nodes use the same nested-group selection semantics.
/// </summary>
public static class DiagramGroupMembership
{
    public static string? ResolveGroupId(IEnumerable<DiagramGroup> groups, double x, double y, double width, double height)
    {
        var centerX = x + width / 2;
        var centerY = y + height / 2;
        return groups
            .Where(group => !group.Collapsed && centerX >= group.X && centerX <= group.X + group.Width && centerY >= group.Y && centerY <= group.Y + group.Height)
            .OrderBy(group => group.Width * group.Height)
            .ThenBy(group => group.Id, StringComparer.Ordinal)
            .Select(group => group.Id)
            .FirstOrDefault();
    }
}

/// <summary>Creates browser/server-compatible operations without hand-written JSON.</summary>
public static class DiagramOperations
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static GhostagramOperation Upsert(DiagramNode node) => Create("node.upsert", node, node.Id);
    public static GhostagramOperation Upsert(DiagramPort port) => Create("port.upsert", port, port.Id);
    public static GhostagramOperation Upsert(DiagramEdge edge) => Create("edge.upsert", edge, edge.Id);
    public static GhostagramOperation Upsert(DiagramGroup group) => Create("group.upsert", group, group.Id);
    public static GhostagramOperation Upsert(DiagramEdgeType edgeType) => Create("edgeType.upsert", edgeType, edgeType.Id);
    public static GhostagramOperation RemoveNode(string id) => Create("node.remove", new { id }, id);
    public static GhostagramOperation RemovePort(string id) => Create("port.remove", new { id }, id);
    public static GhostagramOperation RemoveEdge(string id) => Create("edge.remove", new { id }, id);
    public static GhostagramOperation RemoveGroup(string id) => Create("group.remove", new { id }, id);
    public static GhostagramOperation Select(IEnumerable<string> ids) => Create("selection.replace", new { ids = ids.ToArray() });
    public static GhostagramOperation SetViewport(DiagramViewport viewport) => Create("viewport.set", viewport);
    public static GhostagramOperation Fit(double padding = 32) => Create("viewport.fit", new { padding });
    public static GhostagramOperation Center(double x, double y, double? zoom = null) => Create("viewport.center", new { x, y, zoom });

    public static GhostagramOperation Create(string type, object value, string? id = null) =>
        new(type, JsonSerializer.SerializeToElement(value, SerializerOptions), id);
}

/// <summary>Fluent, immutable-at-build-time construction for C# diagram definitions.</summary>
public sealed class DiagramBuilder
{
    private readonly string _documentId;
    private readonly List<DiagramNode> _nodes = [];
    private readonly List<DiagramPort> _ports = [];
    private readonly List<DiagramEdge> _edges = [];
    private readonly List<DiagramGroup> _groups = [];
    private readonly List<DiagramEdgeType> _edgeTypes = [];
    private DiagramViewport _viewport = new();

    private DiagramBuilder(string documentId) => _documentId = RequireId(documentId, nameof(documentId));
    public static DiagramBuilder Create(string documentId) => new(documentId);
    public DiagramBuilder Node(string id, string? label, double x, double y, double width = 160, double height = 80, string? groupId = null) { _nodes.Add(new(RequireId(id, nameof(id)), x, y, width, height, label, groupId)); return this; }
    public DiagramBuilder Port(string id, string nodeId, object? anchor = null, string direction = "both", string scope = "*") { _ports.Add(new(RequireId(id, nameof(id)), RequireId(nodeId, nameof(nodeId)), direction, scope, Anchor: anchor)); return this; }
    public DiagramBuilder Edge(string id, string sourcePortId, string targetPortId, string? label = null, string connector = "flowchart") { _edges.Add(new(RequireId(id, nameof(id)), RequireId(sourcePortId, nameof(sourcePortId)), RequireId(targetPortId, nameof(targetPortId)), label, connector)); return this; }
    public DiagramBuilder Group(string id, string? label, double x, double y, double width, double height, string? parentGroupId = null) { _groups.Add(new(RequireId(id, nameof(id)), x, y, width, height, label, parentGroupId)); return this; }
    public DiagramBuilder EdgeType(DiagramEdgeType edgeType) { _edgeTypes.Add(edgeType); return this; }
    public DiagramBuilder Viewport(double x, double y, double zoom = 1) { _viewport = new(x, y, zoom); return this; }
    public DiagramDocument Build() => new(_documentId, _nodes.ToArray(), _ports.ToArray(), _edges.ToArray(), _groups.ToArray(), _viewport, _edgeTypes.ToArray());
    private static string RequireId(string value, string name) => !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("An identifier is required.", name);
}
