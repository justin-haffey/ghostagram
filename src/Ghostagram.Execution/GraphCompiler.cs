using System.Text.Json;
using Ghostagram.Core;

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
    GraphCompilationResult Compile(DiagramDocument document, GraphCompileOptions options);
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
}

/// <summary>Projects visual ports to node dependencies, validates cycles, and emits deterministic concurrent stages.</summary>
public sealed class GraphCompiler(INodeTypeRegistry registry) : IGraphCompiler
{
    public GraphCompilationResult Compile(DiagramDocument document, GraphCompileOptions options)
    {
        var diagnostics = ValidateAndProject(document, out var nodes, out var projectedEdges);
        if (diagnostics.Count > 0) return new(null, diagnostics);

        var adjacency = nodes.Keys.ToDictionary(id => id, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var edge in projectedEdges) adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
        var components = StronglyConnectedComponents(adjacency);
        var cyclic = components.Where(component => IsCyclic(component, adjacency)).ToArray();

        if (options.Profile == GraphCompileProfile.DagOnly && cyclic.Length > 0)
        {
            diagnostics.AddRange(cyclic.Select(component => CycleDiagnostic(
                GraphDiagnosticCodes.CycleNotAllowed,
                "DAG compilation does not allow cycle",
                component,
                adjacency,
                projectedEdges)));
            return new(null, diagnostics);
        }

        var guardByComponent = new Dictionary<string, LoopGuard>(StringComparer.Ordinal);
        if (options.Profile == GraphCompileProfile.BoundedCycles)
        {
            var requestedGuards = options.LoopGuards ?? InferPersistedLoopGuards(document);
            var guards = requestedGuards.GroupBy(guard => guard.NodeId, StringComparer.Ordinal).ToArray();
            foreach (var group in guards.Where(group => group.Count() > 1 || group.Any(guard => guard.MaxIterations < 1)))
            {
                diagnostics.Add(new(
                    GraphDiagnosticCodes.CycleGuardInvalid,
                    $"Loop guard for node '{group.Key}' must be unique and have MaxIterations greater than zero.",
                    [group.Key]));
            }
            var registeredControllers = document.Nodes
                .Where(node => node.TypeId is not null && registry.TryGet(node.TypeId, node.TypeVersion, out var registration) && registration.Descriptor.IsLoopController)
                .Select(node => node.Id)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var group in guards.Where(group => !registeredControllers.Contains(group.Key)))
                diagnostics.Add(new(GraphDiagnosticCodes.CycleGuardInvalid, $"Loop guard node '{group.Key}' is not a registered loop-controller node.", [group.Key]));

            foreach (var component in cyclic)
            {
                var matches = guards.Where(group => registeredControllers.Contains(group.Key) && component.Contains(group.Key, StringComparer.Ordinal) && group.Count() == 1 && group.Single().MaxIterations > 0)
                    .Select(group => group.Single()).ToArray();
                if (matches.Length != 1)
                {
                    var path = FindCyclePath(component, adjacency);
                    diagnostics.Add(new(
                        GraphDiagnosticCodes.CycleGuardRequired,
                        $"Bounded-cycle compilation requires exactly one positive loop guard for cycle '{string.Join(" -> ", path)}'.",
                        path.Distinct(StringComparer.Ordinal).ToArray(),
                        CycleEdges(path, projectedEdges)));
                }
                else
                {
                    guardByComponent[ComponentKey(component)] = matches[0];
                }
            }
            if (diagnostics.Count > 0) return new(null, diagnostics);
        }

