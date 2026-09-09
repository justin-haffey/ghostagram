using LocalGraph = Ghostworx.System.Graph.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ghostagram.Core;
using Ghostworx.System.Graph.Runtime.Algorithms;
using Ghostworx.System.Graph.Runtime.Constraints;
using Ghostworx.System.Graph.Runtime.Validation;
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
    [Obsolete("DiagramDocument compilation is a compatibility adapter. Retain ProjectSnapshot(document).Input and compile that GraphCompilationInput for lossless diagram migration.")]
    GraphCompilationResult Compile(DiagramDocument document, GraphCompileOptions options);
}

public interface IGraphSnapshotCompiler
{
    GraphCompilationResult Compile(SystemGraph.GraphSnapshot snapshot, GraphCompileOptions options);
    GraphCompilationResult Compile(LocalGraph.GraphLocalSnapshot snapshot, GraphCompileOptions options);
    GraphCompilationResult Compile(GraphCompilationInput input, GraphCompileOptions options);
}

/// <summary>A local structural snapshot and its immutable, Ghostagram-owned compilation context.</summary>
/// <remarks>Retain this explicit handle when a diagram projection must preserve properties,
/// port endpoints and inferred guards. Snapshot-only compilation consumes semantic facts only.</remarks>
public sealed class GraphCompilationInput
{
    private readonly Func<GraphCompileOptions, LocalGraph.GraphLocalLimits, GraphCompilationResult> _compile;

    internal GraphCompilationInput(LocalGraph.GraphLocalSnapshot snapshot, Func<GraphCompileOptions, LocalGraph.GraphLocalLimits, GraphCompilationResult> compile)
    {
        Snapshot = snapshot;
        _compile = compile;
    }

    public LocalGraph.GraphLocalSnapshot Snapshot { get; }
    internal GraphCompilationResult Compile(GraphCompileOptions options, LocalGraph.GraphLocalLimits limits) => _compile(options, limits);
}

public sealed record GraphSnapshotProjectionResult(
    LocalGraph.GraphLocalSnapshot? Snapshot,
    IReadOnlyList<GraphDiagnostic> Diagnostics)
{
    public bool Succeeded => Snapshot is not null && Diagnostics.Count == 0;
    public GraphCompilationInput? Input { get; init; }
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
    public const string AdmissionRejected = "GRAPH_ADMISSION_REJECTED";
}

/// <summary>Finite local diagram policy; it is not a negotiated semantic profile.</summary>
public static class GraphCompilationLimits
{
    public static LocalGraph.GraphLocalLimits General { get; } = new()
    {
        MaximumInputBytes = 4 * 1024 * 1024,
        MaximumNodes = 100_000,
        MaximumRelationships = 250_000,
        MaximumChangeBatches = 100_000,
        MaximumMetadataEntries = 1_024,
        MaximumExtensions = 1_024
    };
}

public static class GraphExecutionMetadata
{
    public const string LoopController = "ghostagram.loopController";
}

/// <summary>Validates diagram-specific contracts, then delegates graph semantics to snapshot algorithms.</summary>
public sealed class GraphCompiler(INodeTypeRegistry registry, LocalGraph.GraphLocalLimits? projectionLimits = null) : IGraphCompiler, IGraphSnapshotCompiler
{
    private readonly LocalGraph.GraphLocalLimits _projectionLimits = projectionLimits ?? GraphCompilationLimits.General;
    private const string LogicalNodeIdMetadata = "ghostagram.logicalNodeId";
    private const string LoopControllerMetadata = GraphExecutionMetadata.LoopController;
    private const string LogicalEdgeIdMetadata = "ghostagram.logicalEdgeId";
    private const string DiagramEdgeTypeMetadata = "ghostagram.edgeType";
    private static readonly SystemGraph.NodeKind DiagramNodeKind = SystemGraph.NodeKind.Define("Ghostagram.Execution", "DiagramNode");
    private static readonly SystemGraph.RelationshipKind DependencyKind = SystemGraph.RelationshipKind.Define("Ghostagram.Execution", "Dependency");
    private static readonly GraphProfile DagProfile = new([new DagConstraint()]);

