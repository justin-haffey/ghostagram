using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Text.Json;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Ghostagram.Execution;
using Ghostworx.System.Graph;

namespace Ghostagram.Bridge;

public sealed class NodeKindDescriptorRegistry : INodeKindDescriptorRegistry
{
    private readonly IReadOnlyDictionary<NodeKind, NodeTypeDescriptor> _byKind;
    private readonly IReadOnlyDictionary<NodeTypeKey, NodeKind> _byType;

    public NodeKindDescriptorRegistry(IEnumerable<KeyValuePair<NodeKind, NodeTypeDescriptor>> registrations)
    {
        var values = registrations.ToArray();
        var duplicateKind = values.GroupBy(pair => pair.Key).FirstOrDefault(group => group.Count() > 1);
        if (duplicateKind is not null) throw new ArgumentException($"Duplicate node kind '{duplicateKind.Key}'.", nameof(registrations));
        var duplicateType = values.GroupBy(pair => pair.Value.Key).FirstOrDefault(group => group.Count() > 1);
        if (duplicateType is not null) throw new ArgumentException($"Duplicate node type '{duplicateType.Key}'.", nameof(registrations));
        _byKind = new ReadOnlyDictionary<NodeKind, NodeTypeDescriptor>(values.ToDictionary(pair => pair.Key, pair => pair.Value));
        _byType = new ReadOnlyDictionary<NodeTypeKey, NodeKind>(values.ToDictionary(pair => pair.Value.Key, pair => pair.Key));
    }

    public static NodeKindDescriptorRegistry Empty { get; } = new([]);
    public bool TryGet(NodeKind kind, out NodeTypeDescriptor descriptor) => _byKind.TryGetValue(kind, out descriptor!);
    public bool TryGetKind(string typeId, int version, out NodeKind kind) => _byType.TryGetValue(new(typeId, version), out kind);
}