        var stages = BuildStages(components, projectedEdges, guardByComponent);
        var graph = new CompiledGraph(
            document.DocumentId,
            options.Profile,
            Fingerprint(document, options.Profile, stages),
            Array.AsReadOnly(nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal).Select(CloneNode).ToArray()),
            Array.AsReadOnly(projectedEdges.ToArray()),
            Array.AsReadOnly(stages.Select(stage => new ExecutionStage(stage.Order, Array.AsReadOnly(stage.NodeIds.ToArray()), Array.AsReadOnly((stage.LoopGuards ?? []).ToArray()))).ToArray()));
        return new(graph, []);
    }

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

    private static IReadOnlyList<string[]> StronglyConnectedComponents(IReadOnlyDictionary<string, SortedSet<string>> adjacency)
    {
        var index = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<string[]>();

        void Visit(string node)
        {
            indices[node] = index;
            lowLinks[node] = index++;
            stack.Push(node);
            onStack.Add(node);
            foreach (var target in adjacency[node])
            {
                if (!indices.ContainsKey(target))
                {
                    Visit(target);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[target]);
                }
                else if (onStack.Contains(target))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], indices[target]);
                }
            }
            if (lowLinks[node] != indices[node]) return;
            var component = new List<string>();
            string current;
            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            } while (!string.Equals(current, node, StringComparison.Ordinal));
            components.Add(component.Order(StringComparer.Ordinal).ToArray());
        }

        foreach (var node in adjacency.Keys.Order(StringComparer.Ordinal))
            if (!indices.ContainsKey(node)) Visit(node);
        return components;
    }

    private static bool IsCyclic(IReadOnlyList<string> component, IReadOnlyDictionary<string, SortedSet<string>> adjacency) =>
        component.Count > 1 || adjacency[component[0]].Contains(component[0]);

    private static GraphDiagnostic CycleDiagnostic(
        string code,
        string prefix,
        IReadOnlyList<string> component,
        IReadOnlyDictionary<string, SortedSet<string>> adjacency,
        IReadOnlyList<ProjectedEdge> edges)
    {
        var path = FindCyclePath(component, adjacency);
        return new(code, $"{prefix}: {string.Join(" -> ", path)}.", path.Distinct(StringComparer.Ordinal).ToArray(), CycleEdges(path, edges));
    }

    private static string[] FindCyclePath(IReadOnlyList<string> component, IReadOnlyDictionary<string, SortedSet<string>> adjacency)
    {
        var allowed = component.ToHashSet(StringComparer.Ordinal);
        foreach (var start in component.Order(StringComparer.Ordinal))
        {
            var path = new List<string>();
            var active = new HashSet<string>(StringComparer.Ordinal);
            if (Find(start, start, path, active, allowed, adjacency, out var cycle)) return cycle;
        }
        throw new InvalidOperationException("A cyclic component did not contain a discoverable cycle.");

        static bool Find(string current, string start, List<string> path, HashSet<string> active, HashSet<string> allowed,
            IReadOnlyDictionary<string, SortedSet<string>> graph, out string[] cycle)
        {
            path.Add(current);
            active.Add(current);
            foreach (var target in graph[current].Where(allowed.Contains))
            {
                if (target == start)
                {
                    cycle = [.. path, start];
                    return true;
                }
                if (!active.Contains(target) && Find(target, start, path, active, allowed, graph, out cycle)) return true;
            }
            active.Remove(current);
            path.RemoveAt(path.Count - 1);
            cycle = [];
            return false;
        }
    }

    private static string[] CycleEdges(IReadOnlyList<string> path, IReadOnlyList<ProjectedEdge> edges) =>
        path.Zip(path.Skip(1), (source, target) => edges.First(edge => edge.SourceNodeId == source && edge.TargetNodeId == target).EdgeId).ToArray();

    private static IReadOnlyList<ExecutionStage> BuildStages(
        IReadOnlyList<string[]> components,
        IReadOnlyList<ProjectedEdge> edges,
        IReadOnlyDictionary<string, LoopGuard> guards)
    {
        var componentByNode = components.SelectMany(component => component.Select(node => (node, component)))
            .ToDictionary(item => item.node, item => item.component, StringComparer.Ordinal);
        var outgoing = components.ToDictionary(ComponentKey, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var indegree = components.ToDictionary(ComponentKey, _ => 0, StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            var source = ComponentKey(componentByNode[edge.SourceNodeId]);
            var target = ComponentKey(componentByNode[edge.TargetNodeId]);
            if (source != target && outgoing[source].Add(target)) indegree[target]++;
        }

        var remaining = components.ToDictionary(ComponentKey, component => component, StringComparer.Ordinal);
        var stages = new List<ExecutionStage>();
        var order = 0;
        while (remaining.Count > 0)
        {
            var ready = remaining.Keys.Where(key => indegree[key] == 0).Order(StringComparer.Ordinal).ToArray();
            if (ready.Length == 0) throw new InvalidOperationException("The component projection must be acyclic.");
            var stageNodes = ready.SelectMany(key => remaining[key]).Order(StringComparer.Ordinal).ToArray();
            var stageGuards = ready.Where(guards.ContainsKey).Select(key => guards[key]).OrderBy(guard => guard.NodeId, StringComparer.Ordinal).ToArray();
            stages.Add(new(order, stageNodes, stageGuards));
            foreach (var key in ready)
            {
                remaining.Remove(key);
                foreach (var target in outgoing[key]) indegree[target]--;
            }
            order++;
        }
        return stages;
    }

    private static string ComponentKey(IEnumerable<string> component) => string.Join("\u001f", component.Order(StringComparer.Ordinal));

    private IReadOnlyList<LoopGuard> InferPersistedLoopGuards(DiagramDocument document) =>
        document.Nodes.Where(node => node.TypeId is not null && registry.TryGet(node.TypeId, node.TypeVersion, out var registration) && registration.Descriptor.IsLoopController)
            .Select(node => (node, property: node.Properties.FirstOrDefault(property => property.Id == "maxIterations")))
            .Where(item => item.property?.Value is { ValueKind: System.Text.Json.JsonValueKind.Number } value && value.TryGetInt32(out var maximum) && maximum > 0)
            .Select(item => new LoopGuard(item.node.Id, item.property!.Value!.Value.GetInt32()))
            .ToArray();

    private static string Fingerprint(DiagramDocument document, GraphCompileProfile profile, IEnumerable<ExecutionStage> stages)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("documentId", document.DocumentId);
            writer.WriteString("profile", profile.ToString());
            writer.WriteStartArray("nodes");
            foreach (var node in document.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", node.Id);
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
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("ports");
            foreach (var port in document.Ports.OrderBy(port => port.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", port.Id);
                writer.WriteString("nodeId", port.NodeId);
                writer.WriteString("direction", port.Direction);
                writer.WriteString("scope", port.Scope);
                writer.WriteNumber("maxConnections", port.MaxConnections);
                writer.WriteBoolean("enabled", port.Enabled);
                writer.WriteString("propertyId", port.PropertyId);
                writer.WriteNumber("order", port.Order);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("edges");
            foreach (var edge in document.Edges.OrderBy(edge => edge.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", edge.Id);
                writer.WriteString("source", edge.SourcePortId);
                writer.WriteString("target", edge.TargetPortId);
                writer.WriteString("type", edge.Type);
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
