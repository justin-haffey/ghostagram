using Ghostworx.System.Graph.Serialization;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Text.Json;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Ghostagram.Execution;
using Ghostworx.System.Graph;

namespace Ghostagram.Bridge;

public static class GraphDiagramIds
{
    public static string Node(NodeId id) => id.ToString();
    public static string Edge(EdgeId id) => id.ToString();
    public static string InputPort(NodeId id) => $"{id}:in";
    public static string OutputPort(NodeId id) => $"{id}:out";
    public static string Port(NodeId id, string profilePortId)
    {
        if (string.IsNullOrWhiteSpace(profilePortId))
            throw new ArgumentException("A profile port id must be non-empty.", nameof(profilePortId));
        return $"{id}:{profilePortId}";
    }
    public static string Group(NodeId id) => $"group:{id}";

    public static bool TryNode(string? value, out NodeId id)
    {
        id = NodeId.Empty;
        return value is not null && Guid.TryParseExact(value, "N", out var parsed) && (id = new NodeId(parsed)) != NodeId.Empty;
    }

    public static bool TryEdge(string? value, out EdgeId id)
    {
        id = EdgeId.Empty;
        return value is not null && Guid.TryParseExact(value, "N", out var parsed) && (id = new EdgeId(parsed)) != EdgeId.Empty;
    }

    public static bool TryGroup(string? value, out NodeId id)
    {
        id = NodeId.Empty;
        return value is not null && value.StartsWith("group:", StringComparison.Ordinal) && TryNode(value[6..], out id);
    }

    public static bool TryPortNode(string? value, out NodeId id)
    {
        id = NodeId.Empty;
        if (value is null) return false;
        return value.Length > 33 && value[32] == ':' && TryNode(value[..32], out id);
    }
}

public sealed record DiagramBounds(double X, double Y, double Width, double Height, double Rotation = 0);
public sealed record NodePresentationState(DiagramBounds Bounds);
public sealed record GroupPresentationState(DiagramBounds Bounds, bool Collapsed = false);

public sealed record GraphPresentationSnapshot(
    long Revision,
    IReadOnlyDictionary<NodeId, NodePresentationState> Nodes,
    IReadOnlyDictionary<NodeId, GroupPresentationState> Groups,
    IReadOnlyDictionary<EdgeId, IReadOnlyList<DiagramPoint>> EdgeWaypoints,
    DiagramViewport Viewport,
    IReadOnlyList<string> Selection);

public sealed record PresentationStoreCommit<T>(bool Accepted, long Revision, T? Value = default);

public enum OrphanHandling { Retain, Remove }
public sealed record OrphanReconciliationResult(bool Accepted, long Revision, int RemovedNodes, int RemovedGroups, int RemovedEdges, int RemovedSelections);

public interface IGraphPresentationStore
{
    long Revision { get; }
    GraphPresentationSnapshot Capture();
    PresentationStoreCommit<T> Execute<T>(long expectedRevision, Func<GraphPresentationEditor, T> action);
    OrphanReconciliationResult Reconcile(GraphLocalSnapshot snapshot, OrphanHandling handling, long expectedRevision);
}

public sealed class GraphPresentationEditor
{
    internal GraphPresentationEditor(GraphPresentationSnapshot snapshot)
    {
        Nodes = snapshot.Nodes.ToDictionary();
        Groups = snapshot.Groups.ToDictionary();
        EdgeWaypoints = snapshot.EdgeWaypoints.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<DiagramPoint>)pair.Value.Select(point => point with { }).ToArray());
        Viewport = snapshot.Viewport with { };
        Selection = new HashSet<string>(snapshot.Selection, StringComparer.Ordinal);
    }

    internal Dictionary<NodeId, NodePresentationState> Nodes { get; }
    internal Dictionary<NodeId, GroupPresentationState> Groups { get; }
    internal Dictionary<EdgeId, IReadOnlyList<DiagramPoint>> EdgeWaypoints { get; }
    internal DiagramViewport Viewport { get; private set; }
    internal HashSet<string> Selection { get; }

    public void SetNode(NodeId id, NodePresentationState value) => Nodes[id] = value;
    public void RemoveNode(NodeId id) => Nodes.Remove(id);
    public void SetGroup(NodeId id, GroupPresentationState value) => Groups[id] = value;
    public void RemoveGroup(NodeId id) => Groups.Remove(id);
    public void SetWaypoints(EdgeId id, IEnumerable<DiagramPoint> points) => EdgeWaypoints[id] = points.Select(point => point with { }).ToArray();
    public void RemoveWaypoints(EdgeId id) => EdgeWaypoints.Remove(id);
    public void SetViewport(DiagramViewport viewport) => Viewport = viewport with { };
    public void SetSelection(IEnumerable<string> ids) { Selection.Clear(); Selection.UnionWith(ids); }
}