public sealed class DefaultNodePresentationMapper(
    INodeKindDescriptorRegistry? descriptors = null,
    INodePortPresentationProfileRegistry? portProfiles = null) : INodePresentationMapper
{
    private readonly INodeKindDescriptorRegistry _descriptors = descriptors ?? NodeKindDescriptorRegistry.Empty;
    private readonly INodePortPresentationProfileRegistry _portProfiles = portProfiles ?? new NodePortPresentationProfileRegistry();

    public NodeDiagramProjection Map(GraphNodeSnapshot node, GraphPresentationSnapshot presentation, int ordinal)
    {
        var saved = presentation.Nodes.GetValueOrDefault(node.Id);
        var descriptor = _descriptors.TryGet(node.Kind, out var typed) ? typed : null;
        var bounds = saved?.Bounds ?? new DiagramBounds(64 + ordinal % 5 * 240, 64 + ordinal / 5 * 160, descriptor?.Width ?? 180, descriptor?.Height ?? 96);
        var properties = descriptor is null ? GenericProperties(node) : DescriptorProperties(node, descriptor);
        var projected = new DiagramNode(
            GraphDiagramIds.Node(node.Id), bounds.X, bounds.Y, bounds.Width, bounds.Height,
            node.NodeName ?? node.Kind.Name, Rotation: bounds.Rotation, Icon: descriptor?.Icon,
            Style: descriptor?.Style, TypeId: descriptor?.TypeId ?? node.Kind.QualifiedName,
            TypeVersion: descriptor?.Version ?? 1, Properties: properties,
            Sections: descriptor?.Sections.Select(section => new DiagramNodeSection(section.Id, section.Title, section.ParentSectionId, section.Order, section.Collapsible)).ToArray(),
            Presentation: descriptor?.Presentation, RendererKey: descriptor?.RendererKey, RendererVersion: descriptor?.RendererVersion);
        var ports = _portProfiles.TryGet(node.Kind, out var profile)
            ? ValidateProfilePorts(node, projected, profile.Map(node, projected))
            : descriptor is { Ports.Count: > 0 }
                ? DescriptorPorts(node, projected, descriptor)
                : GenericPorts(node, projected);
        return new(projected, ports);
    }

    private static IReadOnlyList<DiagramPort> GenericPorts(GraphNodeSnapshot node, DiagramNode projected) =>
    [
        new DiagramPort(GraphDiagramIds.InputPort(node.Id), projected.Id, "target", "graph", Anchor: "left", Label: "In", Order: 0),
        new DiagramPort(GraphDiagramIds.OutputPort(node.Id), projected.Id, "source", "graph", Anchor: "right", Label: "Out", Order: 1)
    ];

    private static IReadOnlyList<DiagramPort> DescriptorPorts(GraphNodeSnapshot node, DiagramNode projected, NodeTypeDescriptor descriptor) =>
        descriptor.Ports.OrderBy(port => port.Order).ThenBy(port => port.Id, StringComparer.Ordinal)
            .Select(port => new DiagramPort(GraphDiagramIds.Port(node.Id, port.Id), projected.Id, port.Direction, port.Scope,
                port.MaxConnections, Anchor: port.Anchor, PropertyId: port.PropertyId, Label: port.Label, Order: port.Order))
            .ToArray();

    private static IReadOnlyList<DiagramPort> ValidateProfilePorts(
        GraphNodeSnapshot node,
        DiagramNode projected,
        IReadOnlyList<DiagramPort>? ports)
    {
        if (ports is null) throw new InvalidOperationException($"Node port profile for '{node.Kind}' returned null.");
        var values = ports.Select(port => port with { }).ToArray();
        if (values.Any(port => !string.Equals(port.NodeId, projected.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Node port profile for '{node.Kind}' returned a port owned by another node.");
        if (values.Any(port => !GraphDiagramIds.TryPortNode(port.Id, out var owner) || owner != node.Id))
            throw new InvalidOperationException($"Node port profile for '{node.Kind}' must derive port ids from the stable graph node id.");
        if (values.Any(port => port.Direction is not ("source" or "target" or "both") || string.IsNullOrWhiteSpace(port.Scope)))
            throw new InvalidOperationException($"Node port profile for '{node.Kind}' returned an invalid direction or scope.");
        if (values.Select(port => port.Id).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidOperationException($"Node port profile for '{node.Kind}' returned duplicate port ids.");
        return values;
    }

    private static IReadOnlyList<DiagramNodeProperty> GenericProperties(GraphNodeSnapshot node)
    {
        var properties = new List<DiagramNodeProperty>
        {
            new("kind", "Kind", Value: JsonSerializer.SerializeToElement(node.Kind.QualifiedName), Label: "Kind")
        };
        properties.AddRange(node.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => GraphMetadataProjection.TrySerialize(pair.Value, null, out var value)
                ? new DiagramNodeProperty(pair.Key, pair.Key, InferType(pair.Value), value, Label: pair.Key)
                : null)
            .OfType<DiagramNodeProperty>());
        return properties;
    }

    private static IReadOnlyList<DiagramNodeProperty> DescriptorProperties(GraphNodeSnapshot node, NodeTypeDescriptor descriptor) =>
        descriptor.Properties.Select(property =>
        {
            var value = node.Metadata.TryGetValue(property.Id, out var metadata) && GraphMetadataProjection.TrySerialize(metadata, property.Type, out var serialized)
                ? serialized : property.DefaultValue?.Clone();
            return new DiagramNodeProperty(property.Id, property.Name, property.Type, value, property.Mode, property.Label, property.Description,
                property.Required, property.Connectable, property.Options, property.Metadata?.Clone(), property.SectionId, property.Editor);
        }).ToArray();

    private static string InferType(object? value) => value switch
    {
        bool => DiagramPropertyTypes.Boolean,
        byte or sbyte or short or ushort or int or uint or long or ulong => DiagramPropertyTypes.Integer,
        float or double or decimal => DiagramPropertyTypes.Decimal,
        DateTimeOffset => DiagramPropertyTypes.DateTime,
        JsonElement => DiagramPropertyTypes.Json,
        _ => DiagramPropertyTypes.String
    };
}

internal static class GraphMetadataProjection
{
    public static bool TrySerialize(object? value, string? declaredType, out JsonElement serialized)
    {
        if (declaredType is not null) return TrySerializeDeclared(value, declaredType, out serialized);
        switch (value)
        {
            case null: serialized = JsonSerializer.SerializeToElement<object?>(null); return true;
            case JsonElement json when json.ValueKind != JsonValueKind.Undefined: serialized = json.Clone(); return true;
            case string text: serialized = JsonSerializer.SerializeToElement(text); return true;
            case bool boolean: serialized = JsonSerializer.SerializeToElement(boolean); return true;
            case int integer: serialized = JsonSerializer.SerializeToElement(integer); return true;
            case long integer: serialized = JsonSerializer.SerializeToElement(integer); return true;
            case double number when double.IsFinite(number): serialized = JsonSerializer.SerializeToElement(number); return true;
            case decimal number: serialized = JsonSerializer.SerializeToElement(number); return true;
            case Guid guid: serialized = JsonSerializer.SerializeToElement(guid); return true;
            case DateTimeOffset date: serialized = JsonSerializer.SerializeToElement(date); return true;
            default: serialized = default; return false;
        }
    }

    private static bool TrySerializeDeclared(object? value, string type, out JsonElement serialized)
    {
        if (value is null) { serialized = JsonSerializer.SerializeToElement<object?>(null); return true; }
        switch (type)
        {
            case DiagramPropertyTypes.String:
            case DiagramPropertyTypes.Enum:
            case DiagramPropertyTypes.Date:
                if (value is string text) { serialized = JsonSerializer.SerializeToElement(text); return true; }
                break;
            case DiagramPropertyTypes.DateTime:
                if (value is DateTimeOffset date) { serialized = JsonSerializer.SerializeToElement(date); return true; }
                if (value is string dateText) { serialized = JsonSerializer.SerializeToElement(dateText); return true; }
                break;
            case DiagramPropertyTypes.Boolean:
                if (value is bool boolean) { serialized = JsonSerializer.SerializeToElement(boolean); return true; }
                break;
            case DiagramPropertyTypes.Integer:
                if (value is int integer) { serialized = JsonSerializer.SerializeToElement(integer); return true; }
                if (value is long longInteger) { serialized = JsonSerializer.SerializeToElement(longInteger); return true; }
                break;
            case DiagramPropertyTypes.Decimal:
                if (value is double number && double.IsFinite(number)) { serialized = JsonSerializer.SerializeToElement(number); return true; }
                if (value is decimal decimalNumber) { serialized = JsonSerializer.SerializeToElement(decimalNumber); return true; }
                break;
            case DiagramPropertyTypes.Json:
                if (value is JsonElement json && json.ValueKind != JsonValueKind.Undefined) { serialized = json.Clone(); return true; }
                break;
        }
        serialized = default;
        return false;
    }
}

public sealed class DefaultRelationshipPresentationMapper(IRelationshipPresentationProfileRegistry? profiles = null) : IRelationshipPresentationMapper
{
    private readonly IRelationshipPresentationProfileRegistry _profiles = profiles ?? new RelationshipPresentationProfileRegistry();

    public DiagramEdge Map(
        GraphRelationshipSnapshot item,
        GraphPresentationSnapshot presentation,
        IReadOnlyList<DiagramPort> sourcePorts,
        IReadOnlyList<DiagramPort> targetPorts)
    {
        if (_profiles.TryGet(item.Relationship.Kind, out var profile))
        {
            var profiled = profile.Map(item, presentation, sourcePorts, targetPorts);
            if (!string.Equals(profiled.Id, GraphDiagramIds.Edge(item.Relationship.Id), StringComparison.Ordinal))
                throw new InvalidOperationException($"Relationship profile for '{item.Relationship.Kind}' must preserve the stable edge id.");
            if (!GraphDiagramIds.TryPortNode(profiled.SourcePortId, out var source) || source != item.Relationship.Source ||
                !GraphDiagramIds.TryPortNode(profiled.TargetPortId, out var target) || target != item.Relationship.Target)
                throw new InvalidOperationException($"Relationship profile for '{item.Relationship.Kind}' must preserve stable source and target node ownership.");
            if (sourcePorts.All(port => port.Id != profiled.SourcePortId) || targetPorts.All(port => port.Id != profiled.TargetPortId))
                throw new InvalidOperationException($"Relationship profile for '{item.Relationship.Kind}' selected a port not projected for its endpoint.");
            return profiled;
        }
        var edge = item.Relationship;
        var sourcePort = PreferredPort(sourcePorts, GraphDiagramIds.OutputPort(edge.Source), "source");
        var targetPort = PreferredPort(targetPorts, GraphDiagramIds.InputPort(edge.Target), "target");
        return new DiagramEdge(GraphDiagramIds.Edge(edge.Id), sourcePort.Id, targetPort.Id,
            edge.Label, Type: edge.Kind.QualifiedName,
            Waypoints: presentation.EdgeWaypoints.TryGetValue(edge.Id, out var points) ? points.Select(point => point with { }).ToArray() : []);
    }

    private static DiagramPort PreferredPort(IReadOnlyList<DiagramPort> ports, string conventionalId, string direction) =>
        ports.FirstOrDefault(port => port.Id == conventionalId)
        ?? ports.Where(port => port.Direction == direction || port.Direction == "both").OrderBy(port => port.Order).ThenBy(port => port.Id, StringComparer.Ordinal).FirstOrDefault()
        ?? throw new InvalidOperationException($"A projected graph relationship requires a {direction} port.");
}

public sealed class ContainsHierarchyPresentationMapper(RelationshipKind? containmentKind = null) : IHierarchyPresentationMapper
{
    private readonly RelationshipKind _containmentKind = containmentKind ?? RelationshipKind.Contains;

    public HierarchyDiagramProjection Map(GraphSnapshot snapshot, IReadOnlyDictionary<NodeId, DiagramNode> nodes, GraphPresentationSnapshot presentation)
    {
        var containment = snapshot.Relationships.Where(item => item.Relationship.Kind == _containmentKind)
            .Select(item => item.Relationship).OrderBy(edge => edge.Id.Value).ToArray();
        var parents = containment.GroupBy(edge => edge.Target).ToDictionary(group => group.Key, group => group.OrderBy(edge => edge.Source.Value).First().Source);
        var groupNodeIds = containment.Select(edge => edge.Source).Distinct().OrderBy(id => id.Value).ToArray();
        var nodeSnapshots = snapshot.Nodes.ToDictionary(node => node.Id);
        var groups = new List<DiagramGroup>();
        foreach (var groupNodeId in groupNodeIds)
        {
            var saved = presentation.Groups.GetValueOrDefault(groupNodeId);
            var children = containment.Where(edge => edge.Source == groupNodeId).Select(edge => edge.Target).Where(nodes.ContainsKey).Select(id => nodes[id]).ToArray();
            var bounds = saved?.Bounds ?? BoundsFor(children, nodes.GetValueOrDefault(groupNodeId));
            var parentGroup = parents.TryGetValue(groupNodeId, out var parentId) && groupNodeIds.Contains(parentId) ? GraphDiagramIds.Group(parentId) : null;
            groups.Add(new DiagramGroup(GraphDiagramIds.Group(groupNodeId), bounds.X, bounds.Y, bounds.Width, bounds.Height,
                nodeSnapshots.GetValueOrDefault(groupNodeId)?.NodeName ?? "Group", parentGroup, saved?.Collapsed ?? false));
        }
        var assignments = parents.Where(pair => groupNodeIds.Contains(pair.Value)).ToDictionary(pair => pair.Key, pair => GraphDiagramIds.Group(pair.Value));
        return new(groups, assignments);
    }

    private static DiagramBounds BoundsFor(IReadOnlyList<DiagramNode> children, DiagramNode? groupNode)
    {
        if (children.Count == 0 && groupNode is not null) return new(groupNode.X - 24, groupNode.Y - 36, Math.Max(240, groupNode.Width + 48), Math.Max(160, groupNode.Height + 60));
        if (children.Count == 0) return new(40, 40, 320, 220);
        var left = children.Min(node => node.X) - 24;
        var top = children.Min(node => node.Y) - 40;
        var right = children.Max(node => node.X + node.Width) + 24;
        var bottom = children.Max(node => node.Y + node.Height) + 24;
        return new(left, top, Math.Max(160, right - left), Math.Max(100, bottom - top));
    }
}

public sealed class GraphDiagramProjection(
    INodePresentationMapper? nodes = null,
    IRelationshipPresentationMapper? relationships = null,
    IHierarchyPresentationMapper? hierarchy = null) : IGraphDiagramProjection
{
    private static readonly JsonSerializerOptions ProjectionJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INodePresentationMapper _nodes = nodes ?? new DefaultNodePresentationMapper();
    private readonly IRelationshipPresentationMapper _relationships = relationships ?? new DefaultRelationshipPresentationMapper();
    private readonly IHierarchyPresentationMapper _hierarchy = hierarchy ?? new ContainsHierarchyPresentationMapper();

    public DiagramDocument Project(GraphSnapshot snapshot, GraphPresentationSnapshot presentation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(presentation);
        var projected = snapshot.Nodes.OrderBy(node => node.Id.Value).Select((node, ordinal) => ProjectNode(node, presentation, ordinal)).ToArray();
        var nodeById = projected.ToDictionary(item => new NodeId(Guid.ParseExact(item.Node.Id, "N")), item => item.Node);
        var portsByNode = projected.ToDictionary(item => new NodeId(Guid.ParseExact(item.Node.Id, "N")), item => item.Ports);
        var hierarchy = _hierarchy.Map(snapshot, nodeById, presentation);
        var diagramNodes = projected.Select(item => hierarchy.NodeGroups.TryGetValue(new NodeId(Guid.ParseExact(item.Node.Id, "N")), out var groupId) ? item.Node with { GroupId = groupId } : item.Node).ToArray();
        var document = new DiagramDocument(snapshot.GraphId.ToString(), diagramNodes, projected.SelectMany(item => item.Ports).ToArray(),
            snapshot.Relationships.OrderBy(item => item.Relationship.Id.Value).Select(item => ProjectRelationship(
                item, presentation, portsByNode[item.Relationship.Source], portsByNode[item.Relationship.Target])).ToArray(),
            hierarchy.Groups, presentation.Viewport, Selection: presentation.Selection);
        var diagnostics = snapshot.Nodes.SelectMany(node => node.Metadata
            .Where(pair => !GraphMetadataProjection.TrySerialize(pair.Value, null, out _))
            .Select(pair => new GraphProjectionDiagnostic("UNSUPPORTED_METADATA", node.Id.ToString(), pair.Key,
                $"Metadata '{pair.Key}' was omitted because its CLR value has no explicit durable JSON projection.")))
            .ToArray();
        return document with { ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["graphVersion"] = JsonSerializer.SerializeToElement(snapshot.Version),
            ["presentationRevision"] = JsonSerializer.SerializeToElement(presentation.Revision),
            ["projectionDiagnostics"] = JsonSerializer.SerializeToElement(diagnostics, ProjectionJsonOptions)
        } };
    }

    internal NodeDiagramProjection ProjectNode(GraphNodeSnapshot node, GraphPresentationSnapshot presentation, int ordinal) => _nodes.Map(node, presentation, ordinal);
    internal DiagramEdge ProjectRelationship(
        GraphRelationshipSnapshot relationship,
        GraphPresentationSnapshot presentation,
        IReadOnlyList<DiagramPort> sourcePorts,
        IReadOnlyList<DiagramPort> targetPorts) => _relationships.Map(relationship, presentation, sourcePorts, targetPorts);
}

public sealed class GraphDiagramDeltaProjector(IGraphDiagramProjection projection) : IGraphDiagramDeltaProjector
{
    public GraphDiagramOperationBatch Project(GraphChangeBatch batch, DiagramDocument currentDocument, GraphSnapshot authoritativeSnapshot, GraphPresentationSnapshot presentation)
    {
        ArgumentNullException.ThrowIfNull(batch); ArgumentNullException.ThrowIfNull(currentDocument); ArgumentNullException.ThrowIfNull(authoritativeSnapshot);
        var metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["graphVersion"] = JsonSerializer.SerializeToElement(authoritativeSnapshot.Version),
            ["presentationRevision"] = JsonSerializer.SerializeToElement(presentation.Revision)
        };
        if (authoritativeSnapshot.Version != batch.Version || projection is not GraphDiagramProjection direct)
            return new(batch.BaseVersion, batch.Version, [], true, metadata);

        var operations = new List<GhostagramOperation>();
        var nodes = authoritativeSnapshot.Nodes.ToDictionary(node => node.Id);
        var relationships = authoritativeSnapshot.Relationships.ToDictionary(item => item.Relationship.Id);
        var currentNodes = currentDocument.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var currentPorts = currentDocument.Ports.GroupBy(port => port.NodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var ordinals = authoritativeSnapshot.Nodes.OrderBy(node => node.Id.Value)
            .Select((node, ordinal) => (node.Id, ordinal)).ToDictionary(item => item.Id, item => item.ordinal);
        var changedNodes = new HashSet<NodeId>();
        var reprojectOrdinalDefaults = false;

        foreach (var change in batch.Changes)
        {
            switch (change.Kind)
            {
                case GraphChangeKind.NodeRegistered:
                    reprojectOrdinalDefaults = true;
                    if (change.NodeId is { } registeredNode) changedNodes.Add(registeredNode);
                    break;
                case GraphChangeKind.NodeRenamed:
                case GraphChangeKind.NodeMetadataSet:
                case GraphChangeKind.NodeMetadataRemoved:
                    if (change.NodeId is { } changedNode) changedNodes.Add(changedNode);
                    break;
                case GraphChangeKind.NodeUnregistered:
                case GraphChangeKind.NodePruned:
                    reprojectOrdinalDefaults = true;
                    if (change.NodeId is not { } removedNode) return new(batch.BaseVersion, batch.Version, [], true, metadata);
                    var diagramNodeId = GraphDiagramIds.Node(removedNode);
                    if (currentPorts.TryGetValue(diagramNodeId, out var removedPorts))
                        operations.AddRange(removedPorts.OrderBy(port => port.Id, StringComparer.Ordinal).Select(port => DiagramOperations.RemovePort(port.Id)));
                    operations.Add(DiagramOperations.RemoveNode(diagramNodeId));
                    if (currentDocument.Groups.Any(group => group.Id == GraphDiagramIds.Group(removedNode)))
                        operations.Add(DiagramOperations.RemoveGroup(GraphDiagramIds.Group(removedNode)));
                    break;
                case GraphChangeKind.RelationshipConnected:
                    if (change.Relationship is not { } connected || connected.Kind == RelationshipKind.Contains)
                        return new(batch.BaseVersion, batch.Version, [], true, metadata);
                    if (relationships.TryGetValue(connected.Id, out var connectedSnapshot))
                    {
                        if (!nodes.TryGetValue(connected.Source, out var sourceNode) || !nodes.TryGetValue(connected.Target, out var targetNode))
                            return new(batch.BaseVersion, batch.Version, [], true, metadata);
                        var source = direct.ProjectNode(sourceNode, presentation, ordinals[connected.Source]);
                        var target = direct.ProjectNode(targetNode, presentation, ordinals[connected.Target]);
                        operations.Add(DiagramOperations.Upsert(direct.ProjectRelationship(connectedSnapshot, presentation, source.Ports, target.Ports)));
                    }
                    break;
                case GraphChangeKind.RelationshipDisconnected:
                    if (change.Relationship is not { } disconnected || disconnected.Kind == RelationshipKind.Contains)
                        return new(batch.BaseVersion, batch.Version, [], true, metadata);
                    operations.Add(DiagramOperations.RemoveEdge(GraphDiagramIds.Edge(disconnected.Id)));
                    break;
                case GraphChangeKind.RelationshipMetadataSet:
                case GraphChangeKind.RelationshipMetadataRemoved:
                case GraphChangeKind.FeatureAdded:
                case GraphChangeKind.FeatureRemoved:
                case GraphChangeKind.BatchCommitted:
                    break;
                default:
                    return new(batch.BaseVersion, batch.Version, [], true, metadata);
            }
        }
        if (reprojectOrdinalDefaults) changedNodes.UnionWith(nodes.Keys);
        foreach (var nodeId in changedNodes.OrderBy(id => id.Value))
        {
            if (!nodes.TryGetValue(nodeId, out var node)) return new(batch.BaseVersion, batch.Version, [], true, metadata);
            var mapped = direct.ProjectNode(node, presentation, ordinals[nodeId]);
            if (currentNodes.TryGetValue(mapped.Node.Id, out var current) && current.GroupId is not null)
                mapped = mapped with { Node = mapped.Node with { GroupId = current.GroupId } };
            operations.Add(DiagramOperations.Upsert(mapped.Node));
            var existingPortIds = currentPorts.GetValueOrDefault(mapped.Node.Id)?.Select(port => port.Id).ToHashSet(StringComparer.Ordinal) ?? [];
            var mappedPortIds = mapped.Ports.Select(port => port.Id).ToHashSet(StringComparer.Ordinal);
            operations.AddRange(existingPortIds.Except(mappedPortIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(DiagramOperations.RemovePort));
            operations.AddRange(mapped.Ports.OrderBy(port => port.Order).ThenBy(port => port.Id, StringComparer.Ordinal).Select(DiagramOperations.Upsert));
        }
        if (!currentDocument.Selection.SequenceEqual(presentation.Selection)) operations.Add(DiagramOperations.Select(presentation.Selection));
        if (currentDocument.Viewport != presentation.Viewport) operations.Add(DiagramOperations.SetViewport(presentation.Viewport));
        return new(batch.BaseVersion, batch.Version, operations.ToImmutableArray(), false, metadata);
    }
}