    private sealed record CompilationPort(string Id, string Direction, string Scope,
        int MaxConnections, bool Enabled, string? PropertyId, int Order);

    // Presentation data belongs to this compilation, never to a System semantic value.
    private sealed record CompilationContext(
        IReadOnlyDictionary<SystemGraph.NodeId, DiagramNode> Nodes,
        IReadOnlyDictionary<SystemGraph.NodeId, IReadOnlyList<CompilationPort>> Ports,
        IReadOnlyDictionary<SystemGraph.EdgeId, ProjectedEdge> Edges);


    [Obsolete("DiagramDocument compilation is a compatibility adapter. Retain ProjectSnapshot(document).Input and compile that GraphCompilationInput for lossless diagram migration.")]
    public GraphCompilationResult Compile(DiagramDocument document, GraphCompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        var projection = ProjectSnapshot(document);
        return !projection.Succeeded
            ? new(null, projection.Diagnostics)
            : Compile(projection.Input!, options);
    }

    public GraphCompilationResult Compile(SystemGraph.GraphSnapshot snapshot, GraphCompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            return Compile(LocalGraph.GraphLocalInspection.FromSnapshot(snapshot, _projectionLimits), options);
        }
        catch (ArgumentException exception)
        {
            return new(null, [new(GraphDiagnosticCodes.AdmissionRejected, exception.Message, [])]);
        }
    }

    public GraphCompilationResult Compile(LocalGraph.GraphLocalSnapshot snapshot, GraphCompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return CompileSnapshot(snapshot, options, snapshot.GraphId.ToString(), limits: _projectionLimits);
    }

    public GraphCompilationResult Compile(GraphCompilationInput input, GraphCompileOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Compile(options, _projectionLimits);
    }

    /// <summary>
    /// Exposes the temporary diagram projection so migration callers can prove parity.
    /// Compile the returned Input to preserve diagram properties, port endpoints and inferred guards.
    /// Compile Snapshot alone only when semantic-only compilation is intended.
    /// Its removal criteria are the same as the DiagramDocument compile overload.
    /// </summary>
    [Obsolete("DiagramDocument projection exists only for persisted-document migration and compatibility parity.")]
    public GraphSnapshotProjectionResult ProjectSnapshot(DiagramDocument document)
        => ProjectSnapshot(document, out _);

    private GraphSnapshotProjectionResult ProjectSnapshot(DiagramDocument document, out CompilationContext? context)
    {
        ArgumentNullException.ThrowIfNull(document);
        context = null;
        var limits = _projectionLimits;
        if (new[] { limits.MaximumInputBytes, limits.MaximumNodes, limits.MaximumRelationships,
                limits.MaximumChangeBatches, limits.MaximumMetadataEntries, limits.MaximumExtensions }
            .Any(value => value <= 0 || value == int.MaxValue))
            return new(null, [new(GraphDiagnosticCodes.AdmissionRejected, "Local structural limits must be finite and positive.", [])]);
        if (document.Nodes.Count > limits.MaximumNodes || document.Edges.Count > limits.MaximumRelationships)
            return new(null, [new(GraphDiagnosticCodes.AdmissionRejected, "Diagram exceeds the configured local structural capacity.", [])]);
        var diagnostics = ValidateAndProject(document, out var nodes, out var projectedEdges);
        if (diagnostics.Count > 0) return new(null, diagnostics);
        var nodeIds = nodes.Keys.ToDictionary(id => id, id => StableNodeId(id), StringComparer.Ordinal);
        var portsByNode = document.Ports.GroupBy(port => port.NodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CompilationPort>)Array.AsReadOnly(group.OrderBy(port => port.Id, StringComparer.Ordinal)
                .Select(port => new CompilationPort(port.Id, port.Direction, port.Scope, port.MaxConnections, port.Enabled, port.PropertyId, port.Order)).ToArray()), StringComparer.Ordinal);
        var snapshotNodes = nodes.Values.Select(node =>
        {
            var isLoopController = node.TypeId is not null && registry.TryGet(node.TypeId, node.TypeVersion, out var registration) && registration.Descriptor.IsLoopController;
            var metadata = new Dictionary<string, SystemGraph.GraphSemanticValue>(StringComparer.Ordinal)
            {
                [LogicalNodeIdMetadata] = SystemGraph.GraphSemanticValue.String(node.Id),
                [LoopControllerMetadata] = SystemGraph.GraphSemanticValue.Boolean(isLoopController)
            };
            return new LocalGraph.GraphLocalNodeSnapshot(nodeIds[node.Id], DiagramNodeKind, node.Label, metadata);
        }).ToArray();
        var edgeTypes = document.Edges.ToDictionary(edge => edge.Id, edge => edge.Type, StringComparer.Ordinal);
        var snapshotRelationships = projectedEdges.Select(edge => new LocalGraph.GraphLocalRelationshipSnapshot(
            new LocalGraph.GraphLocalRelationship(StableEdgeId(edge.EdgeId), nodeIds[edge.SourceNodeId], nodeIds[edge.TargetNodeId], DependencyKind),
            new Dictionary<string, SystemGraph.GraphSemanticValue>(StringComparer.Ordinal)
            {
                [LogicalEdgeIdMetadata] = SystemGraph.GraphSemanticValue.String(edge.EdgeId),
                [DiagramEdgeTypeMetadata] = edgeTypes.GetValueOrDefault(edge.EdgeId) is { } type
                    ? SystemGraph.GraphSemanticValue.String(type) : SystemGraph.GraphSemanticValue.Null
            })).ToArray();
        if (nodeIds.Values.Distinct().Count() != nodeIds.Count)
            return new(null, [new(GraphDiagnosticCodes.DuplicateNode, "Stable compilation node identity collision.", [])]);
        if (snapshotRelationships.Select(item => item.Relationship.Id).Distinct().Count() != snapshotRelationships.Length)
            return new(null, [new(GraphDiagnosticCodes.DuplicateEdge, "Stable compilation edge identity collision.", [])]);
        context = new(
            new System.Collections.ObjectModel.ReadOnlyDictionary<SystemGraph.NodeId, DiagramNode>(nodes.Values.ToDictionary(node => nodeIds[node.Id], CloneNode)),
            new System.Collections.ObjectModel.ReadOnlyDictionary<SystemGraph.NodeId, IReadOnlyList<CompilationPort>>(nodes.Keys.ToDictionary(id => nodeIds[id], id => portsByNode.GetValueOrDefault(id) ?? Array.Empty<CompilationPort>())),
            new System.Collections.ObjectModel.ReadOnlyDictionary<SystemGraph.EdgeId, ProjectedEdge>(projectedEdges.ToDictionary(edge => StableEdgeId(edge.EdgeId))));
        var graphId = StableNodeId($"document:{document.DocumentId}");
        LocalGraph.GraphLocalSnapshot snapshot;
        try
        {
            // Preserve the prior semantic-value bounds for the adapter's typed metadata.
            foreach (var value in snapshotNodes.SelectMany(node => node.Metadata.Values)
                         .Concat(snapshotRelationships.SelectMany(edge => edge.Metadata.Values)))
                SystemGraph.GraphValueAdmission.Validate(value, Ghostworx.System.Graph.Runtime.GraphLegacyMetadataPolicy.Default.Limits);
            snapshot = new(graphId, 0, snapshotNodes, snapshotRelationships,
                new Ghostworx.System.Primitives.SemanticAuthority("ghostagram.execution"), limits);
        }
        catch (ArgumentException exception)
        {
            context = null;
            return new(null, [new(GraphDiagnosticCodes.AdmissionRejected, exception.Message, [])]);
        }
        var frozenContext = context;
        var documentId = document.DocumentId;
        return new(snapshot, [])
        {
            Input = new(snapshot, (options, receiverLimits) => CompileSnapshot(snapshot, options, documentId, frozenContext, receiverLimits))
        };
    }

    private static GraphCompilationResult CompileSnapshot(LocalGraph.GraphLocalSnapshot snapshot, GraphCompileOptions options, string documentId, CompilationContext? context = null, LocalGraph.GraphLocalLimits? limits = null)
    {
        limits ??= GraphCompilationLimits.General;
        if (new[] { limits.MaximumInputBytes, limits.MaximumNodes, limits.MaximumRelationships,
                limits.MaximumChangeBatches, limits.MaximumMetadataEntries, limits.MaximumExtensions }
            .Any(value => value <= 0 || value == int.MaxValue) || snapshot.Nodes.Count > limits.MaximumNodes ||
            snapshot.Relationships.Count > limits.MaximumRelationships ||
            snapshot.Nodes.Any(node => node.Metadata.Count > limits.MaximumMetadataEntries) ||
            snapshot.Relationships.Any(edge => edge.Metadata.Count > limits.MaximumMetadataEntries))
            return new(null, [new(GraphDiagnosticCodes.AdmissionRejected, "Snapshot exceeds or invalidates the configured local structural policy.", [])]);
        try
        {
            foreach (var metadata in snapshot.Nodes.Select(node => node.Metadata)
                         .Concat(snapshot.Relationships.Select(edge => edge.Metadata)))
                _ = SystemGraph.GraphValueAdmission.Metadata(metadata, Ghostworx.System.Graph.Runtime.GraphLegacyMetadataPolicy.Default.Limits);
        }
        catch (ArgumentException exception)
        {
            return new(null, [new(GraphDiagnosticCodes.AdmissionRejected, exception.Message, [])]);
        }
        var diagnostics = ValidateSnapshot(snapshot, context, out var nodes, out var logicalIds, out var projectedEdges);
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
            var requestedGuards = options.LoopGuards ?? InferPersistedLoopGuards(snapshot, logicalIds, context);
            var guards = requestedGuards.GroupBy(guard => guard.NodeId, StringComparer.Ordinal).ToArray();
            foreach (var group in guards.Where(group => group.Count() > 1 || group.Any(guard => guard.MaxIterations < 1)))
                diagnostics.Add(new(GraphDiagnosticCodes.CycleGuardInvalid,
                    $"Loop guard for node '{group.Key}' must be unique and have MaxIterations greater than zero.", [group.Key]));

            var registeredControllers = snapshot.Nodes
                .Where(node => node.Metadata.TryGetValue(LoopControllerMetadata, out var value) && value.Kind == SystemGraph.GraphSemanticValueKind.Boolean && value.GetScalar<bool>())
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

        var stages = BuildStages(snapshot, components, logicalIds, guardByComponent, limits);
        var graph = new CompiledGraph(
            documentId,
            options.Profile,
            Fingerprint(snapshot, options.Profile, stages, logicalIds, context),
            Array.AsReadOnly(nodes.Values.OrderBy(node => logicalIds[node.Id], StringComparer.Ordinal).Select(node => SnapshotDiagramNode(node, logicalIds[node.Id], context)).ToArray()),
            Array.AsReadOnly(projectedEdges.ToArray()),
            Array.AsReadOnly(stages.Select(stage => new ExecutionStage(
                stage.Order,
                Array.AsReadOnly(stage.NodeIds.ToArray()),
                Array.AsReadOnly((stage.LoopGuards ?? []).ToArray()))).ToArray()));
        return new(graph, []);
    }

    private static List<GraphDiagnostic> ValidateSnapshot(
        LocalGraph.GraphLocalSnapshot snapshot,
        CompilationContext? context,
        out IReadOnlyDictionary<SystemGraph.NodeId, LocalGraph.GraphLocalNodeSnapshot> nodes,
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
            foreach (var metadata in node.Metadata.Where(pair => pair.Key is not (LogicalNodeIdMetadata or LoopControllerMetadata)))
                if (!IsFingerprintMetadataSupported(metadata.Value))
                    diagnostics.Add(new(GraphDiagnosticCodes.UnsupportedMetadata,
                        $"Node '{logical[node.Id]}' metadata '{metadata.Key}' uses unsupported semantic kind '{metadata.Value.Kind}'.",
                        [logical[node.Id]]));

        var relationshipGroups = snapshot.Relationships.GroupBy(item => item.Relationship.Id).ToArray();
        foreach (var duplicate in relationshipGroups.Where(group => group.Count() > 1))
            diagnostics.Add(new(GraphDiagnosticCodes.DuplicateEdge, $"Edge id '{duplicate.Key}' is duplicated.", [], [duplicate.Key.ToString()]));
        foreach (var duplicate in relationshipGroups.Select(group => group.First()).GroupBy(LogicalEdgeId, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(new(GraphDiagnosticCodes.DuplicateEdge, $"Edge id '{duplicate.Key}' is duplicated.", [], [duplicate.Key]));
        var projection = new List<ProjectedEdge>();
        foreach (var relationship in relationshipGroups.Select(group => group.First()).OrderBy(item => LogicalEdgeId(item), StringComparer.Ordinal))
        {
            foreach (var metadata in relationship.Metadata.Where(pair => pair.Key is not (LogicalEdgeIdMetadata or DiagramEdgeTypeMetadata)))
                if (!IsFingerprintMetadataSupported(metadata.Value))
                    diagnostics.Add(new(GraphDiagnosticCodes.UnsupportedMetadata,
                        $"Relationship '{LogicalEdgeId(relationship)}' metadata '{metadata.Key}' uses unsupported semantic kind '{metadata.Value.Kind}'.",
                        [], [LogicalEdgeId(relationship)]));
            if (!nodeMap.ContainsKey(relationship.Relationship.Source) || !nodeMap.ContainsKey(relationship.Relationship.Target))
            {
                diagnostics.Add(new(GraphDiagnosticCodes.EdgePortMissing,
                    $"Graph relationship '{LogicalEdgeId(relationship)}' references a missing node.", [], [LogicalEdgeId(relationship)]));
                continue;
            }
            projection.Add(context is not null && context.Edges.TryGetValue(relationship.Relationship.Id, out var projected)
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

    private static string LogicalNodeId(LocalGraph.GraphLocalNodeSnapshot node) =>
        node.Metadata.TryGetValue(LogicalNodeIdMetadata, out var value) && value.Kind == SystemGraph.GraphSemanticValueKind.String && !string.IsNullOrWhiteSpace(value.GetScalar<string>())
            ? value.GetScalar<string>()
            : node.Id.ToString();

    private static string LogicalEdgeId(LocalGraph.GraphLocalRelationshipSnapshot relationship) =>
        relationship.Metadata.TryGetValue(LogicalEdgeIdMetadata, out var value) && value.Kind == SystemGraph.GraphSemanticValueKind.String
            ? value.GetScalar<string>() : relationship.Relationship.Id.ToString();

    private static DiagramNode SnapshotDiagramNode(LocalGraph.GraphLocalNodeSnapshot node, string logicalId, CompilationContext? context) =>
        context is not null && context.Nodes.TryGetValue(node.Id, out var diagramNode)
            ? CloneNode(diagramNode) : new DiagramNode(logicalId, 0, 0, Label: node.NodeName);

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
    private static bool IsCyclic(IReadOnlyList<SystemGraph.NodeId> component, LocalGraph.GraphLocalSnapshot snapshot) =>
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
        LocalGraph.GraphLocalSnapshot snapshot,
        IReadOnlyList<IReadOnlyList<SystemGraph.NodeId>> components,
        IReadOnlyDictionary<SystemGraph.NodeId, string> logicalIds,
        IReadOnlyDictionary<string, LoopGuard> guards,
        LocalGraph.GraphLocalLimits limits)
    {
        var componentByNode = components.SelectMany(component => component.Select(nodeId => (nodeId, component)))
            .ToDictionary(item => item.nodeId, item => item.component);
        var membersByKey = components.ToDictionary(ComponentKey, StringComparer.Ordinal);
        var representativeByKey = membersByKey.ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.Ordinal);
        var keyByRepresentative = representativeByKey.ToDictionary(pair => pair.Value, pair => pair.Key);
        var condensationNodes = representativeByKey.Values.Select(nodeId => new LocalGraph.GraphLocalNodeSnapshot(
            nodeId, SystemGraph.NodeKind.Graph, null, new Dictionary<string, SystemGraph.GraphSemanticValue>())).ToArray();
        var condensationRelationships = snapshot.Relationships
            .Select(relationship => (
                Relationship: relationship,
                Source: componentByNode[relationship.Relationship.Source][0],
                Target: componentByNode[relationship.Relationship.Target][0]))
            .Where(item => item.Source != item.Target)
            .GroupBy(item => (item.Source, item.Target))
            .Select(group => new LocalGraph.GraphLocalRelationshipSnapshot(
                new LocalGraph.GraphLocalRelationship(group.First().Relationship.Relationship.Id, group.Key.Source, group.Key.Target, DependencyKind),
                new Dictionary<string, SystemGraph.GraphSemanticValue>()))
            .ToArray();
        var condensation = new LocalGraph.GraphLocalSnapshot(snapshot.GraphId, snapshot.Version, condensationNodes, condensationRelationships, snapshot.ValidationProvenance.OriginAuthority, limits);
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
                var stageNodes = keys.SelectMany(key => membersByKey[key])
                    .Select(nodeId => logicalIds[nodeId]).Order(StringComparer.Ordinal).ToArray();
                var stageGuards = keys.Where(guards.ContainsKey).Select(key => guards[key]).OrderBy(guard => guard.NodeId, StringComparer.Ordinal).ToArray();
                return new ExecutionStage(group.Key, stageNodes, stageGuards);
            })
            .ToArray();
    }

    private static string ComponentKey(IEnumerable<SystemGraph.NodeId> component) =>
        string.Join("\u001f", component.Select(nodeId => nodeId.ToString()).Order(StringComparer.Ordinal));

    private static IReadOnlyList<LoopGuard> InferPersistedLoopGuards(
        LocalGraph.GraphLocalSnapshot snapshot,
        IReadOnlyDictionary<SystemGraph.NodeId, string> logicalIds,
        CompilationContext? context) =>
        snapshot.Nodes
            .Where(node => node.Metadata.TryGetValue(LoopControllerMetadata, out var controller) && controller.Kind == SystemGraph.GraphSemanticValueKind.Boolean && controller.GetScalar<bool>())
            .Select(node => (node, diagramNode: context?.Nodes.GetValueOrDefault(node.Id)))
            .Where(item => item.diagramNode is not null)
            .Select(item => (item.node, property: item.diagramNode!.Properties.FirstOrDefault(property => property.Id == "maxIterations")))
            .Where(item => item.property?.Value is { ValueKind: System.Text.Json.JsonValueKind.Number } value && value.TryGetInt32(out var maximum) && maximum > 0)
            .Select(item => new LoopGuard(logicalIds[item.node.Id], item.property!.Value!.Value.GetInt32()))
            .ToArray();

    private static string Fingerprint(
        LocalGraph.GraphLocalSnapshot snapshot,
        GraphCompileProfile profile,
        IEnumerable<ExecutionStage> stages,
        IReadOnlyDictionary<SystemGraph.NodeId, string> logicalIds,
        CompilationContext? context)
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
                if (context is not null && context.Nodes.TryGetValue(snapshotNode.Id, out var node))
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
                if (context is not null && context.Ports.TryGetValue(snapshotNode.Id, out var ports))
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
                             .Where(pair => pair.Key is not (LogicalNodeIdMetadata or LoopControllerMetadata))
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
                if (context is not null && context.Edges.TryGetValue(relationship.Relationship.Id, out var edge))
                {
                    writer.WriteString("sourcePort", edge.SourcePortId);
                    writer.WriteString("targetPort", edge.TargetPortId);
                }
                if (relationship.Metadata.TryGetValue(DiagramEdgeTypeMetadata, out var typeValue) && typeValue.Kind == SystemGraph.GraphSemanticValueKind.String)
                    writer.WriteString("type", typeValue.GetScalar<string>());
                writer.WriteStartObject("metadata");
                foreach (var metadata in relationship.Metadata
                             .Where(pair => pair.Key is not (LogicalEdgeIdMetadata or DiagramEdgeTypeMetadata))
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

    private static void WriteMetadataValue(Utf8JsonWriter writer, SystemGraph.GraphSemanticValue value)
    {
        // Keep kind tags: numerically equal values with different semantic types remain distinct.
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind.ToString());
        writer.WritePropertyName("value");
        switch (value.Kind)
        {
            case SystemGraph.GraphSemanticValueKind.Null: writer.WriteNullValue(); break;
            case SystemGraph.GraphSemanticValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.GetArray()) WriteMetadataValue(writer, item);
                writer.WriteEndArray(); break;
            case SystemGraph.GraphSemanticValueKind.Object:
                writer.WriteStartObject();
                foreach (var pair in value.GetObject().OrderBy(pair => pair.Key, StringComparer.Ordinal))
                { writer.WritePropertyName(pair.Key); WriteMetadataValue(writer, pair.Value); }
                writer.WriteEndObject(); break;
            default: JsonSerializer.Serialize(writer, value.GetScalar<object>()); break;
        }
        writer.WriteEndObject();
    }

    private static bool IsFingerprintMetadataSupported(SystemGraph.GraphSemanticValue value) => value.Kind switch
    {
        SystemGraph.GraphSemanticValueKind.Bytes or SystemGraph.GraphSemanticValueKind.OpaqueExtension => false,
        SystemGraph.GraphSemanticValueKind.Array => value.GetArray().All(IsFingerprintMetadataSupported),
        SystemGraph.GraphSemanticValueKind.Object => value.GetObject().Values.All(IsFingerprintMetadataSupported),
        _ => true
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
            Editor = property.Editor is null ? null : property.Editor with { ExtensionData = CloneExtensions(property.Editor.ExtensionData) },
            ExtensionData = CloneExtensions(property.ExtensionData)
        }).ToArray()),
        Style = node.Style is null ? null : node.Style with { ExtensionData = CloneExtensions(node.Style.ExtensionData) },
        Sections = node.Sections is null ? null : Array.AsReadOnly(node.Sections.Select(section => section with
        {
            ExtensionData = CloneExtensions(section.ExtensionData)
        }).ToArray()),
        Presentation = node.Presentation is null ? null : node.Presentation with
        {
            CollapsedSectionIds = node.Presentation.CollapsedSectionIds is null ? null : Array.AsReadOnly(node.Presentation.CollapsedSectionIds.ToArray()),
            ExtensionData = CloneExtensions(node.Presentation.ExtensionData)
        },
        ExtensionData = CloneExtensions(node.ExtensionData)
    };

    private static IDictionary<string, JsonElement>? CloneExtensions(IDictionary<string, JsonElement>? values) =>
        values?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
}
