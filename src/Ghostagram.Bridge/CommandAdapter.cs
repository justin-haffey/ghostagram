using System.Collections.Immutable;
using System.Text.Json;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Ghostworx.System.Graph;

namespace Ghostagram.Bridge;

public sealed class GraphDiagramCommandAdapter : IGraphDiagramCommandAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IGraph _graph;
    private readonly IGraphSnapshotSource _snapshots;
    private readonly ITransactionalGraph _transactions;
    private readonly IGraphPresentationStore _presentation;
    private readonly IGraphDiagramProjection _projection;
    private readonly INodeKindDescriptorRegistry _descriptors;

    public GraphDiagramCommandAdapter(IGraph graph, IGraphPresentationStore presentation, IGraphDiagramProjection projection, INodeKindDescriptorRegistry? descriptors = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _snapshots = graph as IGraphSnapshotSource ?? throw new ArgumentException("The graph must support authoritative snapshots.", nameof(graph));
        _transactions = graph as ITransactionalGraph ?? throw new ArgumentException("The graph must support optimistic transactions.", nameof(graph));
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _descriptors = descriptors ?? NodeKindDescriptorRegistry.Empty;
    }

    public GraphDiagramCommandResult Apply(GraphDiagramCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_graph.Version != command.ExpectedGraphVersion) return Conflict("GRAPH_VERSION_CONFLICT", $"Expected graph version {command.ExpectedGraphVersion}, but the current version is {_graph.Version}.");
        GraphChangeBatch? changes = null;
        try
        {
            var commit = _presentation.Execute(command.ExpectedDiagramRevision, editor =>
            {
                using var transaction = _transactions.BeginTransaction(command.ExpectedGraphVersion);
                var snapshot = _snapshots.CaptureSnapshot();
                var pendingNodes = new Dictionary<NodeId, INode>();
                var pendingEdges = new List<PendingEdgePresentation>();
                foreach (var operation in command.Operations) ApplyOperation(operation, transaction, editor, snapshot, pendingNodes, pendingEdges);
                var committed = transaction.Commit();
                foreach (var pending in pendingEdges)
                {
                    var relationship = committed.Changes
                        .Where(change => change.Kind == GraphChangeKind.RelationshipConnected && change.Relationship is not null)
                        .Select(change => change.Relationship!.Value)
                        .Single(edge => edge.Source == pending.Source && edge.Target == pending.Target && edge.Kind == pending.Kind && edge.Label == pending.Label);
                    editor.SetWaypoints(relationship.Id, pending.Waypoints);
                }
                return committed;
            });
            if (!commit.Accepted) return Conflict("DIAGRAM_REVISION_CONFLICT", $"Expected diagram revision {command.ExpectedDiagramRevision}, but the current revision is {commit.Revision}.");
            changes = commit.Value;
            return Success(changes!);
        }
        catch (GraphVersionConflictException conflict)
        {
            return Conflict("GRAPH_VERSION_CONFLICT", conflict.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException)
        {
            return Conflict("INVALID_PROPOSAL", exception.Message);
        }
    }

    private void ApplyOperation(GhostagramOperation operation, IGraphTransaction transaction, GraphPresentationEditor editor, GraphSnapshot snapshot,
        Dictionary<NodeId, INode> pendingNodes, List<PendingEdgePresentation> pendingEdges)
    {
        switch (operation.Type)
        {
            case "node.upsert": ApplyNode(Deserialize<DiagramNode>(operation), transaction, editor, snapshot, pendingNodes); break;
            case "node.remove":
            {
                var id = RequireNodeId(operation.Id ?? Property(operation.Value, "id"));
                transaction.Unregister(id); editor.RemoveNode(id); editor.RemoveGroup(id);
                editor.Selection.Remove(GraphDiagramIds.Node(id)); editor.Selection.Remove(GraphDiagramIds.Group(id));
                foreach (var edge in snapshot.Relationships.Where(item => item.Relationship.Source == id || item.Relationship.Target == id))
                {
                    editor.RemoveWaypoints(edge.Relationship.Id);
                    editor.Selection.Remove(GraphDiagramIds.Edge(edge.Relationship.Id));
                }
                break;
            }
            case "port.upsert": case "port.remove": break; // Synthetic ports are graph-derived and cannot own semantics.
            case "edge.upsert": ApplyEdge(Deserialize<DiagramEdge>(operation), transaction, editor, snapshot, pendingNodes, pendingEdges); break;
            case "edge.remove":
            {
                var id = RequireEdgeId(operation.Id ?? Property(operation.Value, "id"));
                transaction.Disconnect(id); editor.RemoveWaypoints(id); editor.Selection.Remove(GraphDiagramIds.Edge(id)); break;
            }
            case "group.upsert": ApplyGroup(Deserialize<DiagramGroup>(operation), editor, snapshot, pendingNodes); break;
            case "group.remove": ApplyGroupRemoval(operation.Id ?? Property(operation.Value, "id"), transaction, editor, snapshot); break;
            case "group.assignNode": ApplyGroupAssignment(operation.Value, transaction, snapshot, pendingNodes); break;
            case "selection.replace": ApplySelection(operation.Value, editor, snapshot, pendingNodes); break;
            case "viewport.set":
            {
                var viewport = operation.Value.Deserialize<DiagramViewport>(JsonOptions) ?? throw new ArgumentException("A viewport is required.");
                ValidateViewport(viewport); editor.SetViewport(viewport); break;
            }
            default: throw new ArgumentException($"Unsupported graph diagram proposal '{operation.Type}'.");
        }
    }

    private void ApplyNode(DiagramNode node, IGraphTransaction transaction, GraphPresentationEditor editor, GraphSnapshot snapshot, Dictionary<NodeId, INode> pendingNodes)
    {
        ValidateBounds(node.X, node.Y, node.Width, node.Height, node.Rotation, "Node");
        ValidateProperties(node.Properties);
        var id = RequireNodeId(node.Id);
        var existing = snapshot.Nodes.FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            var kind = ResolveKind(node);
            var created = new BridgeMutableNode(id, kind, node.Label, _graph);
            foreach (var property in node.Properties.Where(property => property.Id != "kind")) created.SetMetadata(property.Id, ToClr(property));
            transaction.Register(created); pendingNodes[id] = created;
        }
        else
        {
            if (!StringComparer.Ordinal.Equals(existing.NodeName, node.Label)) transaction.Rename(id, node.Label);
            var replacements = node.Properties.Where(property => property.Id != "kind").ToDictionary(property => property.Id, StringComparer.Ordinal);
            foreach (var removed in existing.Metadata.Keys.Except(replacements.Keys, StringComparer.Ordinal))
                transaction.RemoveNodeMetadata(id, removed);
            foreach (var property in replacements.Values)
            {
                var value = ToClr(property);
                if (!existing.Metadata.TryGetValue(property.Id, out var old) || !Equals(old, value)) transaction.SetNodeMetadata(id, property.Id, value);
            }
        }
        editor.SetNode(id, new(new(node.X, node.Y, node.Width, node.Height, node.Rotation)));
        Reparent(id, node.GroupId, transaction, snapshot, pendingNodes);
    }

    private void ApplyEdge(DiagramEdge edge, IGraphTransaction transaction, GraphPresentationEditor editor, GraphSnapshot snapshot,
        Dictionary<NodeId, INode> pendingNodes, List<PendingEdgePresentation> pendingEdges)
    {
        ValidateWaypoints(edge.Waypoints ?? []);
        if (GraphDiagramIds.TryEdge(edge.Id, out var edgeId) && snapshot.Relationships.FirstOrDefault(item => item.Relationship.Id == edgeId) is { } existing)
        {
            if (!StringComparer.Ordinal.Equals(existing.Relationship.Label, edge.Label) || (edge.Type is not null && edge.Type != existing.Relationship.Kind.QualifiedName))
                throw new InvalidOperationException("Existing relationship kind and label are graph-owned and cannot be rewritten by edge.upsert.");
            editor.SetWaypoints(edgeId, edge.Waypoints ?? []);
            return;
        }
        if (!GraphDiagramIds.TryPortNode(edge.SourcePortId, out var sourceId) || !GraphDiagramIds.TryPortNode(edge.TargetPortId, out var targetId))
            throw new ArgumentException("Graph edges must use deterministic graph node ports.");
        var source = RequireNode(sourceId, pendingNodes);
        var target = RequireNode(targetId, pendingNodes);
        var kind = ParseRelationshipKind(edge.Type);
        transaction.Connect(source, target, kind, edge.Label);
        pendingEdges.Add(new(sourceId, targetId, kind, edge.Label, (edge.Waypoints ?? []).Select(point => point with { }).ToArray()));
    }

    private void ApplyGroup(DiagramGroup group, GraphPresentationEditor editor, GraphSnapshot snapshot, IReadOnlyDictionary<NodeId, INode> pendingNodes)
    {
        ValidateBounds(group.X, group.Y, group.Width, group.Height, 0, "Group");
        if (!GraphDiagramIds.TryGroup(group.Id, out var id)) throw new ArgumentException("Graph group ids must identify a semantic graph node.");
        if (!snapshot.Nodes.Any(node => node.Id == id) && !pendingNodes.ContainsKey(id)) throw new ArgumentException($"Graph group '{group.Id}' does not reference an existing semantic node.");
        if (group.ParentGroupId is not null && (!GraphDiagramIds.TryGroup(group.ParentGroupId, out var parentId) ||
            !snapshot.Nodes.Any(node => node.Id == parentId) && !pendingNodes.ContainsKey(parentId)))
            throw new ArgumentException($"Parent group '{group.ParentGroupId}' does not reference an existing semantic node.");
        editor.SetGroup(id, new(new(group.X, group.Y, group.Width, group.Height), group.Collapsed));
    }

    private static void ApplyGroupRemoval(string? groupId, IGraphTransaction transaction, GraphPresentationEditor editor, GraphSnapshot snapshot)
    {
        if (!GraphDiagramIds.TryGroup(groupId, out var id)) throw new ArgumentException("Graph group ids must identify a semantic graph node.");
        foreach (var relationship in snapshot.Relationships.Where(item => item.Relationship.Source == id && item.Relationship.Kind == RelationshipKind.Contains)) transaction.Disconnect(relationship.Relationship.Id);
        editor.RemoveGroup(id); editor.Selection.Remove(GraphDiagramIds.Group(id));
    }

    private void ApplyGroupAssignment(JsonElement value, IGraphTransaction transaction, GraphSnapshot snapshot, Dictionary<NodeId, INode> pendingNodes)
    {
        var nodeId = RequireNodeId(value.GetProperty("nodeId").GetString());
        var groupId = value.TryGetProperty("groupId", out var group) && group.ValueKind != JsonValueKind.Null ? group.GetString() : null;
        Reparent(nodeId, groupId, transaction, snapshot, pendingNodes);
    }

    private void Reparent(NodeId nodeId, string? groupId, IGraphTransaction transaction, GraphSnapshot snapshot, Dictionary<NodeId, INode> pendingNodes)
    {
        var existing = snapshot.Relationships.Where(item => item.Relationship.Target == nodeId && item.Relationship.Kind == RelationshipKind.Contains).Select(item => item.Relationship).ToArray();
        NodeId? parentId = null;
        if (groupId is not null)
        {
            if (!GraphDiagramIds.TryGroup(groupId, out var parsed)) throw new ArgumentException("Graph group ids must identify a semantic graph node.");
            _ = RequireNode(parsed, pendingNodes);
            parentId = parsed;
        }
        if (existing.Length == (parentId is null ? 0 : 1) && (parentId is null || existing[0].Source == parentId.Value)) return;
        foreach (var relationship in existing) transaction.Disconnect(relationship.Id);
        if (parentId is null) return;
        transaction.Connect(RequireNode(parentId.Value, pendingNodes), RequireNode(nodeId, pendingNodes), RelationshipKind.Contains);
    }

    private INode RequireNode(NodeId id, IReadOnlyDictionary<NodeId, INode> pendingNodes) => pendingNodes.TryGetValue(id, out var pending) ? pending
        : _graph.TryGetNode(id, out var node) && node is not null ? node
        : throw new KeyNotFoundException($"Graph node '{id}' does not exist.");

    private NodeKind ResolveKind(DiagramNode node)
    {
        if (node.TypeId is not null && _descriptors.TryGetKind(node.TypeId, node.TypeVersion, out var registered)) return registered;
        return ParseNodeKind(node.TypeId);
    }

    private static NodeKind ParseNodeKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return NodeKind.Other;
        var separator = value.LastIndexOf(':');
        return separator > 0 && separator < value.Length - 1 ? NodeKind.Define(value[..separator], value[(separator + 1)..]) : NodeKind.Define("Ghostagram.Bridge", value);
    }

    private static RelationshipKind ParseRelationshipKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return RelationshipKind.References;
        var separator = value.LastIndexOf(':');
        return separator > 0 && separator < value.Length - 1 ? RelationshipKind.Define(value[..separator], value[(separator + 1)..]) : RelationshipKind.Define("Ghostagram.Bridge", value);
    }

    private GraphDiagramCommandResult Success(GraphChangeBatch changes)
    {
        var snapshot = _snapshots.CaptureSnapshot();
        var presentation = _presentation.Capture();
        return new(true, "OK", "Graph and presentation command committed.", snapshot.Version, presentation.Revision, _projection.Project(snapshot, presentation), changes);
    }

    private GraphDiagramCommandResult Conflict(string code, string message)
    {
        var snapshot = _snapshots.CaptureSnapshot();
        var presentation = _presentation.Capture();
        return new(false, code, message, snapshot.Version, presentation.Revision, _projection.Project(snapshot, presentation));
    }

    private static T Deserialize<T>(GhostagramOperation operation) => operation.Value.Deserialize<T>(JsonOptions) ?? throw new ArgumentException($"Operation '{operation.Type}' requires a value.");
    private static string? Property(JsonElement value, string name) => value.TryGetProperty(name, out var property) ? property.GetString() : null;
    private static NodeId RequireNodeId(string? value) => GraphDiagramIds.TryNode(value, out var id) ? id : throw new ArgumentException($"'{value}' is not a graph node id.");
    private static EdgeId RequireEdgeId(string? value) => GraphDiagramIds.TryEdge(value, out var id) ? id : throw new ArgumentException($"'{value}' is not a graph edge id.");
    private static object? ToClr(DiagramNodeProperty property)
    {
        if (property.Value is not { } value || value.ValueKind == JsonValueKind.Null) return null;
        return property.Type switch
        {
            DiagramPropertyTypes.String or DiagramPropertyTypes.Enum or DiagramPropertyTypes.Date when value.ValueKind == JsonValueKind.String => value.GetString(),
            DiagramPropertyTypes.DateTime when value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var date) => date,
            DiagramPropertyTypes.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
            DiagramPropertyTypes.Integer when value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var integer) => integer,
            DiagramPropertyTypes.Decimal when value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) => number,
            DiagramPropertyTypes.Json when value.ValueKind != JsonValueKind.Undefined => value.Clone(),
            _ => throw new ArgumentException($"Property '{property.Id}' value is incompatible with declared type '{property.Type}'.")
        };
    }

    private static void ValidateProperties(IReadOnlyList<DiagramNodeProperty> properties)
    {
        if (properties.Any(property => string.IsNullOrWhiteSpace(property.Id))) throw new ArgumentException("Node property ids are required.");
        var duplicate = properties.GroupBy(property => property.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new ArgumentException($"Node property '{duplicate.Key}' is duplicated.");
        foreach (var property in properties.Where(property => property.Id != "kind")) _ = ToClr(property);
    }

    private static void ValidateBounds(double x, double y, double width, double height, double rotation, string kind)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(rotation) || width <= 0 || height <= 0)
            throw new ArgumentException($"{kind} geometry must be finite with positive width and height.");
    }

    private static void ValidateViewport(DiagramViewport viewport)
    {
        if (!double.IsFinite(viewport.X) || !double.IsFinite(viewport.Y) || !double.IsFinite(viewport.Zoom) || viewport.Zoom <= 0)
            throw new ArgumentException("Viewport coordinates must be finite and zoom must be positive.");
    }

    private static void ValidateWaypoints(IEnumerable<DiagramPoint> points)
    {
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("Edge waypoints must be finite.");
    }

    private static void ApplySelection(JsonElement value, GraphPresentationEditor editor, GraphSnapshot snapshot, IReadOnlyDictionary<NodeId, INode> pendingNodes)
    {
        var ids = value.GetProperty("ids").EnumerateArray().Select(item => item.GetString() ?? throw new ArgumentException("Selection ids must be strings.")).ToArray();
        var nodeIds = snapshot.Nodes.Select(node => node.Id).Concat(pendingNodes.Keys).ToHashSet();
        var edgeIds = snapshot.Relationships.Select(item => item.Relationship.Id).ToHashSet();
        foreach (var id in ids)
        {
            var valid = GraphDiagramIds.TryNode(id, out var nodeId) && nodeIds.Contains(nodeId)
                || GraphDiagramIds.TryEdge(id, out var edgeId) && edgeIds.Contains(edgeId)
                || GraphDiagramIds.TryGroup(id, out var groupId) && nodeIds.Contains(groupId);
            if (!valid) throw new ArgumentException($"Selection id '{id}' does not reference authoritative graph state.");
        }
        editor.SetSelection(ids);
    }

    private sealed record PendingEdgePresentation(NodeId Source, NodeId Target, RelationshipKind Kind, string? Label, IReadOnlyList<DiagramPoint> Waypoints);

    private sealed class BridgeMutableNode(NodeId id, NodeKind kind, string? name, IGraph graph) : IMutableNode
    {
        private readonly Dictionary<string, object?> _metadata = new(StringComparer.Ordinal);
        public NodeId Id { get; } = id;
        public NodeKind Kind { get; } = kind;
        public string? NodeName { get; private set; } = name;
        public IGraph Graph { get; } = graph;
        public IReadOnlyDictionary<string, object?> Metadata => new Dictionary<string, object?>(_metadata, StringComparer.Ordinal);
        public IReadOnlyList<GraphEdge> Edges => Graph.GetEdges(Id);
        public void Rename(string? nodeName) => NodeName = nodeName;
        public void SetMetadata(string key, object? value) => _metadata[key] = value;
        public bool RemoveMetadata(string key) => _metadata.Remove(key);
    }
}