public sealed class GraphPresentationStore : IGraphPresentationStore
{
    private readonly object _gate = new();
    private GraphPresentationSnapshot _snapshot;

    public GraphPresentationStore(GraphPresentationSnapshot? initial = null) =>
        _snapshot = initial is null ? Empty() : Clone(initial);

    public long Revision { get { lock (_gate) return _snapshot.Revision; } }
    public GraphPresentationSnapshot Capture() { lock (_gate) return Clone(_snapshot); }

    public PresentationStoreCommit<T> Execute<T>(long expectedRevision, Func<GraphPresentationEditor, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (expectedRevision != _snapshot.Revision) return new(false, _snapshot.Revision);
            var editor = new GraphPresentationEditor(_snapshot);
            var value = action(editor);
            _snapshot = Freeze(_snapshot.Revision + 1, editor);
            return new(true, _snapshot.Revision, value);
        }
    }

    public OrphanReconciliationResult Reconcile(GraphLocalSnapshot snapshot, OrphanHandling handling, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (expectedRevision != _snapshot.Revision)
                return new(false, _snapshot.Revision, 0, 0, 0, 0);
            if (handling == OrphanHandling.Retain)
                return new(true, _snapshot.Revision, 0, 0, 0, 0);
            var nodeIds = snapshot.Nodes.Select(node => node.Id).ToHashSet();
            var edgeIds = snapshot.Relationships.Select(item => item.Relationship.Id).ToHashSet();
            var validSelections = nodeIds.Select(GraphDiagramIds.Node)
                .Concat(edgeIds.Select(GraphDiagramIds.Edge))
                .Concat(nodeIds.Select(GraphDiagramIds.Group)).ToHashSet(StringComparer.Ordinal);
            var editor = new GraphPresentationEditor(_snapshot);
            var removedNodes = RemoveMissing(editor.Nodes, nodeIds);
            var removedGroups = RemoveMissing(editor.Groups, nodeIds);
            var removedEdges = RemoveMissing(editor.EdgeWaypoints, edgeIds);
            var before = editor.Selection.Count;
            editor.Selection.IntersectWith(validSelections);
            var removedSelections = before - editor.Selection.Count;
            if (removedNodes + removedGroups + removedEdges + removedSelections == 0)
                return new(true, _snapshot.Revision, 0, 0, 0, 0);
            _snapshot = Freeze(_snapshot.Revision + 1, editor);
            return new(true, _snapshot.Revision, removedNodes, removedGroups, removedEdges, removedSelections);
        }
    }

    private static int RemoveMissing<TKey, TValue>(Dictionary<TKey, TValue> values, HashSet<TKey> retained) where TKey : notnull
    {
        var removed = values.Keys.Where(key => !retained.Contains(key)).ToArray();
        foreach (var key in removed) values.Remove(key);
        return removed.Length;
    }

    private static GraphPresentationSnapshot Empty() => new(0,
        new ReadOnlyDictionary<NodeId, NodePresentationState>(new Dictionary<NodeId, NodePresentationState>()),
        new ReadOnlyDictionary<NodeId, GroupPresentationState>(new Dictionary<NodeId, GroupPresentationState>()),
        new ReadOnlyDictionary<EdgeId, IReadOnlyList<DiagramPoint>>(new Dictionary<EdgeId, IReadOnlyList<DiagramPoint>>()),
        new(), []);

    private static GraphPresentationSnapshot Freeze(long revision, GraphPresentationEditor editor) => new(
        revision,
        new ReadOnlyDictionary<NodeId, NodePresentationState>(editor.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value with { Bounds = pair.Value.Bounds with { } })),
        new ReadOnlyDictionary<NodeId, GroupPresentationState>(editor.Groups.ToDictionary(pair => pair.Key, pair => pair.Value with { Bounds = pair.Value.Bounds with { } })),
        new ReadOnlyDictionary<EdgeId, IReadOnlyList<DiagramPoint>>(editor.EdgeWaypoints.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<DiagramPoint>)pair.Value.Select(point => point with { }).ToArray())),
        editor.Viewport with { }, editor.Selection.Order(StringComparer.Ordinal).ToArray());

    private static GraphPresentationSnapshot Clone(GraphPresentationSnapshot snapshot) => Freeze(snapshot.Revision, new GraphPresentationEditor(snapshot));
}

