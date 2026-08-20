using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ghostagram.Core;
using Ghostworx.System.Graph.Algorithms;
using Ghostworx.System.Graph.Features;
using Ghostworx.System.Graph.Validation;
using SystemGraph = Ghostworx.System.Graph;

namespace Ghostagram.Execution;

public enum GraphCompileProfile
{
    DagOnly,
    BoundedCycles,
    AdapterNative
}

public sealed record LoopGuard(string NodeId, int MaxIterations);
public sealed record GraphCompileOptions(GraphCompileProfile Profile, IReadOnlyList<LoopGuard>? LoopGuards = null);
public sealed record GraphDiagnostic(string Code, string Message, IReadOnlyList<string> NodeIds, IReadOnlyList<string>? EdgeIds = null);
public sealed record ProjectedEdge(string EdgeId, string SourceNodeId, string TargetNodeId, string SourcePortId, string TargetPortId);
public sealed record ExecutionStage(int Order, IReadOnlyList<string> NodeIds, IReadOnlyList<LoopGuard>? LoopGuards = null);
public sealed record CompiledGraph(
    string DocumentId,
    GraphCompileProfile Profile,
    string PlanFingerprint,
    IReadOnlyList<DiagramNode> Nodes,
    IReadOnlyList<ProjectedEdge> Edges,
    IReadOnlyList<ExecutionStage> Stages);
public sealed record GraphCompilationResult(CompiledGraph? Graph, IReadOnlyList<GraphDiagnostic> Diagnostics)
{
    public bool Succeeded => Graph is not null && Diagnostics.Count == 0;
}

public interface IGraphCompiler
{
    /// <summary>
    /// Temporary DiagramDocument compatibility adapter. Remove only after persisted documents,
    /// execution callers, and graph-to-diagram round trips use <see cref="IGraphSnapshotCompiler"/>
    /// with parity coverage.
    /// </summary>
    [Obsolete("DiagramDocument compilation is a compatibility adapter. Compile a Ghostworx.System.Graph.GraphSnapshot through IGraphSnapshotCompiler instead.")]
    GraphCompilationResult Compile(DiagramDocument document, GraphCompileOptions options);
}

public interface IGraphSnapshotCompiler
{
    GraphCompilationResult Compile(SystemGraph.GraphSnapshot snapshot, GraphCompileOptions options);
}

public sealed record GraphSnapshotProjectionResult(
    SystemGraph.GraphSnapshot? Snapshot,
    IReadOnlyList<GraphDiagnostic> Diagnostics)
{
    public bool Succeeded => Snapshot is not null && Diagnostics.Count == 0;
}

public static class GraphDiagnosticCodes
{
    public const string DuplicateNode = "GRAPH_DUPLICATE_NODE";
    public const string UnknownNodeType = "GRAPH_UNKNOWN_NODE_TYPE";
    public const string NodeSchemaMismatch = "GRAPH_NODE_SCHEMA_MISMATCH";
    public const string DuplicatePort = "GRAPH_DUPLICATE_PORT";
    public const string DuplicateEdge = "GRAPH_DUPLICATE_EDGE";
    public const string DuplicateProperty = "GRAPH_DUPLICATE_PROPERTY";
    public const string RequiredPropertyMissing = "GRAPH_REQUIRED_PROPERTY_MISSING";
    public const string PropertyValueInvalid = "GRAPH_PROPERTY_VALUE_INVALID";
    public const string PortNodeMissing = "GRAPH_PORT_NODE_MISSING";
    public const string PortPropertyMissing = "GRAPH_PORT_PROPERTY_MISSING";
    public const string PortDirectionInvalid = "GRAPH_PORT_DIRECTION_INVALID";
    public const string NodePortSchemaMismatch = "GRAPH_NODE_PORT_SCHEMA_MISMATCH";
    public const string PortCapacityExceeded = "GRAPH_PORT_CAPACITY_EXCEEDED";
    public const string EdgePortMissing = "GRAPH_EDGE_PORT_MISSING";
    public const string EdgeDirectionInvalid = "GRAPH_EDGE_DIRECTION_INVALID";
    public const string EdgePortDisabled = "GRAPH_EDGE_PORT_DISABLED";
    public const string EdgeScopeMismatch = "GRAPH_EDGE_SCOPE_MISMATCH";
    public const string ConnectionPolicyViolation = "GRAPH_CONNECTION_POLICY_VIOLATION";
    public const string CycleNotAllowed = "GRAPH_CYCLE_NOT_ALLOWED";
    public const string CycleGuardRequired = "GRAPH_CYCLE_GUARD_REQUIRED";
    public const string CycleGuardInvalid = "GRAPH_CYCLE_GUARD_INVALID";
    public const string UnsupportedMetadata = "GRAPH_UNSUPPORTED_METADATA";
}

public static class GraphExecutionMetadata
{
    public const string LoopController = "ghostagram.loopController";
}

/// <summary>Validates diagram-specific contracts, then delegates graph semantics to snapshot algorithms.</summary>
public sealed class GraphCompiler(INodeTypeRegistry registry) : IGraphCompiler, IGraphSnapshotCompiler
{
    private const string LogicalNodeIdMetadata = "ghostagram.logicalNodeId";
    private const string DiagramNodeMetadata = "ghostagram.diagramNode";
    private const string DiagramPortsMetadata = "ghostagram.diagramPorts";
    private const string LoopControllerMetadata = GraphExecutionMetadata.LoopController;
    private const string ProjectedEdgeMetadata = "ghostagram.projectedEdge";
    private const string DiagramEdgeTypeMetadata = "ghostagram.edgeType";
    private static readonly SystemGraph.NodeKind DiagramNodeKind = SystemGraph.NodeKind.Define("Ghostagram.Execution", "DiagramNode");
    private static readonly SystemGraph.RelationshipKind DependencyKind = SystemGraph.RelationshipKind.Define("Ghostagram.Execution", "Dependency");
    private static readonly GraphProfile DagProfile = new([new DagFeature()]);

    [Obsolete("DiagramDocument compilation is a compatibility adapter. Compile a Ghostworx.System.Graph.GraphSnapshot through IGraphSnapshotCompiler instead.")]
    public GraphCompilationResult Compile(DiagramDocument document, GraphCompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        var projection = ProjectSnapshot(document);
        return !projection.Succeeded
            ? new(null, projection.Diagnostics)
            : CompileSnapshot(projection.Snapshot!, options, document.DocumentId);
    }

    public GraphCompilationResult Compile(SystemGraph.GraphSnapshot snapshot, GraphCompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return CompileSnapshot(snapshot, options, snapshot.GraphId.ToString());
    }