public sealed record NodeDiagramProjection(DiagramNode Node, IReadOnlyList<DiagramPort> Ports);
public sealed record HierarchyDiagramProjection(IReadOnlyList<DiagramGroup> Groups, IReadOnlyDictionary<NodeId, string> NodeGroups);

public interface INodeKindDescriptorRegistry
{
    bool TryGet(NodeKind kind, out NodeTypeDescriptor descriptor);
    bool TryGetKind(string typeId, int version, out NodeKind kind);
}

/// <summary>Overrides the ports projected for one semantic node kind.</summary>
public interface INodePortPresentationProfile
{
    NodeKind Kind { get; }
    IReadOnlyList<DiagramPort> Map(GraphLocalNodeSnapshot node, DiagramNode projectedNode);
}

public interface INodePortPresentationProfileRegistry
{
    bool TryGet(NodeKind kind, out INodePortPresentationProfile profile);
}

/// <summary>Overrides the edge presentation projected for one semantic relationship kind.</summary>
public interface IRelationshipPresentationProfile
{
    RelationshipKind Kind { get; }
    DiagramEdge Map(
        GraphLocalRelationshipSnapshot relationship,
        GraphPresentationSnapshot presentation,
        IReadOnlyList<DiagramPort> sourcePorts,
        IReadOnlyList<DiagramPort> targetPorts);
}

public interface IRelationshipPresentationProfileRegistry
{
    bool TryGet(RelationshipKind kind, out IRelationshipPresentationProfile profile);
}

public interface INodePresentationMapper { NodeDiagramProjection Map(GraphLocalNodeSnapshot node, GraphPresentationSnapshot presentation, int ordinal); }
public interface IRelationshipPresentationMapper
{
    DiagramEdge Map(
        GraphLocalRelationshipSnapshot relationship,
        GraphPresentationSnapshot presentation,
        IReadOnlyList<DiagramPort> sourcePorts,
        IReadOnlyList<DiagramPort> targetPorts);
}
public interface IHierarchyPresentationMapper { HierarchyDiagramProjection Map(GraphLocalSnapshot snapshot, IReadOnlyDictionary<NodeId, DiagramNode> nodes, GraphPresentationSnapshot presentation); }
public interface IGraphDiagramProjection { DiagramDocument Project(GraphLocalSnapshot snapshot, GraphPresentationSnapshot presentation); }

public sealed record GraphProjectionDiagnostic(string Code, string NodeId, string MetadataKey, string Message);

public sealed record GraphDiagramOperationBatch(
    long BaseGraphVersion,
    long GraphVersion,
    ImmutableArray<GhostagramOperation> Operations,
    bool RequiresFullProjection = false,
    IReadOnlyDictionary<string, JsonElement>? Metadata = null);
public interface IGraphDiagramDeltaProjector { GraphDiagramOperationBatch Project(GraphLocalChangeBatch batch, DiagramDocument currentDocument, GraphLocalSnapshot authoritativeSnapshot, GraphPresentationSnapshot presentation); }

public sealed record GraphDiagramCommand(long ExpectedGraphVersion, long ExpectedDiagramRevision, ImmutableArray<GhostagramOperation> Operations);
public sealed record GraphDiagramCommandResult(bool Accepted, string Code, string Message, long GraphVersion, long DiagramRevision, DiagramDocument AuthoritativeDocument, GraphLocalChangeBatch? GraphChanges = null);
public interface IGraphDiagramCommandAdapter { GraphDiagramCommandResult Apply(GraphDiagramCommand command); }