    /// <summary>
    /// Exposes the temporary diagram-to-snapshot adapter so migration callers can prove parity.
    /// Its removal criteria are the same as the DiagramDocument compile overload.
    /// </summary>
    [Obsolete("DiagramDocument projection exists only for persisted-document migration and compatibility parity.")]
    public GraphSnapshotProjectionResult ProjectSnapshot(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = ValidateAndProject(document, out var nodes, out var projectedEdges);
        if (diagnostics.Count > 0) return new(null, diagnostics);
        var nodeIds = nodes.Keys.ToDictionary(id => id, id => StableNodeId(id), StringComparer.Ordinal);
        var portsByNode = document.Ports.GroupBy(port => port.NodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<DiagramPort>)Array.AsReadOnly(group.OrderBy(port => port.Id, StringComparer.Ordinal).ToArray()), StringComparer.Ordinal);
        var snapshotNodes = nodes.Values.Select(node =>
        {
            var isLoopController = node.TypeId is not null && registry.TryGet(node.TypeId, node.TypeVersion, out var registration) && registration.Descriptor.IsLoopController;
            var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [LogicalNodeIdMetadata] = node.Id,
                [DiagramNodeMetadata] = CloneNode(node),
                [DiagramPortsMetadata] = portsByNode.GetValueOrDefault(node.Id) ?? Array.Empty<DiagramPort>(),
                [LoopControllerMetadata] = isLoopController
            };
            return new SystemGraph.GraphNodeSnapshot(nodeIds[node.Id], DiagramNodeKind, node.Label, metadata);
        }).ToArray();
        var edgeTypes = document.Edges.ToDictionary(edge => edge.Id, edge => edge.Type, StringComparer.Ordinal);
        var snapshotRelationships = projectedEdges.Select(edge => new SystemGraph.GraphRelationshipSnapshot(
            new SystemGraph.GraphEdge(StableEdgeId(edge.EdgeId), nodeIds[edge.SourceNodeId], nodeIds[edge.TargetNodeId], DependencyKind),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ProjectedEdgeMetadata] = edge,
                [DiagramEdgeTypeMetadata] = edgeTypes.GetValueOrDefault(edge.EdgeId)
            })).ToArray();
        return new(new SystemGraph.GraphSnapshot(StableNodeId($"document:{document.DocumentId}"), 0, snapshotNodes, snapshotRelationships), []);
    }

    private GraphCompilationResult CompileSnapshot(SystemGraph.GraphSnapshot snapshot, GraphCompileOptions options, string documentId)
    {
        var diagnostics = ValidateSnapshot(snapshot, out var nodes, out var logicalIds, out var projectedEdges);
        if (diagnostics.Count > 0) return new(null, diagnostics);

        // System Graph owns SCC discovery and DAG validity. Execution only consumes those
        // results to assign bounded-loop guards and deterministic execution stages.
        var components = GraphAlgorithms.StronglyConnectedComponents(snapshot);
        var cyclic = components.Where(component => IsCyclic(component, snapshot)).ToArray();
        if (options.Profile == GraphCompileProfile.DagOnly && !DagProfile.Validate(snapshot).IsValid)
        {
            diagnostics.AddRange(cyclic.Select(component => CycleComponentDiagnostic(
                GraphDiagnosticCodes.CycleNotAllowed,
                "DAG compilation does not allow a cycle involving",
                component,
                logicalIds,
                projectedEdges)));
            return new(null, diagnostics);
        }

        var guardByComponent = new Dictionary<string, LoopGuard>(StringComparer.Ordinal);
        if (options.Profile == GraphCompileProfile.BoundedCycles)
        {
            var requestedGuards = options.LoopGuards ?? InferPersistedLoopGuards(snapshot, logicalIds);
            var guards = requestedGuards.GroupBy(guard => guard.NodeId, StringComparer.Ordinal).ToArray();
            foreach (var group in guards.Where(group => group.Count() > 1 || group.Any(guard => guard.MaxIterations < 1)))
                diagnostics.Add(new(GraphDiagnosticCodes.CycleGuardInvalid,
                    $"Loop guard for node '{group.Key}' must be unique and have MaxIterations greater than zero.", [group.Key]));

            var registeredControllers = snapshot.Nodes
                .Where(node => node.Metadata.TryGetValue(LoopControllerMetadata, out var value) && value is true)
                .Select(node => logicalIds[node.Id])
                .ToHashSet(StringComparer.Ordinal);
            foreach (var group in guards.Where(group => !registeredControllers.Contains(group.Key)))
                diagnostics.Add(new(GraphDiagnosticCodes.CycleGuardInvalid,
                    $"Loop guard node '{group.Key}' is not a registered loop-controller node.", [group.Key]));

            foreach (var component in cyclic)
            {
                var logicalComponent = component.Select(nodeId => logicalIds[nodeId]).ToHashSet(StringComparer.Ordinal);
                var matches = guards
                    .Where(group => registeredControllers.Contains(group.Key) && logicalComponent.Contains(group.Key) && group.Count() == 1 && group.Single().MaxIterations > 0)
                    .Select(group => group.Single())
                    .ToArray();
                if (matches.Length != 1)
                {
                    diagnostics.Add(CycleComponentDiagnostic(
                        GraphDiagnosticCodes.CycleGuardRequired,
                        "Bounded-cycle compilation requires exactly one positive loop guard for the cycle involving",
                        component,
                        logicalIds,
                        projectedEdges));
                }
                else
                {
                    guardByComponent[ComponentKey(component)] = matches[0];
                }
            }
            if (diagnostics.Count > 0) return new(null, diagnostics);
        }

        var stages = BuildStages(snapshot, components, logicalIds, guardByComponent);
        var graph = new CompiledGraph(
            documentId,
            options.Profile,
            Fingerprint(snapshot, options.Profile, stages, logicalIds),
            Array.AsReadOnly(nodes.Values.OrderBy(node => logicalIds[node.Id], StringComparer.Ordinal).Select(node => SnapshotDiagramNode(node, logicalIds[node.Id])).ToArray()),
            Array.AsReadOnly(projectedEdges.ToArray()),
            Array.AsReadOnly(stages.Select(stage => new ExecutionStage(
                stage.Order,
                Array.AsReadOnly(stage.NodeIds.ToArray()),
                Array.AsReadOnly((stage.LoopGuards ?? []).ToArray()))).ToArray()));
        return new(graph, []);
    }

    private static List<GraphDiagnostic> ValidateSnapshot(
        SystemGraph.GraphSnapshot snapshot,
        out IReadOnlyDictionary<SystemGraph.NodeId, SystemGraph.GraphNodeSnapshot> nodes,
        out IReadOnlyDictionary<SystemGraph.NodeId, string> logicalIds,
        out IReadOnlyList<ProjectedEdge> projectedEdges)
    {
        var diagnostics = new List<GraphDiagnostic>();
        var nodeGroups = snapshot.Nodes.GroupBy(node => node.Id).ToArray();
        foreach (var duplicate in nodeGroups.Where(group => group.Count() > 1))
            diagnostics.Add(new(GraphDiagnosticCodes.DuplicateNode, $"Node id '{duplicate.Key}' is duplicated.", [duplicate.Key.ToString()]));
        var nodeMap = nodeGroups.ToDictionary(group => group.Key, group => group.First());
        nodes = nodeMap;
        var logical = nodeMap.Values.ToDictionary(node => node.Id, LogicalNodeId);
        logicalIds = logical;
        foreach (var duplicate in logical.GroupBy(pair => pair.Value, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(new(GraphDiagnosticCodes.DuplicateNode, $"Node id '{duplicate.Key}' is duplicated.", [duplicate.Key]));
        foreach (var node in nodeMap.Values)
            foreach (var metadata in node.Metadata.Where(pair => pair.Key is not (LogicalNodeIdMetadata or DiagramNodeMetadata or DiagramPortsMetadata or LoopControllerMetadata)))
                if (!IsFingerprintMetadataSupported(metadata.Value))
                    diagnostics.Add(new(GraphDiagnosticCodes.UnsupportedMetadata,
                        $"Node '{logical[node.Id]}' metadata '{metadata.Key}' uses unsupported CLR type '{metadata.Value?.GetType().FullName ?? "unknown"}'.",
                        [logical[node.Id]]));

        var relationshipGroups = snapshot.Relationships.GroupBy(item => item.Relationship.Id).ToArray();
        foreach (var duplicate in relationshipGroups.Where(group => group.Count() > 1))
            diagnostics.Add(new(GraphDiagnosticCodes.DuplicateEdge, $"Edge id '{duplicate.Key}' is duplicated.", [], [duplicate.Key.ToString()]));
        var projection = new List<ProjectedEdge>();
        foreach (var relationship in relationshipGroups.Select(group => group.First()).OrderBy(item => LogicalEdgeId(item), StringComparer.Ordinal))
        {
            foreach (var metadata in relationship.Metadata.Where(pair => pair.Key is not (ProjectedEdgeMetadata or DiagramEdgeTypeMetadata)))
                if (!IsFingerprintMetadataSupported(metadata.Value))
                    diagnostics.Add(new(GraphDiagnosticCodes.UnsupportedMetadata,
                        $"Relationship '{LogicalEdgeId(relationship)}' metadata '{metadata.Key}' uses unsupported CLR type '{metadata.Value?.GetType().FullName ?? "unknown"}'.",
                        [], [LogicalEdgeId(relationship)]));
            if (!nodeMap.ContainsKey(relationship.Relationship.Source) || !nodeMap.ContainsKey(relationship.Relationship.Target))
            {
                diagnostics.Add(new(GraphDiagnosticCodes.EdgePortMissing,
                    $"Graph relationship '{LogicalEdgeId(relationship)}' references a missing node.", [], [LogicalEdgeId(relationship)]));
                continue;
            }
            projection.Add(relationship.Metadata.TryGetValue(ProjectedEdgeMetadata, out var value) && value is ProjectedEdge projected
                ? projected
                : new(
                    LogicalEdgeId(relationship),
                    logical[relationship.Relationship.Source],
                    logical[relationship.Relationship.Target],
                    $"{logical[relationship.Relationship.Source]}:out",
                    $"{logical[relationship.Relationship.Target]}:in"));
        }
        projectedEdges = projection;
        return diagnostics;
    }

    private static string LogicalNodeId(SystemGraph.GraphNodeSnapshot node) =>
        node.Metadata.TryGetValue(LogicalNodeIdMetadata, out var value) && value is string logicalId && !string.IsNullOrWhiteSpace(logicalId)
            ? logicalId
            : node.Id.ToString();

    private static string LogicalEdgeId(SystemGraph.GraphRelationshipSnapshot relationship) =>
        relationship.Metadata.TryGetValue(ProjectedEdgeMetadata, out var value) && value is ProjectedEdge projected
            ? projected.EdgeId
            : relationship.Relationship.Id.ToString();

    private static DiagramNode SnapshotDiagramNode(SystemGraph.GraphNodeSnapshot node, string logicalId) =>
        node.Metadata.TryGetValue(DiagramNodeMetadata, out var value) && value is DiagramNode diagramNode
            ? CloneNode(diagramNode)
            : new DiagramNode(logicalId, 0, 0, Label: node.NodeName);

    private static SystemGraph.NodeId StableNodeId(string value) => new(StableGuid($"node:{value}"));
    private static SystemGraph.EdgeId StableEdgeId(string value) => new(StableGuid($"edge:{value}"));
    private static Guid StableGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private List<GraphDiagnostic> ValidateAndProject(
        DiagramDocument document,
        out IReadOnlyDictionary<string, DiagramNode> nodes,
        out IReadOnlyList<ProjectedEdge> projectedEdges)
    {
        var diagnostics = new List<GraphDiagnostic>();
        var nodeGroups = document.Nodes.GroupBy(node => node.Id, StringComparer.Ordinal).ToArray();
        foreach (var duplicate in nodeGroups.Where(group => group.Count() > 1))
            diagnostics.Add(new(GraphDiagnosticCodes.DuplicateNode, $"Node id '{duplicate.Key}' is duplicated.", [duplicate.Key]));
        var nodeMap = nodeGroups.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        nodes = nodeMap;
        foreach (var node in nodeMap.Values.OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            var duplicate = node.Properties.GroupBy(property => property.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
                diagnostics.Add(new(GraphDiagnosticCodes.DuplicateProperty, $"Node '{node.Id}' has duplicate property id '{duplicate.Key}'.", [node.Id]));

            NodeTypeDescriptor? descriptor = null;
            if (node.TypeId is not null)
            {
                if (registry.TryGet(node.TypeId, node.TypeVersion, out var registration)) descriptor = registration.Descriptor;
                else diagnostics.Add(new(GraphDiagnosticCodes.UnknownNodeType, $"Node '{node.Id}' references unregistered type '{node.TypeId}@{node.TypeVersion}'.", [node.Id]));
            }

            if (descriptor is not null)
            {
                foreach (var property in node.Properties.Where(property => descriptor.Properties.All(definition => definition.Id != property.Id)).OrderBy(property => property.Id, StringComparer.Ordinal))
                    diagnostics.Add(new(GraphDiagnosticCodes.NodeSchemaMismatch, $"Property '{property.Id}' on node '{node.Id}' is not declared by type '{descriptor.Key}'.", [node.Id]));
                foreach (var definition in descriptor.Properties)
                {
                    var property = node.Properties.FirstOrDefault(candidate => candidate.Id == definition.Id);
                    if (definition.Required && (property is null || PropertyValueRules.IsMissing(property.Value)))
                    {
                        diagnostics.Add(new(GraphDiagnosticCodes.RequiredPropertyMissing, $"Required property '{definition.Id}' on node '{node.Id}' has no value.", [node.Id]));
                        continue;
                    }
                    if (property is null) continue;
                    if (!string.Equals(property.Type, definition.Type, StringComparison.Ordinal))
                    {
                        diagnostics.Add(new(GraphDiagnosticCodes.NodeSchemaMismatch, $"Property '{property.Id}' on node '{node.Id}' declares type '{property.Type}' but registry type '{descriptor.Key}' requires '{definition.Type}'.", [node.Id]));
                        continue;
                    }
                    if (!PropertyValueRules.IsCompatible(definition.Type, property.Value, definition.Options, out var reason))
                        diagnostics.Add(new(GraphDiagnosticCodes.PropertyValueInvalid, $"Property '{property.Id}' on node '{node.Id}' is invalid. {reason}", [node.Id]));
                }
            }
            else
            {
                foreach (var property in node.Properties.OrderBy(property => property.Id, StringComparer.Ordinal))
                {
                    if (property.Required && PropertyValueRules.IsMissing(property.Value))
                    {
                        diagnostics.Add(new(GraphDiagnosticCodes.RequiredPropertyMissing, $"Required property '{property.Id}' on node '{node.Id}' has no value.", [node.Id]));
                        continue;
                    }
                    if (!PropertyValueRules.IsCompatible(property.Type, property.Value, property.Options, out var reason))
                        diagnostics.Add(new(GraphDiagnosticCodes.PropertyValueInvalid, $"Property '{property.Id}' on node '{node.Id}' is invalid. {reason}", [node.Id]));
                }
            }
        }

        var portGroups = document.Ports.GroupBy(port => port.Id, StringComparer.Ordinal).ToArray();
        foreach (var duplicate in portGroups.Where(group => group.Count() > 1))
            diagnostics.Add(new(GraphDiagnosticCodes.DuplicatePort, $"Port id '{duplicate.Key}' is duplicated.", []));
        var ports = portGroups.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var port in ports.Values.Where(port => port.Direction is not ("source" or "target" or "both")).OrderBy(port => port.Id, StringComparer.Ordinal))
            diagnostics.Add(new(GraphDiagnosticCodes.PortDirectionInvalid, $"Port '{port.Id}' has invalid direction '{port.Direction}'.", [port.NodeId]));
        foreach (var port in ports.Values.Where(port => !nodeMap.ContainsKey(port.NodeId)).OrderBy(port => port.Id, StringComparer.Ordinal))
            diagnostics.Add(new(GraphDiagnosticCodes.PortNodeMissing, $"Port '{port.Id}' references missing node '{port.NodeId}'.", [port.NodeId]));
        foreach (var port in ports.Values.Where(port => port.PropertyId is not null && nodeMap.TryGetValue(port.NodeId, out var node) && node.Properties.All(property => property.Id != port.PropertyId)).OrderBy(port => port.Id, StringComparer.Ordinal))
            diagnostics.Add(new(GraphDiagnosticCodes.PortPropertyMissing, $"Port '{port.Id}' references missing property '{port.PropertyId}' on node '{port.NodeId}'.", [port.NodeId]));

        foreach (var node in nodeMap.Values.Where(node => node.TypeId is not null).OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            if (!registry.TryGet(node.TypeId!, node.TypeVersion, out var registration)) continue;
            var expected = registration.Descriptor.Ports.ToDictionary(definition => $"{node.Id}:{definition.Id}", StringComparer.Ordinal);
            var actual = ports.Values.Where(port => port.NodeId == node.Id).ToDictionary(port => port.Id, StringComparer.Ordinal);
            foreach (var definition in expected)
            {
                if (!actual.TryGetValue(definition.Key, out var port))
                {
                    diagnostics.Add(new(GraphDiagnosticCodes.NodePortSchemaMismatch, $"Node '{node.Id}' is missing registered port '{definition.Key}'.", [node.Id]));
                    continue;
                }
                var schema = definition.Value;
                if (port.Direction != schema.Direction || port.Scope != schema.Scope || port.PropertyId != schema.PropertyId ||
                    port.Label != schema.Label || port.Order != schema.Order || port.MaxConnections != schema.MaxConnections ||
                    !AnchorMatches(port.Anchor, schema.Anchor) || !port.Enabled)
                    diagnostics.Add(new(GraphDiagnosticCodes.NodePortSchemaMismatch, $"Port '{port.Id}' does not match registered schema for node type '{registration.Descriptor.Key}'.", [node.Id]));
            }
            foreach (var extra in actual.Keys.Except(expected.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                diagnostics.Add(new(GraphDiagnosticCodes.NodePortSchemaMismatch, $"Node '{node.Id}' has unregistered port '{extra}'.", [node.Id]));
        }

        var edgeGroups = document.Edges.GroupBy(edge => edge.Id, StringComparer.Ordinal).ToArray();
        foreach (var duplicate in edgeGroups.Where(group => group.Count() > 1))
            diagnostics.Add(new(GraphDiagnosticCodes.DuplicateEdge, $"Edge id '{duplicate.Key}' is duplicated.", [], [duplicate.Key]));
        var connectionCounts = document.Edges
            .SelectMany(edge => new[] { edge.SourcePortId, edge.TargetPortId }.Distinct(StringComparer.Ordinal))
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var port in ports.Values.Where(port => port.MaxConnections >= 0 && connectionCounts.GetValueOrDefault(port.Id) > port.MaxConnections).OrderBy(port => port.Id, StringComparer.Ordinal))
            diagnostics.Add(new(GraphDiagnosticCodes.PortCapacityExceeded, $"Port '{port.Id}' exceeds MaxConnections '{port.MaxConnections}'.", [port.NodeId]));
        var projection = new List<ProjectedEdge>();
        foreach (var edge in edgeGroups.Select(group => group.First()).OrderBy(edge => edge.Id, StringComparer.Ordinal))
        {
            if (!ports.TryGetValue(edge.SourcePortId, out var source) || !ports.TryGetValue(edge.TargetPortId, out var target))
            {
                var missing = !ports.ContainsKey(edge.SourcePortId) ? edge.SourcePortId : edge.TargetPortId;
                diagnostics.Add(new(GraphDiagnosticCodes.EdgePortMissing, $"Edge '{edge.Id}' references missing port '{missing}'.", [], [edge.Id]));
                continue;
            }
            if (!source.Enabled || !target.Enabled)
            {
                diagnostics.Add(new(GraphDiagnosticCodes.EdgePortDisabled, $"Edge '{edge.Id}' references a disabled port.", [source.NodeId, target.NodeId], [edge.Id]));
                continue;
            }
            if (source.Direction is not ("source" or "both") || target.Direction is not ("target" or "both"))
            {
                diagnostics.Add(new(GraphDiagnosticCodes.EdgeDirectionInvalid, $"Edge '{edge.Id}' must connect a source-capable port to a target-capable port.", [source.NodeId, target.NodeId], [edge.Id]));
                continue;
            }
            if (!ScopesCompatible(source.Scope, target.Scope))
            {
                diagnostics.Add(new(GraphDiagnosticCodes.EdgeScopeMismatch, $"Edge '{edge.Id}' connects incompatible scopes '{source.Scope}' and '{target.Scope}'.", [source.NodeId, target.NodeId], [edge.Id]));
                continue;
            }
            if (!Allows(source.ConnectionPolicy, target) || !Allows(target.ConnectionPolicy, source))
            {
                diagnostics.Add(new(GraphDiagnosticCodes.ConnectionPolicyViolation, $"Edge '{edge.Id}' violates a port connection policy.", [source.NodeId, target.NodeId], [edge.Id]));
                continue;
            }
            if (nodeMap.ContainsKey(source.NodeId) && nodeMap.ContainsKey(target.NodeId))
                projection.Add(new(edge.Id, source.NodeId, target.NodeId, source.Id, target.Id));
        }
        projectedEdges = projection;
        return diagnostics;
    }

    private static bool AnchorMatches(object? actual, string? expected) => actual switch
    {
        null => expected is null,
        string value => string.Equals(value, expected, StringComparison.Ordinal),
        JsonElement { ValueKind: JsonValueKind.String } value => string.Equals(value.GetString(), expected, StringComparison.Ordinal),
        _ => false
    };

    private static bool ScopesCompatible(string source, string target) => source == "*" || target == "*" || string.Equals(source, target, StringComparison.Ordinal);

    private static bool Allows(DiagramConnectionPolicy? policy, DiagramPort counterpart)
    {
        if (policy is null) return true;
        if (policy.DenyPortIds?.Contains(counterpart.Id, StringComparer.Ordinal) == true || policy.DenyNodeIds?.Contains(counterpart.NodeId, StringComparer.Ordinal) == true) return false;
        if (policy.AllowPortIds is { } allowedPorts && !allowedPorts.Contains(counterpart.Id, StringComparer.Ordinal)) return false;
        if (policy.AllowNodeIds is { } allowedNodes && !allowedNodes.Contains(counterpart.NodeId, StringComparer.Ordinal)) return false;
        return true;
    }

    // Execution-specific classification of a System-produced SCC: only a multi-node
    // component or a singleton with a self-loop requires a bounded execution guard.
    private static bool IsCyclic(IReadOnlyList<SystemGraph.NodeId> component, SystemGraph.GraphSnapshot snapshot) =>
        component.Count > 1 || snapshot.Relationships.Any(relationship =>
            relationship.Relationship.Source == component[0] && relationship.Relationship.Target == component[0]);

    private static GraphDiagnostic CycleComponentDiagnostic(
        string code,
        string prefix,
        IReadOnlyList<SystemGraph.NodeId> component,
        IReadOnlyDictionary<SystemGraph.NodeId, string> logicalIds,
        IReadOnlyList<ProjectedEdge> edges)
    {
        var nodeIds = component.Select(nodeId => logicalIds[nodeId]).Order(StringComparer.Ordinal).ToArray();
        var members = nodeIds.ToHashSet(StringComparer.Ordinal);
        var edgeIds = edges
            .Where(edge => members.Contains(edge.SourceNodeId) && members.Contains(edge.TargetNodeId))
            .Select(edge => edge.EdgeId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new(code, $"{prefix}: {string.Join(", ", nodeIds)}.", nodeIds, edgeIds);
    }

    private static IReadOnlyList<ExecutionStage> BuildStages(
        SystemGraph.GraphSnapshot snapshot,
        IReadOnlyList<IReadOnlyList<SystemGraph.NodeId>> components,
        IReadOnlyDictionary<SystemGraph.NodeId, string> logicalIds,
        IReadOnlyDictionary<string, LoopGuard> guards)
    {
        var componentByNode = components.SelectMany(component => component.Select(nodeId => (nodeId, component)))
            .ToDictionary(item => item.nodeId, item => item.component);
        var representativeByKey = components.ToDictionary(ComponentKey, component => component[0], StringComparer.Ordinal);
        var keyByRepresentative = representativeByKey.ToDictionary(pair => pair.Value, pair => pair.Key);
        var condensationNodes = representativeByKey.Values.Select(nodeId => new SystemGraph.GraphNodeSnapshot(
            nodeId, SystemGraph.NodeKind.Graph, null, new Dictionary<string, object?>())).ToArray();
        var condensationRelationships = snapshot.Relationships
            .Select(relationship => (
                Relationship: relationship,
                Source: componentByNode[relationship.Relationship.Source][0],
                Target: componentByNode[relationship.Relationship.Target][0]))
            .Where(item => item.Source != item.Target)
            .GroupBy(item => (item.Source, item.Target))
            .Select(group => new SystemGraph.GraphRelationshipSnapshot(
                new SystemGraph.GraphEdge(group.First().Relationship.Relationship.Id, group.Key.Source, group.Key.Target, DependencyKind),
                new Dictionary<string, object?>()))
            .ToArray();
        var condensation = new SystemGraph.GraphSnapshot(snapshot.GraphId, snapshot.Version, condensationNodes, condensationRelationships);
        var topological = GraphAlgorithms.TopologicalSort(condensation);
        if (!topological.Succeeded) throw new InvalidOperationException("The component projection must be acyclic.");
        var levels = condensationNodes.ToDictionary(node => node.Id, _ => 0);
        var incoming = condensationRelationships.GroupBy(item => item.Relationship.Target)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var representative in topological.Order)
            if (incoming.TryGetValue(representative, out var dependencies))
                levels[representative] = dependencies.Max(item => levels[item.Relationship.Source] + 1);

        return levels.GroupBy(pair => pair.Value)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var keys = group.Select(pair => keyByRepresentative[pair.Key]).Order(StringComparer.Ordinal).ToArray();
                var stageNodes = keys.SelectMany(key => components.Single(component => ComponentKey(component) == key))
                    .Select(nodeId => logicalIds[nodeId]).Order(StringComparer.Ordinal).ToArray();
                var stageGuards = keys.Where(guards.ContainsKey).Select(key => guards[key]).OrderBy(guard => guard.NodeId, StringComparer.Ordinal).ToArray();
                return new ExecutionStage(group.Key, stageNodes, stageGuards);
            })
            .ToArray();
    }

    private static string ComponentKey(IEnumerable<SystemGraph.NodeId> component) =>
        string.Join("\u001f", component.Select(nodeId => nodeId.ToString()).Order(StringComparer.Ordinal));

    private static IReadOnlyList<LoopGuard> InferPersistedLoopGuards(
        SystemGraph.GraphSnapshot snapshot,
        IReadOnlyDictionary<SystemGraph.NodeId, string> logicalIds) =>
        snapshot.Nodes
            .Where(node => node.Metadata.TryGetValue(LoopControllerMetadata, out var controller) && controller is true)
            .Select(node => (node, diagramNode: node.Metadata.GetValueOrDefault(DiagramNodeMetadata) as DiagramNode))
            .Where(item => item.diagramNode is not null)
            .Select(item => (item.node, property: item.diagramNode!.Properties.FirstOrDefault(property => property.Id == "maxIterations")))
            .Where(item => item.property?.Value is { ValueKind: System.Text.Json.JsonValueKind.Number } value && value.TryGetInt32(out var maximum) && maximum > 0)
            .Select(item => new LoopGuard(logicalIds[item.node.Id], item.property!.Value!.Value.GetInt32()))
            .ToArray();

    private static string Fingerprint(
        SystemGraph.GraphSnapshot snapshot,
        GraphCompileProfile profile,
        IEnumerable<ExecutionStage> stages,
        IReadOnlyDictionary<SystemGraph.NodeId, string> logicalIds)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("graphId", snapshot.GraphId.ToString());
            writer.WriteString("profile", profile.ToString());
            writer.WriteStartArray("nodes");
            foreach (var snapshotNode in snapshot.Nodes.OrderBy(node => logicalIds[node.Id], StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", logicalIds[snapshotNode.Id]);
                writer.WriteString("kind", snapshotNode.Kind.QualifiedName);
                writer.WriteString("name", snapshotNode.NodeName);
                if (snapshotNode.Metadata.TryGetValue(DiagramNodeMetadata, out var diagramValue) && diagramValue is DiagramNode node)
                {
                    writer.WriteString("typeId", node.TypeId);
                    writer.WriteNumber("typeVersion", node.TypeVersion);
                    writer.WriteStartArray("properties");
                    foreach (var property in node.Properties.OrderBy(property => property.Id, StringComparer.Ordinal))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", property.Id);
                        writer.WriteString("name", property.Name);
                        writer.WriteString("type", property.Type);
                        writer.WriteString("mode", property.Mode);
                        writer.WriteBoolean("required", property.Required);
                        writer.WritePropertyName("value");
                        if (property.Value is { } value) WriteCanonical(writer, value); else writer.WriteNullValue();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                if (snapshotNode.Metadata.TryGetValue(DiagramPortsMetadata, out var portsValue) && portsValue is IEnumerable<DiagramPort> ports)
                {
                    writer.WriteStartArray("ports");
                    foreach (var port in ports.OrderBy(port => port.Id, StringComparer.Ordinal))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", port.Id);
                        writer.WriteString("direction", port.Direction);
                        writer.WriteString("scope", port.Scope);
                        writer.WriteNumber("maxConnections", port.MaxConnections);
                        writer.WriteBoolean("enabled", port.Enabled);
                        writer.WriteString("propertyId", port.PropertyId);
                        writer.WriteNumber("order", port.Order);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteStartObject("metadata");
                foreach (var metadata in snapshotNode.Metadata
                             .Where(pair => pair.Key is not (LogicalNodeIdMetadata or DiagramNodeMetadata or DiagramPortsMetadata or LoopControllerMetadata))
                             .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(metadata.Key);
                    WriteMetadataValue(writer, metadata.Value);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("relationships");
            foreach (var relationship in snapshot.Relationships.OrderBy(LogicalEdgeId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", LogicalEdgeId(relationship));
                writer.WriteString("sourceNode", logicalIds[relationship.Relationship.Source]);
                writer.WriteString("targetNode", logicalIds[relationship.Relationship.Target]);
                writer.WriteString("kind", relationship.Relationship.Kind.QualifiedName);
                if (relationship.Metadata.TryGetValue(ProjectedEdgeMetadata, out var projectedValue) && projectedValue is ProjectedEdge edge)
                {
                    writer.WriteString("sourcePort", edge.SourcePortId);
                    writer.WriteString("targetPort", edge.TargetPortId);
                }
                if (relationship.Metadata.TryGetValue(DiagramEdgeTypeMetadata, out var typeValue) && typeValue is string type)
                    writer.WriteString("type", type);
                writer.WriteStartObject("metadata");
                foreach (var metadata in relationship.Metadata
                             .Where(pair => pair.Key is not (ProjectedEdgeMetadata or DiagramEdgeTypeMetadata))
                             .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(metadata.Key);
                    WriteMetadataValue(writer, metadata.Value);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("stages");
            foreach (var stage in stages)
            {
                writer.WriteStartObject();
                writer.WriteNumber("order", stage.Order);
                writer.WriteStartArray("nodes");
                foreach (var nodeId in stage.NodeIds) writer.WriteStringValue(nodeId);
                writer.WriteEndArray();
                writer.WriteStartArray("guards");
                foreach (var guard in stage.LoopGuards ?? [])
                {
                    writer.WriteStartObject();
                    writer.WriteString("nodeId", guard.NodeId);
                    writer.WriteNumber("maxIterations", guard.MaxIterations);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteMetadataValue(System.Text.Json.Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement json:
                WriteCanonical(writer, json);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case double number when double.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case Guid guid:
                writer.WriteStringValue(guid);
                break;
            case DateTimeOffset date:
                writer.WriteStringValue(date);
                break;
            default:
                throw new InvalidOperationException($"Unsupported fingerprint metadata type '{value.GetType().FullName}'.");
        }
    }

    private static bool IsFingerprintMetadataSupported(object? value) => value switch
    {
        null or string or bool or int or long or decimal or Guid or DateTimeOffset => true,
        double number => double.IsFinite(number),
        JsonElement json => json.ValueKind != JsonValueKind.Undefined,
        _ => false
    };

    private static void WriteCanonical(System.Text.Json.Utf8JsonWriter writer, System.Text.Json.JsonElement value)
    {
        switch (value.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case System.Text.Json.JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static DiagramNode CloneNode(DiagramNode node) => node with
    {
        Properties = Array.AsReadOnly(node.Properties.Select(property => property with
        {
            Value = property.Value?.Clone(),
            Metadata = property.Metadata?.Clone(),
            Options = property.Options is null ? null : Array.AsReadOnly(property.Options.ToArray()),
            ExtensionData = property.ExtensionData?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)
        }).ToArray()),
        ExtensionData = node.ExtensionData?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)
    };
}
