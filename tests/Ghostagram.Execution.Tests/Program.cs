using System.Text.Json;
using System.Reflection;
using Ghostagram.Core;
using Ghostagram.Execution;
using Ghostagram.NodeSets.Maf;
using Ghostagram.NodeSets.UML;
using Ghostworx.System.Graph.Features;
using Ghostworx.System.Graph.Validation;
using SystemGraph = Ghostworx.System.Graph;

#pragma warning disable CS0618 // This suite intentionally proves compatibility-adapter parity and obsolescence.

var simpleType = new NodeTypeDescriptor(
    "test.node", 1, "Test node", "Tests", "Science", 200, 120,
    properties:
    [
        new("name", "Name", DefaultValue: Json("\"default\"")),
        new("payload", "Payload", DiagramPropertyTypes.Json, DiagramPropertyModes.DisplayAndEdit, Connectable: true)
    ],
    ports:
    [
        new("payload-in", "target", PropertyId: "payload", Label: "Payload", Order: 1),
        new("next", "source", Label: "Next", Order: 2)
    ]);
var registry = new NodeTypeRegistry([new("tests", "Tests", [simpleType]), MafOrchestrationNodeSet.Descriptor, UmlNodeSet.Descriptor]);
Assert(registry.GetLatest("test.node").Descriptor.Version == 1, "Registry resolves the latest registered version.");
var mutableOptions = new List<string> { "one", "two" };
var mutableMetadata = new Dictionary<string, JsonElement>();
NodeTypeDescriptor clonedDescriptor;
using (var defaultDocument = JsonDocument.Parse("\"one\""))
using (var propertyMetadataDocument = JsonDocument.Parse("{\"rank\":1}"))
using (var descriptorMetadataDocument = JsonDocument.Parse("{\"owner\":\"tests\"}"))
{
    mutableMetadata["catalog"] = descriptorMetadataDocument.RootElement;
    clonedDescriptor = new("test.cloned", 1, "Cloned", "Tests", properties:
        [new("choice", "Choice", DiagramPropertyTypes.Enum, Options: mutableOptions, DefaultValue: defaultDocument.RootElement, Metadata: propertyMetadataDocument.RootElement)],
        metadata: mutableMetadata);
}
mutableOptions[0] = "mutated";
mutableMetadata.Clear();
Assert(clonedDescriptor.Properties.Single().Options!.SequenceEqual(["one", "two"]), "Catalog construction defensively clones property options.");
Assert(clonedDescriptor.Properties.Single().DefaultValue!.Value.GetString() == "one" && clonedDescriptor.Properties.Single().Metadata!.Value.GetProperty("rank").GetInt32() == 1, "Catalog property JSON survives source document disposal.");
Assert(clonedDescriptor.Metadata["catalog"].GetProperty("owner").GetString() == "tests", "Catalog metadata is cloned before singleton storage.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", width: 0), "Node dimensions must be finite and positive.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", properties: [new("choice", "Choice", DiagramPropertyTypes.Enum)]), "Enum property 'choice' requires at least one option.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", ports: [new("bad", "sideways")]), "Port 'bad' has invalid direction 'sideways'.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", ports: [new("bad", Anchor: "diagonal")]), "Port 'bad' has invalid fixed anchor 'diagonal'.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", rendererKey: " ", rendererVersion: 1), "A renderer key cannot be blank.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", rendererKey: "test.card"), "Renderer key and version must be specified together.");
Expect<ArgumentOutOfRangeException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", rendererKey: "test.card", rendererVersion: 0), "A renderer version must be positive.");

var mutableSections = new List<NodeSectionDefinition>
{
    new("identity", "Identity", Order: 10),
    new("preferences", "Preferences", Order: 20),
    new("advanced", "Advanced", "preferences", 10)
};
var advancedType = new NodeTypeDescriptor(
    "test.advanced", 1, "Advanced node", "Tests", width: 280, height: 220,
    properties:
    [
        new("name", "Name", SectionId: "identity", Editor: new(DiagramPropertyEditorKinds.Text, "Customer name")),
        new("confidence", "Confidence", DiagramPropertyTypes.Decimal, DiagramPropertyModes.DisplayAndEdit,
            SectionId: "advanced", Editor: new(DiagramPropertyEditorKinds.Range, Minimum: 0, Maximum: 1, Step: 0.05m)),
        new("color", "Color", SectionId: "preferences", Editor: new(DiagramPropertyEditorKinds.Color))
    ],
    sections: mutableSections,
    presentation: new(CollapsedSectionIds: ["advanced"]),
    rendererKey: "test.card",
    rendererVersion: 2);
mutableSections[0] = new("mutated", "Mutated");
Assert(advancedType.Sections[0].Id == "identity", "Catalog construction defensively clones section definitions.");
Assert(advancedType.Presentation!.CollapsedSectionIds!.SequenceEqual(["advanced"]), "Catalog construction owns an immutable collapse-state snapshot.");

Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", sections: [new("same", "One"), new("same", "Two")]), "Duplicate node section id 'same'.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", sections: [new("child", "Child", "missing")]), "Section 'child' references unknown parent section 'missing'.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", sections: [new("a", "A", "b"), new("b", "B", "a")]), "Section 'a' participates in a parent cycle.");
var tooDeepSections = Enumerable.Range(1, 9)
    .Select(index => new NodeSectionDefinition($"s{index}", $"Section {index}", index == 1 ? null : $"s{index - 1}"))
    .ToArray();
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", sections: tooDeepSections), "Section 's9' exceeds the maximum nesting depth of 8.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", properties: [new("name", "Name", SectionId: "missing")]), "Property 'name' references unknown section 'missing'.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", properties: [new("flag", "Flag", DiagramPropertyTypes.Boolean, Editor: new(DiagramPropertyEditorKinds.Range))]), "Editor 'range' is incompatible with property 'flag' of type 'boolean'.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", properties: [new("name", "Name", Editor: new(DiagramPropertyEditorKinds.Text, Minimum: 0))]), "Editor 'text' for property 'name' cannot define numeric bounds.");
Expect<ArgumentException>(() => new NodeTypeDescriptor("bad", 1, "Bad", "Tests", sections: [new("fixed", "Fixed", Collapsible: false)], presentation: new(CollapsedSectionIds: ["fixed"])), "Node presentation collapses non-collapsible section 'fixed'.");

Expect<InvalidOperationException>(
    () => new NodeTypeRegistry([new("duplicate", "Duplicate", [simpleType, simpleType])]),
    "Duplicate node type registration 'test.node@1'.");

var factory = new DeterministicNodeFactory(registry);
var request = new NodeCreationRequest("node-1", "test.node", 10, 20,
    PropertyValues: new Dictionary<string, JsonElement?> { ["payload"] = Json("{\"id\":7}") });
var created1 = factory.Create(request);
var created2 = factory.Create(request);
Assert(JsonSerializer.Serialize(created1) == JsonSerializer.Serialize(created2), "Node creation is deterministic for the same catalog and request.");
Assert(created1.Node.TypeId == "test.node" && created1.Node.TypeVersion == 1, "Factory stamps the persisted type identity.");
Assert(created1.Node.Sections is null && created1.Node.Presentation is null, "Factory leaves simple nodes free of advanced presentation payload.");
Assert(created1.Ports.Select(port => port.Id).SequenceEqual(["node-1:payload-in", "node-1:next"]), "Factory creates stable property-port identifiers.");
Assert(created1.Ports[0].PropertyId == "payload", "Factory preserves property-to-port association.");
Assert(created1.Node.Properties.Single(property => property.Id == "payload").Value?.GetProperty("id").GetInt32() == 7, "Factory preserves custom property values.");
Expect<ArgumentException>(() => factory.Create(request with { PropertyValues = new Dictionary<string, JsonElement?> { ["unknown"] = null } }), "Property 'unknown' is not defined by node type 'test.node@1'.");

var advancedRegistry = new NodeTypeRegistry([new("advanced-tests", "Advanced tests", [advancedType])]);
var advancedCreated = new DeterministicNodeFactory(advancedRegistry).Create(new("advanced-1", "test.advanced", 40, 60));
Assert(advancedCreated.Node.Sections!.Select(section => section.Id).SequenceEqual(["identity", "preferences", "advanced"]), "Factory materializes flat and nested section definitions without a second node datatype.");
Assert(advancedCreated.Node.Properties.Single(property => property.Id == "confidence").Editor?.Kind == DiagramPropertyEditorKinds.Range, "Factory materializes typed property editor hints.");
Assert(advancedCreated.Node.Presentation!.DisplayMode == DiagramNodeDisplayModes.Expanded && advancedCreated.Node.Presentation.ExpandedHeight == 220, "Factory materializes authoritative expanded geometry.");
Assert(advancedCreated.Node.Presentation.CollapsedSectionIds!.SequenceEqual(["advanced"]), "Factory preserves authoritative section-collapse state.");
Assert(advancedCreated.Node.RendererKey == "test.card" && advancedCreated.Node.RendererVersion == 2, "Factory stamps the typed renderer identity from its descriptor.");
var advancedNodeRoundTrip = JsonSerializer.Deserialize<DiagramNode>(JsonSerializer.Serialize(advancedCreated.Node, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
Assert(advancedNodeRoundTrip.Sections![2].ParentSectionId == "preferences" && advancedNodeRoundTrip.Properties[1].SectionId == "advanced", "Factory-created nested nodes survive a web JSON round trip.");

var compiler = new GraphCompiler(registry);
IGraphSnapshotCompiler snapshotCompiler = compiler;
var snapshotCompileMethod = typeof(IGraphSnapshotCompiler).GetMethod(nameof(IGraphSnapshotCompiler.Compile), [typeof(SystemGraph.GraphSnapshot), typeof(GraphCompileOptions)]);
var legacyCompileMethod = typeof(IGraphCompiler).GetMethod(nameof(IGraphCompiler.Compile), [typeof(DiagramDocument), typeof(GraphCompileOptions)]);
var engineSnapshotCompileMethod = typeof(IGraphExecutionEngine).GetMethod(nameof(IGraphExecutionEngine.Compile), [typeof(SystemGraph.GraphSnapshot), typeof(GraphCompileOptions)]);
var engineLegacyCompileMethod = typeof(IGraphExecutionEngine).GetMethod(nameof(IGraphExecutionEngine.Compile), [typeof(DiagramDocument), typeof(GraphCompileOptions)]);
Assert(snapshotCompileMethod is not null && engineSnapshotCompileMethod is not null, "Execution contracts expose GraphSnapshot compilation as the graph-native call surface.");
Assert(legacyCompileMethod?.GetCustomAttribute<ObsoleteAttribute>() is not null && engineLegacyCompileMethod?.GetCustomAttribute<ObsoleteAttribute>() is not null,
    "Every DiagramDocument compilation contract is explicitly marked as a compatibility adapter.");
Assert(typeof(GraphExecutionEngine).GetConstructors().Single().GetParameters()[0].ParameterType == typeof(IGraphSnapshotCompiler),
    "The production execution engine depends on the graph-native compiler contract.");
var compilerAlgorithms = typeof(GraphCompiler).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
Assert(!compilerAlgorithms.Contains("StronglyConnectedComponents") && !compilerAlgorithms.Contains("TopologicalSort") && !compilerAlgorithms.Contains("FindCyclePath"),
    "Ghostagram retains no private SCC, topological-sort, or cycle-member traversal implementation.");
var dagProfile = (GraphProfile?)typeof(GraphCompiler).GetField("DagProfile", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
Assert(dagProfile?.Features is [DagFeature],
    "DAG rejection delegates to the System graph profile.");
var dag = Document(
    ["a", "b", "c"],
    [("ab", "a", "b"), ("ac", "a", "c")]);
var dagResult = compiler.Compile(dag, new(GraphCompileProfile.DagOnly));
Assert(dagResult.Succeeded, "DAG compilation succeeds.");
Assert(dagResult.Graph!.PlanFingerprint.Length == 64, "Compiled plans have a stable SHA-256 fingerprint.");
Assert(dagResult.Graph!.Stages.Select(stage => stage.Order).Distinct().Count() == 2, "DAG compiler emits concurrent topological stage orders.");
Assert(dagResult.Graph.Stages.Count == 2, "Ready components are grouped into deterministic concurrent stage buckets.");
Assert(dagResult.Graph.Stages.Where(stage => stage.Order == 1).SelectMany(stage => stage.NodeIds).SequenceEqual(["b", "c"]), "Independent successors share the same concurrent stage order.");
var dagProjection = compiler.ProjectSnapshot(dag);
Assert(dagProjection.Succeeded, "A valid diagram projects to an immutable graph snapshot.");
var graphNativeDag = snapshotCompiler.Compile(dagProjection.Snapshot!, new(GraphCompileProfile.DagOnly));
Assert(graphNativeDag.Succeeded, "The graph-native compiler accepts the projected DAG.");
Assert(StageSignature(graphNativeDag) == StageSignature(dagResult), "Diagram and graph-native DAG compilation produce identical stages.");
Assert(graphNativeDag.Graph!.PlanFingerprint == dagResult.Graph.PlanFingerprint, "Diagram and graph-native DAG compilation produce identical fingerprints.");

var nativeA = new SystemGraph.NodeId(Guid.Parse("10000000-0000-0000-0000-000000000001"));
var nativeB = new SystemGraph.NodeId(Guid.Parse("10000000-0000-0000-0000-000000000002"));
var nativeKind = SystemGraph.NodeKind.Define("Ghostagram.Execution.Tests", "Native");
var nativeRelationshipKind = SystemGraph.RelationshipKind.Define("Ghostagram.Execution.Tests", "Dependency");
var nativeNodes = new[]
{
    new SystemGraph.GraphNodeSnapshot(nativeA, nativeKind, "A", new Dictionary<string, object?> { [GraphExecutionMetadata.LoopController] = true }),
    new SystemGraph.GraphNodeSnapshot(nativeB, nativeKind, "B", new Dictionary<string, object?>())
};
var nativeForward = new SystemGraph.GraphRelationshipSnapshot(
    new(new SystemGraph.EdgeId(Guid.Parse("20000000-0000-0000-0000-000000000001")), nativeA, nativeB, nativeRelationshipKind),
    new Dictionary<string, object?>());
var nativeSnapshot = new SystemGraph.GraphSnapshot(
    new(Guid.Parse("30000000-0000-0000-0000-000000000001")), 1, nativeNodes, [nativeForward]);
var directNativeDag = snapshotCompiler.Compile(nativeSnapshot, new(GraphCompileProfile.DagOnly));
Assert(directNativeDag.Succeeded && directNativeDag.Graph!.Stages.Count == 2, "The graph-native compiler accepts snapshots without diagram adapter metadata.");
var nativeReverse = new SystemGraph.GraphRelationshipSnapshot(
    new(new SystemGraph.EdgeId(Guid.Parse("20000000-0000-0000-0000-000000000002")), nativeB, nativeA, nativeRelationshipKind),
    new Dictionary<string, object?>());
var directNativeCycle = snapshotCompiler.Compile(
    new SystemGraph.GraphSnapshot(nativeSnapshot.GraphId, 2, nativeNodes, [nativeForward, nativeReverse]),
    new(GraphCompileProfile.BoundedCycles, [new(nativeA.ToString(), 4)]));
Assert(directNativeCycle.Succeeded && directNativeCycle.Graph!.Stages.Single().LoopGuards!.Single() == new LoopGuard(nativeA.ToString(), 4), "A native snapshot expresses its explicit bounded-cycle guard with the stable node ID.");
var unmarkedNativeNodes = nativeNodes.Select(node => node.Id == nativeA
    ? new SystemGraph.GraphNodeSnapshot(node.Id, node.Kind, node.NodeName, new Dictionary<string, object?>()) : node).ToArray();
var unmarkedNativeCycle = snapshotCompiler.Compile(
    new SystemGraph.GraphSnapshot(nativeSnapshot.GraphId, 2, unmarkedNativeNodes, [nativeForward, nativeReverse]),
    new(GraphCompileProfile.BoundedCycles, [new(nativeA.ToString(), 4)]));
Assert(unmarkedNativeCycle.Diagnostics.Any(diagnostic => diagnostic.Code == GraphDiagnosticCodes.CycleGuardInvalid),
    "Native bounded-cycle guards require the explicit semantic loop-controller marker.");
var unsupportedNativeMetadata = nativeNodes.Select(node => node.Id == nativeB
    ? new SystemGraph.GraphNodeSnapshot(node.Id, node.Kind, node.NodeName, new Dictionary<string, object?> { ["unsafe"] = new UnsupportedFingerprintMetadata() }) : node).ToArray();
var unsupportedNative = snapshotCompiler.Compile(
    new SystemGraph.GraphSnapshot(nativeSnapshot.GraphId, 1, unsupportedNativeMetadata, [nativeForward]),
    new(GraphCompileProfile.DagOnly));
Assert(unsupportedNative.Diagnostics.Single().Code == GraphDiagnosticCodes.UnsupportedMetadata,
    "Graph-native compilation rejects metadata without an explicit canonical fingerprint representation.");
var weightedNative = snapshotCompiler.Compile(
    new SystemGraph.GraphSnapshot(nativeSnapshot.GraphId, 1, nativeNodes,
        [new SystemGraph.GraphRelationshipSnapshot(nativeForward.Relationship, new Dictionary<string, object?> { ["weight"] = 2m })]),
    new(GraphCompileProfile.DagOnly));
Assert(weightedNative.Succeeded && weightedNative.Graph!.PlanFingerprint != directNativeDag.Graph!.PlanFingerprint,
    "Durable relationship metadata participates in graph-native plan fingerprints.");

var cycle = Document(["a", "b"], [("ab", "a", "b"), ("ba", "b", "a")]);
var rejectedCycle = compiler.Compile(cycle, new(GraphCompileProfile.DagOnly));
Assert(!rejectedCycle.Succeeded, "DAG compilation rejects cycles.");
Assert(rejectedCycle.Diagnostics.Single().Code == GraphDiagnosticCodes.CycleNotAllowed, "Cycle rejection has a stable diagnostic code.");
Assert(rejectedCycle.Diagnostics.Single().Message == "DAG compilation does not allow a cycle involving: a, b.", "Cycle diagnostics contain deterministic System SCC members.");
var cycleProjection = compiler.ProjectSnapshot(cycle);
Assert(cycleProjection.Succeeded, "A structurally valid cyclic diagram projects before policy validation.");
var graphNativeRejectedCycle = snapshotCompiler.Compile(cycleProjection.Snapshot!, new(GraphCompileProfile.DagOnly));
Assert(DiagnosticSignature(graphNativeRejectedCycle) == DiagnosticSignature(rejectedCycle), "Diagram and graph-native cycle rejection produce identical diagnostics.");

var unguarded = compiler.Compile(cycle, new(GraphCompileProfile.BoundedCycles));
Assert(unguarded.Diagnostics.Single().Code == GraphDiagnosticCodes.CycleGuardRequired, "Bounded cycles require an explicit guard.");
var persistedGuardNodes = cycle.Nodes.Select(node => node.Id == "a"
    ? node with { TypeId = "maf.loop-guard", Properties = [new("maxIterations", "Maximum iterations", DiagramPropertyTypes.Integer, Json("3"))] }
    : node).ToArray();
var controlledCycle = cycle with
{
    Nodes = persistedGuardNodes,
    Ports = [new DiagramPort("a:in", "a", "target", Label: "In", Order: 0), new DiagramPort("a:body", "a", "source", Label: "Body", Order: 1), new DiagramPort("a:complete", "a", "source", Label: "Complete", Order: 2), new DiagramPort("b-in", "b", "target"), new DiagramPort("b-out", "b", "source")],
    Edges = [new DiagramEdge("ab", "a:body", "b-in"), new DiagramEdge("ba", "b-out", "a:in")]
};
var guarded = compiler.Compile(controlledCycle, new(GraphCompileProfile.BoundedCycles, [new("a", 5)]));
Assert(guarded.Succeeded && guarded.Graph!.Stages.Single().LoopGuards!.Single() == new LoopGuard("a", 5), "A registered positive loop guard compiles with its SCC.");
var controlledProjection = compiler.ProjectSnapshot(controlledCycle);
var graphNativeGuarded = snapshotCompiler.Compile(controlledProjection.Snapshot!, new(GraphCompileProfile.BoundedCycles, [new("a", 5)]));
Assert(graphNativeGuarded.Succeeded, "The graph-native compiler accepts an explicitly guarded cycle.");
Assert(StageSignature(graphNativeGuarded) == StageSignature(guarded), "Diagram and graph-native bounded-cycle compilation produce identical stages and guards.");
Assert(graphNativeGuarded.Graph!.PlanFingerprint == guarded.Graph!.PlanFingerprint, "Diagram and graph-native bounded-cycle compilation produce identical fingerprints.");
var invalidExplicitGuard = compiler.Compile(cycle, new(GraphCompileProfile.BoundedCycles, [new("a", 5)]));
Assert(invalidExplicitGuard.Diagnostics.Any(diagnostic => diagnostic.Code == GraphDiagnosticCodes.CycleGuardInvalid), "Explicit guards must identify registered loop-controller nodes.");
var inferredGuard = compiler.Compile(controlledCycle, new(GraphCompileProfile.BoundedCycles));
Assert(inferredGuard.Succeeded && inferredGuard.Graph!.Stages.Single().LoopGuards!.Single() == new LoopGuard("a", 3), "Persisted loop-guard nodes compile without a duplicate hidden option.");
var native = compiler.Compile(cycle, new(GraphCompileProfile.AdapterNative));
Assert(native.Succeeded, "Adapter-native compilation permits agentic loops.");
var graphNativeAdapter = snapshotCompiler.Compile(cycleProjection.Snapshot!, new(GraphCompileProfile.AdapterNative));
Assert(graphNativeAdapter.Succeeded && StageSignature(graphNativeAdapter) == StageSignature(native), "Diagram and graph-native adapter compilation produce identical cyclic stages.");
Assert(graphNativeAdapter.Graph!.PlanFingerprint == native.Graph!.PlanFingerprint, "Diagram and graph-native adapter compilation produce identical fingerprints.");

var propertyChanged = dag with { Nodes = dag.Nodes.Select(node => node.Id == "a" ? node with { Properties = [new("value", "Value", DiagramPropertyTypes.Integer, Json("1"))] } : node).ToArray() };
var changedFingerprint = compiler.Compile(propertyChanged, new(GraphCompileProfile.DagOnly)).Graph!.PlanFingerprint;
Assert(changedFingerprint != dagResult.Graph.PlanFingerprint, "Execution property changes invalidate plan fingerprints and stale checkpoints.");
var changedProjection = compiler.ProjectSnapshot(propertyChanged);
Assert(snapshotCompiler.Compile(changedProjection.Snapshot!, new(GraphCompileProfile.DagOnly)).Graph!.PlanFingerprint == changedFingerprint, "Graph-native compilation preserves property-sensitive fingerprint parity.");

var duplicatePropertyNode = new DiagramNode("bad", 0, 0, Properties: [new("x", "X"), new("x", "X again")]);
var duplicateProperty = compiler.Compile(new DiagramDocument("bad", [duplicatePropertyNode], [], []), new(GraphCompileProfile.DagOnly));
Assert(duplicateProperty.Diagnostics.Single().Code == GraphDiagnosticCodes.DuplicateProperty, "Compiler rejects imported duplicate property IDs.");
var missingProperty = compiler.Compile(new DiagramDocument("bad-port", [new DiagramNode("bad", 0, 0)], [new DiagramPort("p", "bad", PropertyId: "missing")], []), new(GraphCompileProfile.DagOnly));
Assert(missingProperty.Diagnostics.Single().Code == GraphDiagnosticCodes.PortPropertyMissing, "Compiler rejects ports attached to missing properties.");
var invalidPortDirection = compiler.Compile(new DiagramDocument("bad-direction", [new DiagramNode("n", 0, 0)], [new DiagramPort("p", "n", "sideways")], []), new(GraphCompileProfile.DagOnly));
Assert(invalidPortDirection.Diagnostics.Single().Code == GraphDiagnosticCodes.PortDirectionInvalid, "Compiler rejects invalid imported port directions.");
var reversedEdge = compiler.Compile(new DiagramDocument("reversed", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0)], [new DiagramPort("a-in", "a", "target"), new DiagramPort("b-out", "b", "source")], [new DiagramEdge("e", "a-in", "b-out")]), new(GraphCompileProfile.DagOnly));
Assert(reversedEdge.Diagnostics.Single().Code == GraphDiagnosticCodes.EdgeDirectionInvalid, "Compiler rejects reversed edge topology.");
var duplicateEdges = compiler.Compile(new DiagramDocument("duplicate-edge", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0)], [new DiagramPort("a-out", "a", "source"), new DiagramPort("b-in", "b", "target")], [new DiagramEdge("e", "a-out", "b-in"), new DiagramEdge("e", "a-out", "b-in")]), new(GraphCompileProfile.DagOnly));
Assert(duplicateEdges.Diagnostics.Single().Code == GraphDiagnosticCodes.DuplicateEdge, "Compiler rejects duplicate edge IDs.");
var scopeMismatch = compiler.Compile(new DiagramDocument("scope", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0)], [new DiagramPort("a-out", "a", "source", "alpha"), new DiagramPort("b-in", "b", "target", "beta")], [new DiagramEdge("e", "a-out", "b-in")]), new(GraphCompileProfile.DagOnly));
Assert(scopeMismatch.Diagnostics.Single().Code == GraphDiagnosticCodes.EdgeScopeMismatch, "Compiler rejects incompatible port scopes.");
var overCapacity = compiler.Compile(new DiagramDocument("capacity", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0), new DiagramNode("c", 200, 0)], [new DiagramPort("a-out", "a", "source", MaxConnections: 1), new DiagramPort("b-in", "b", "target"), new DiagramPort("c-in", "c", "target")], [new DiagramEdge("ab", "a-out", "b-in"), new DiagramEdge("ac", "a-out", "c-in")]), new(GraphCompileProfile.DagOnly));
Assert(overCapacity.Diagnostics.Single().Code == GraphDiagnosticCodes.PortCapacityExceeded, "Compiler enforces MaxConnections capacity.");
var denyPolicy = new DiagramConnectionPolicy(DenyNodeIds: ["b"]);
var policyViolation = compiler.Compile(new DiagramDocument("policy", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0)], [new DiagramPort("a-out", "a", "source", ConnectionPolicy: denyPolicy), new DiagramPort("b-in", "b", "target")], [new DiagramEdge("e", "a-out", "b-in")]), new(GraphCompileProfile.DagOnly));
Assert(policyViolation.Diagnostics.Single().Code == GraphDiagnosticCodes.ConnectionPolicyViolation, "Compiler enforces allow and deny connection policies.");
var emptyAllowPolicy = new DiagramConnectionPolicy(AllowPortIds: []);
var emptyAllowViolation = compiler.Compile(new DiagramDocument("empty-allow", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0)], [new DiagramPort("a-out", "a", "source", ConnectionPolicy: emptyAllowPolicy), new DiagramPort("b-in", "b", "target")], [new DiagramEdge("e", "a-out", "b-in")]), new(GraphCompileProfile.DagOnly));
Assert(emptyAllowViolation.Diagnostics.Single().Code == GraphDiagnosticCodes.ConnectionPolicyViolation, "A defined empty allow-list denies every connection, matching the browser runtime.");
var samePortSelfLoop = compiler.Compile(new DiagramDocument("same-port-loop", [new DiagramNode("a", 0, 0)], [new DiagramPort("a-both", "a", "both", MaxConnections: 1)], [new DiagramEdge("loop", "a-both", "a-both")]), new(GraphCompileProfile.AdapterNative));
Assert(samePortSelfLoop.Succeeded, "A same-port self-loop consumes one MaxConnections slot, matching the browser runtime.");
var registeredDocument = new DiagramDocument("registered", [created1.Node], created1.Ports, []);
Assert(compiler.Compile(registeredDocument, new(GraphCompileProfile.DagOnly)).Succeeded, "Factory-created registered ports satisfy descriptor schema.");
var tamperedRegisteredPorts = created1.Ports.Select((port, index) => index == 0 ? port with { Label = "Tampered" } : port).ToArray();
var portSchemaMismatch = compiler.Compile(registeredDocument with { Ports = tamperedRegisteredPorts }, new(GraphCompileProfile.DagOnly));
Assert(portSchemaMismatch.Diagnostics.Single().Code == GraphDiagnosticCodes.NodePortSchemaMismatch, "Compiler rejects registered node ports that drift from their descriptor.");
var missingRequired = compiler.Compile(new DiagramDocument("required", [new DiagramNode("n", 0, 0, Properties: [new("name", "Name", Required: true)])], [], []), new(GraphCompileProfile.DagOnly));
Assert(missingRequired.Diagnostics.Single().Code == GraphDiagnosticCodes.RequiredPropertyMissing, "Compiler rejects missing required execution properties.");
var invalidPrimitive = compiler.Compile(new DiagramDocument("invalid", [new DiagramNode("n", 0, 0, Properties: [new("count", "Count", DiagramPropertyTypes.Integer, Json("\"many\""))])], [], []), new(GraphCompileProfile.DagOnly));
Assert(invalidPrimitive.Diagnostics.Single().Code == GraphDiagnosticCodes.PropertyValueInvalid, "Compiler rejects incompatible built-in primitive values.");
var invalidEnum = compiler.Compile(new DiagramDocument("invalid-enum", [new DiagramNode("n", 0, 0, Properties: [new("choice", "Choice", DiagramPropertyTypes.Enum, Json("\"other\""), Options: ["one", "two"])])], [], []), new(GraphCompileProfile.DagOnly));
Assert(invalidEnum.Diagnostics.Single().Code == GraphDiagnosticCodes.PropertyValueInvalid, "Compiler rejects enum values outside declared options.");
var mutableCompiledProperties = new List<DiagramNodeProperty> { new("value", "Value", Value: Json("\"before\"")) };
var mutableCompiledDocument = new DiagramDocument("snapshot", [new DiagramNode("n", 0, 0, Properties: mutableCompiledProperties)], [], []);
var compiledSnapshot = compiler.Compile(mutableCompiledDocument, new(GraphCompileProfile.DagOnly)).Graph!;
mutableCompiledProperties[0] = mutableCompiledProperties[0] with { Value = Json("\"after\"") };
Assert(compiledSnapshot.Nodes.Single().Properties.Single().Value!.Value.GetString() == "before", "Compiled plans detach property data from mutable source collections.");

var data = new ConcurrentExecutionData();
Parallel.For(0, 100, value => data.Set($"value-{value}", value));
Assert(data.Snapshot().Count == 100, "Concurrent execution data supports parallel node writes.");
var scopedKey = new ExecutionDataKey(new("run-1", ConversationId: "call-7"), "counter");
data.Set(scopedKey, 0);
Parallel.For(0, 100, iteration =>
{
    while (true)
    {
        data.TryGet<int>(scopedKey, out _, out var version);
        if (data.TryUpdate<int>(scopedKey, version, current => current + 1, out _)) break;
    }
});
Assert(data.TryGet<int>(scopedKey, out var counter, out var counterVersion) && counter == 100 && counterVersion == 101, "Scoped versioned data supports optimistic atomic updates.");

var adapter = new FakeAdapter();
var engine = new GraphExecutionEngine(compiler, [adapter]);
var engineNativeCompilation = engine.Compile(dagProjection.Snapshot!, new(GraphCompileProfile.DagOnly));
Assert(engineNativeCompilation.Succeeded && engineNativeCompilation.Graph!.PlanFingerprint == graphNativeDag.Graph!.PlanFingerprint,
    "The production execution engine compiles GraphSnapshot through its primary path.");
var engineCompatibilityCompilation = engine.Compile(dag, new(GraphCompileProfile.DagOnly));
Assert(engineCompatibilityCompilation.Succeeded && engineCompatibilityCompilation.Graph!.PlanFingerprint == engineNativeCompilation.Graph!.PlanFingerprint,
    "The obsolete DiagramDocument execution adapter remains parity-compatible.");
var activator = new FakeActivator();
var identity = new ExecutionRunIdentity("run-1", "activation-1", 1, "call-7");
var policy = new ExecutionRunPolicy(TimeSpan.FromMinutes(5), 100, 8, 1_000);
var execution = await engine.ExecuteAsync(new(native.Graph!, adapter.Id, identity, policy, data, activator));
Assert(execution.Succeeded && adapter.Calls == 1, "Execution delegates through the neutral adapter boundary.");
Assert(execution.Checkpoint?.PlanFingerprint == native.Graph!.PlanFingerprint, "Adapter checkpoints remain bound to the compiled plan fingerprint.");
await ExpectAsync<InvalidOperationException>(
    () => engine.ExecuteAsync(new(native.Graph!, adapter.Id, identity, policy, data, activator, new("other", "x", native.Graph.PlanFingerprint, identity.RunId, identity.ConversationId))).AsTask(),
    "Checkpoint adapter 'other' does not match requested adapter 'fake'.");
await using (var lease = await activator.AcquireAsync(new("customer-agent", "a", identity.RunId, identity.ActivationId, identity.Attempt, identity.ConversationId)))
{
    var updates = new List<ComponentUpdate>();
    await foreach (var update in lease.InvokeAsync(new("respond", Json("{\"message\":\"hello\"}")))) updates.Add(update);
    Assert(lease.ComponentKey == "customer-agent" && updates.Single().Kind == "completed", "Component invocation returns only neutral scripted updates.");
}
Assert(activator.LastLease!.Disposed, "Component leases own future boot-instance cleanup.");

var hitlAdapter = new HitlAdapter();
var hitlEngine = new GraphExecutionEngine(compiler, [hitlAdapter]);
var hitlSession = await hitlEngine.StartSessionAsync(new(native.Graph!, hitlAdapter.Id, identity, policy, data, activator));
await foreach (var _ in hitlSession.StartAsync()) { }
Assert(hitlSession.State == GraphExecutionSessionState.WaitingForExternalInput, "Execution sessions retain a run at an external-input pause.");
Assert(!hitlAdapter.LastRun!.Disposed, "A paused HITL run is not disposed.");
var pending = hitlSession.PendingExternalInput!;
await foreach (var _ in hitlSession.SubmitExternalResponseAsync(new(pending.RequestId, Json("\"approved\"")))) { }
Assert(hitlSession.State == GraphExecutionSessionState.Completed && hitlSession.Result.Succeeded, "A retained session continues to completion after an external response.");
Assert(!hitlAdapter.LastRun.Disposed, "A completed run remains session-owned until explicit disposal.");
await hitlSession.DisposeAsync();
Assert(hitlAdapter.LastRun.Disposed && hitlSession.State == GraphExecutionSessionState.Disposed, "Disposing the execution session releases its adapter run.");

var duplicateStreamAdapter = new FakeAdapter();
var duplicateStreamSession = await new GraphExecutionEngine(compiler, [duplicateStreamAdapter]).StartSessionAsync(new(native.Graph!, duplicateStreamAdapter.Id, identity, policy, data, activator));
var firstLazyStream = duplicateStreamSession.StartAsync();
var duplicateLazyStream = duplicateStreamSession.StartAsync();
await ConsumeAsync(firstLazyStream);
await ExpectAsync<InvalidOperationException>(() => ConsumeAsync(duplicateLazyStream), "Cannot begin Start while execution session state is 'Completed'.");
Assert(duplicateStreamSession.State == GraphExecutionSessionState.Completed, "A duplicate lazy stream cannot restart a completed session.");
await duplicateStreamSession.DisposeAsync();

var abandonedAdapter = new FakeAdapter();
var abandonedSession = await new GraphExecutionEngine(compiler, [abandonedAdapter]).StartSessionAsync(new(native.Graph!, abandonedAdapter.Id, identity, policy, data, activator));
_ = abandonedSession.StartAsync();
Assert(abandonedSession.State == GraphExecutionSessionState.Prepared, "An unenumerated lazy stream does not reserve session state.");
await abandonedSession.DisposeAsync();
Assert(abandonedSession.State == GraphExecutionSessionState.Disposed, "An abandoned lazy stream does not block disposal.");

var earlyBreakAdapter = new FakeAdapter();
var earlyBreakSession = await new GraphExecutionEngine(compiler, [earlyBreakAdapter]).StartSessionAsync(new(native.Graph!, earlyBreakAdapter.Id, identity, policy, data, activator));
await foreach (var _ in earlyBreakSession.StartAsync()) break;
Assert(earlyBreakSession.State == GraphExecutionSessionState.Failed && earlyBreakSession.Result.Error!.Contains("abandoned", StringComparison.Ordinal), "Breaking event consumption early fails the non-resumable session deterministically.");
await earlyBreakSession.DisposeAsync();

var faultingAdapter = new FaultingAdapter();
var faultingSession = await new GraphExecutionEngine(compiler, [faultingAdapter]).StartSessionAsync(new(native.Graph!, faultingAdapter.Id, identity, policy, data, activator));
await ExpectAsync<InvalidOperationException>(() => ConsumeAsync(faultingSession.StartAsync()), "adapter boom");
Assert(faultingSession.State == GraphExecutionSessionState.Failed && faultingSession.Result.Error == "adapter boom", "Adapter exceptions leave the session failed.");
await faultingSession.DisposeAsync();

var blockingAdapter = new BlockingAdapter();
var blockingSession = await new GraphExecutionEngine(compiler, [blockingAdapter]).StartSessionAsync(new(native.Graph!, blockingAdapter.Id, identity, policy, data, activator));
var blockingConsumption = ConsumeAsync(blockingSession.StartAsync());
await blockingAdapter.LastRun!.Started.Task;
await ExpectAsync<InvalidOperationException>(() => ConsumeAsync(blockingSession.StartAsync()), "Only one execution-session segment may stream events at a time.");
await blockingSession.CancelAsync();
await ExpectAsync<OperationCanceledException>(() => blockingConsumption, string.Empty);
Assert(blockingSession.State == GraphExecutionSessionState.Cancelled && blockingAdapter.LastRun.CancelCalls == 1, "Cancellation interrupts the active stream and records one adapter cancellation.");
await blockingSession.DisposeAsync();

var cancelRaceAdapter = new CancelRaceAdapter();
var cancelRaceSession = await new GraphExecutionEngine(compiler, [cancelRaceAdapter]).StartSessionAsync(new(native.Graph!, cancelRaceAdapter.Id, identity, policy, data, activator));
var cancelRaceConsumption = ConsumeAsync(cancelRaceSession.StartAsync());
await cancelRaceAdapter.LastRun!.AwaitingTerminal.Task;
await cancelRaceSession.CancelAsync();
await ExpectAsync<OperationCanceledException>(() => cancelRaceConsumption, string.Empty);
Assert(cancelRaceSession.State == GraphExecutionSessionState.Cancelled && !cancelRaceSession.Result.Succeeded, "A terminal event released by cancellation cannot overwrite the atomically reserved Cancelled state.");
await cancelRaceSession.DisposeAsync();

var disposalAdapter = new BlockingAdapter();
var disposalSession = await new GraphExecutionEngine(compiler, [disposalAdapter]).StartSessionAsync(new(native.Graph!, disposalAdapter.Id, identity, policy, data, activator));
var disposalConsumption = ConsumeAsync(disposalSession.StartAsync());
await disposalAdapter.LastRun!.Started.Task;
var disposalTask = disposalSession.DisposeAsync().AsTask();
await ExpectAsync<OperationCanceledException>(() => disposalConsumption, string.Empty);
await disposalTask;
Assert(disposalSession.State == GraphExecutionSessionState.Disposed && disposalAdapter.LastRun.Disposed, "Disposal cancels an active iterator, serializes teardown, and releases the run.");

var handlerNode = new DiagramNode("handler", 0, 0, Properties: [new("name", "Name", Value: Json("\"Ada\""))]);
var typedRegistration = NodeRegistration.Create<TestHandler, TestState>(simpleType, node =>
    new(node.Properties.Single(property => property.Id == "name").Value!.Value.GetString()!));
var handler = new TestHandler();
var serviceProvider = new SingleServiceProvider(handler);
var handlerResult = await typedRegistration.ResolveHandler(serviceProvider).ExecuteAsync(
    new(handlerNode, identity, policy, data, activator), typedRegistration.BindState(handlerNode));
Assert(handlerResult.Succeeded && handler.LastName == "Ada", "Typed handlers receive DI resolution and explicitly bound persisted state without graph plumbing.");
var activation = new NodeExecutionContext(handlerNode, identity, policy, data, activator).CreateComponentActivation("customer-agent");
activation.ValidateAgainst(identity);
Expect<InvalidOperationException>(() => (activation with { RunId = "other" }).ValidateAgainst(identity), "Component activation identity does not match the execution run.");

var maf = MafOrchestrationNodeSet.Descriptor;
Assert(maf.DisplayName == "MAF Orchestration" && maf.NodeTypes.Count >= 8, "MAF orchestration ships as a complete palette node set.");
Assert(maf.NodeTypes.All(type => type.Width % 16 == 0 && type.Height % 16 == 0), "Every MAF node type has grid-fitted default dimensions.");
Assert(maf.NodeTypes.Max(type => type.Width) <= 224 && maf.NodeTypes.Max(type => type.Height) <= 192, "MAF defaults remain compact while fitting their property rows.");
Assert(maf.NodeTypes.All(type => type.Ports.All(port => port.PropertyId is null || type.Properties.Any(property => property.Id == port.PropertyId))), "MAF property ports reference declared properties.");
Assert(maf.NodeTypes.Single(type => type.TypeId == "maf.join").Properties.Single(property => property.Id == "strategy").Options!.SequenceEqual(["all", "any", "quorum"]), "Join exposes usable all, any, and quorum choices.");
Assert(maf.NodeTypes.Single(type => type.TypeId == "maf.data-capture").Ports.Count(port => port.PropertyId == "value") == 2, "Data Capture exposes separate input and output property ports.");
Assert(typeof(MafOrchestrationNodeSet).Assembly.GetReferencedAssemblies().All(name => !name.Name!.Contains("Microsoft.Agents", StringComparison.OrdinalIgnoreCase)), "The MAF node set has no Microsoft Agent Framework binary dependency.");

var uml = UmlNodeSet.Descriptor;
Assert(uml.Id == "uml-basic" && uml.DisplayName == "UML", "The UML node set has a stable palette identity.");
var latestUmlTypes = uml.NodeTypes
    .GroupBy(type => type.TypeId, StringComparer.Ordinal)
    .Select(versions => versions.OrderByDescending(type => type.Version).First())
    .ToArray();
Assert(latestUmlTypes.Select(type => type.TypeId).SequenceEqual([
    "uml.class", "uml.abstract-class", "uml.interface", "uml.enumeration", "uml.data-type", "uml.object"
]), "The UML basic set exposes its six structural types in a stable order.");
Assert(uml.NodeTypes.Count == 12 && latestUmlTypes.All(type => type.Version == 2), "UML retains version 1 schemas while exposing version 2 as latest.");
Assert(uml.NodeTypes.All(type => type.Metadata.ContainsKey("description") && type.Metadata.ContainsKey("umlKind")), "Every UML descriptor carries versioned descriptive metadata.");
Assert(latestUmlTypes.All(type => type.Ports.Select(port => port.Id).SequenceEqual(["relationships-top", "relationships-right", "relationships-bottom", "relationships-left"])), "Every current UML type exposes four stable relationship ports.");
Assert(latestUmlTypes.All(type => type.Ports.Select(port => port.Anchor).SequenceEqual(["top", "right", "bottom", "left"])), "Current UML relationship ports are fixed to all four node sides.");
Assert(latestUmlTypes.All(type => type.Ports.All(port => port.PropertyId is null)), "UML relationship ports remain independent from node properties.");
Assert(latestUmlTypes.All(type => type.Width == 208 && type.Height <= 112 && type.Width % 16 == 0 && type.Height % 16 == 0), "Current UML nodes use compact grid-fitted default dimensions.");
Assert(uml.NodeTypes.SelectMany(type => type.Properties).All(property => !property.Required || property.DefaultValue is not null), "New UML nodes never begin with an unsatisfied required property.");
Assert(registry.NodeSets.Any(set => set.Id == MafOrchestrationNodeSet.Id) && registry.NodeSets.Any(set => set.Id == UmlNodeSet.Id), "MAF and UML node sets coexist in one immutable registry.");
Expect<InvalidOperationException>(() => registry.GetLatest("uml.class").ResolveHandler(new SingleServiceProvider(new object())), "Node type 'uml.class@2' has no execution handler registration.");

foreach (var type in latestUmlTypes)
{
    var nodeId = $"uml-test-{type.TypeId[4..]}";
    var umlRequest = new NodeCreationRequest(nodeId, type.TypeId, 32, 48);
    var first = factory.Create(umlRequest);
    var second = factory.Create(umlRequest);
    Assert(JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second), $"Factory creation is deterministic for '{type.TypeId}'.");
    Assert(first.Node.TypeId == type.TypeId && first.Node.TypeVersion == 2, $"Factory preserves the latest identity of '{type.TypeId}'.");
    Assert(first.Ports.Select(port => port.Id).SequenceEqual(type.Ports.Select(port => $"{nodeId}:{port.Id}")), $"Factory creates stable ports for '{type.TypeId}'.");
    Assert(first.Ports.Select(port => port.Anchor).SequenceEqual(["top", "right", "bottom", "left"]), $"Factory materializes four fixed-side anchors for '{type.TypeId}'.");
}

var legacyUmlClass = factory.Create(new("uml-legacy-class", "uml.class", 0, 0, TypeVersion: 1));
Assert(legacyUmlClass.Node.TypeVersion == 1 && legacyUmlClass.Node.Width == 260 && legacyUmlClass.Ports.Select(port => port.Id).SequenceEqual(["uml-legacy-class:relationships-in", "uml-legacy-class:relationships-out"]), "Saved UML version 1 schemas remain resolvable.");

var umlClass = factory.Create(new("uml-class-1", "uml.class", 24, 32, Label: "Customer"));
var umlInterface = factory.Create(new("uml-interface-1", "uml.interface", 360, 32, Label: "IRepository"));
var umlDocument = new DiagramDocument(
    "uml-round-trip",
    [umlClass.Node, umlInterface.Node],
    umlClass.Ports.Concat(umlInterface.Ports).ToArray(),
    [new("uml-realization", "uml-class-1:relationships-right", "uml-interface-1:relationships-left")]);
var umlJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var umlJson = JsonSerializer.Serialize(umlDocument, umlJsonOptions);
var umlRoundTrip = JsonSerializer.Deserialize<DiagramDocument>(umlJson, umlJsonOptions)
    ?? throw new InvalidOperationException("UML document did not deserialize.");
Assert(JsonSerializer.Serialize(umlRoundTrip, umlJsonOptions) == umlJson, "A UML document round-trips through the public JSON model without losing node identities or relationship ports.");
Assert(compiler.Compile(umlRoundTrip, new(GraphCompileProfile.DagOnly)).Succeeded, "The compiler accepts a round-tripped UML relationship between fixed-side ports.");
var tamperedUmlPorts = umlRoundTrip.Ports.Select(port => port.Id == "uml-class-1:relationships-right" ? port with { Anchor = "left" } : port).ToArray();
Assert(compiler.Compile(umlRoundTrip with { Ports = tamperedUmlPorts }, new(GraphCompileProfile.DagOnly)).Diagnostics.Any(diagnostic => diagnostic.Code == GraphDiagnosticCodes.NodePortSchemaMismatch), "The compiler rejects a registered UML port moved away from its declared side.");

var wildcardUmlDocument = new DiagramDocument(
    "uml-wildcard",
    [umlClass.Node, new DiagramNode("generic-target", 360, 32, Label: "Generic target")],
    umlClass.Ports.Concat([new DiagramPort("generic-target:in", "generic-target", "target", "*")]).ToArray(),
    [new("uml-to-generic", "uml-class-1:relationships-right", "generic-target:in")]);
Assert(compiler.Compile(wildcardUmlDocument, new(GraphCompileProfile.DagOnly)).Succeeded, "A UML relationship scope intentionally connects to Ghostagram's wildcard port scope.");

var umlReferences = typeof(UmlNodeSet).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
Assert(!umlReferences.Contains("Ghostagram.Server", StringComparer.Ordinal) && !umlReferences.Contains("Ghostagram.Blazor", StringComparer.Ordinal), "The UML node set has no Server or Blazor dependency.");

Console.WriteLine("Ghostagram.Execution focused checks passed.");

static DiagramDocument Document(string[] nodeIds, (string Id, string Source, string Target)[] edges)
{
    var nodes = nodeIds.Select((id, index) => new DiagramNode(id, index * 100, 0)).ToArray();
    var ports = nodeIds.SelectMany(id => new[]
    {
        new DiagramPort($"{id}-in", id, "target"),
        new DiagramPort($"{id}-out", id, "source")
    }).ToArray();
    var diagramEdges = edges.Select(edge => new DiagramEdge(edge.Id, $"{edge.Source}-out", $"{edge.Target}-in")).ToArray();
    return new("test", nodes, ports, diagramEdges);
}

static string StageSignature(GraphCompilationResult result) =>
    JsonSerializer.Serialize(result.Graph!.Stages);

static string DiagnosticSignature(GraphCompilationResult result) =>
    JsonSerializer.Serialize(result.Diagnostics);

static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

static async Task ConsumeAsync(IAsyncEnumerable<OrchestrationEvent> events)
{
    await foreach (var _ in events) { }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Expect<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
        throw new InvalidOperationException($"Expected {typeof(TException).Name}: {message}");
    }
    catch (TException exception) when (exception.Message.StartsWith(message, StringComparison.Ordinal))
    {
    }
}

static async Task ExpectAsync<TException>(Func<Task> action, string message) where TException : Exception
{
    try
    {
        await action();
        throw new InvalidOperationException($"Expected {typeof(TException).Name}: {message}");
    }
    catch (TException exception) when (exception.Message.StartsWith(message, StringComparison.Ordinal))
    {
    }
}

sealed class FakeAdapter : IOrchestrationAdapter
{
    public string Id => "fake";
    public int Calls { get; private set; }
    public OrchestrationAdapterCapabilities Capabilities { get; } = new(
        new HashSet<GraphCompileProfile>(Enum.GetValues<GraphCompileProfile>()), true, true, true, true);
    public ValueTask<IOrchestrationRun> PrepareAsync(OrchestrationPreparationRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        return ValueTask.FromResult<IOrchestrationRun>(new FakeRun(Id, request.Graph, request.Identity));
    }
}

sealed class FakeRun(string adapterId, CompiledGraph graph, ExecutionRunIdentity identity) : IOrchestrationRun
{
    public string RunId => identity.RunId;
    public string? ConversationId => identity.ConversationId;
    public string PlanFingerprint => graph.PlanFingerprint;
    public OrchestrationCheckpointReference? LatestCheckpoint { get; private set; }

    public async IAsyncEnumerable<OrchestrationEvent> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new(0, OrchestrationEventKind.RunStarted, DateTimeOffset.UtcNow);
        long sequence = 1;
        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(sequence++, OrchestrationEventKind.NodeCompleted, DateTimeOffset.UtcNow, node.Id);
        }
        LatestCheckpoint = new(adapterId, "checkpoint-1", graph.PlanFingerprint, RunId, ConversationId);
        yield return new(sequence++, OrchestrationEventKind.CheckpointCreated, DateTimeOffset.UtcNow, Checkpoint: LatestCheckpoint);
        yield return new(sequence, OrchestrationEventKind.RunCompleted, DateTimeOffset.UtcNow);
        await Task.CompletedTask;
    }

    public IAsyncEnumerable<OrchestrationEvent> ResumeAsync(OrchestrationCheckpointReference checkpoint, CancellationToken cancellationToken = default) => StartAsync(cancellationToken);
    public async IAsyncEnumerable<OrchestrationEvent> SubmitExternalResponseAsync(ExternalInputResponse response, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
    public ValueTask CancelAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class HitlAdapter : IOrchestrationAdapter
{
    public string Id => "hitl";
    public HitlRun? LastRun { get; private set; }
    public OrchestrationAdapterCapabilities Capabilities { get; } = new(
        new HashSet<GraphCompileProfile>(Enum.GetValues<GraphCompileProfile>()), true, true, true, true);
    public ValueTask<IOrchestrationRun> PrepareAsync(OrchestrationPreparationRequest request, CancellationToken cancellationToken = default)
    {
        LastRun = new(this, request);
        return ValueTask.FromResult<IOrchestrationRun>(LastRun);
    }
}

sealed class HitlRun(HitlAdapter adapter, OrchestrationPreparationRequest request) : IOrchestrationRun
{
    public string RunId => request.Identity.RunId;
    public string? ConversationId => request.Identity.ConversationId;
    public string PlanFingerprint => request.Graph.PlanFingerprint;
    public OrchestrationCheckpointReference? LatestCheckpoint { get; private set; }
    public bool Disposed { get; private set; }

    public async IAsyncEnumerable<OrchestrationEvent> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new(0, OrchestrationEventKind.RunStarted, DateTimeOffset.UtcNow);
        LatestCheckpoint = new(adapter.Id, "hitl-1", PlanFingerprint, RunId, ConversationId);
        yield return new(1, OrchestrationEventKind.CheckpointCreated, DateTimeOffset.UtcNow, Checkpoint: LatestCheckpoint);
        yield return new(2, OrchestrationEventKind.ExternalInputRequired, DateTimeOffset.UtcNow,
            ExternalInput: new("approval-1", RunId, "a", "approval", ConversationId, "Approve?"), Checkpoint: LatestCheckpoint);
        await Task.CompletedTask;
    }

    public IAsyncEnumerable<OrchestrationEvent> ResumeAsync(OrchestrationCheckpointReference checkpoint, CancellationToken cancellationToken = default) => StartAsync(cancellationToken);
    public async IAsyncEnumerable<OrchestrationEvent> SubmitExternalResponseAsync(ExternalInputResponse response, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new(3, OrchestrationEventKind.NodeCompleted, DateTimeOffset.UtcNow, "a");
        yield return new(4, OrchestrationEventKind.RunCompleted, DateTimeOffset.UtcNow);
        await Task.CompletedTask;
    }
    public ValueTask CancelAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

sealed class FaultingAdapter : IOrchestrationAdapter
{
    public string Id => "faulting";
    public OrchestrationAdapterCapabilities Capabilities { get; } = new(
        new HashSet<GraphCompileProfile>(Enum.GetValues<GraphCompileProfile>()), false, false, true, true);
    public ValueTask<IOrchestrationRun> PrepareAsync(OrchestrationPreparationRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IOrchestrationRun>(new FaultingRun(request));
}

sealed class FaultingRun(OrchestrationPreparationRequest request) : IOrchestrationRun
{
    public string RunId => request.Identity.RunId;
    public string? ConversationId => request.Identity.ConversationId;
    public string PlanFingerprint => request.Graph.PlanFingerprint;
    public OrchestrationCheckpointReference? LatestCheckpoint => null;
    public async IAsyncEnumerable<OrchestrationEvent> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new(0, OrchestrationEventKind.RunStarted, DateTimeOffset.UtcNow);
        await Task.Yield();
        throw new InvalidOperationException("adapter boom");
    }
    public IAsyncEnumerable<OrchestrationEvent> ResumeAsync(OrchestrationCheckpointReference checkpoint, CancellationToken cancellationToken = default) => StartAsync(cancellationToken);
    public IAsyncEnumerable<OrchestrationEvent> SubmitExternalResponseAsync(ExternalInputResponse response, CancellationToken cancellationToken = default) => StartAsync(cancellationToken);
    public ValueTask CancelAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class BlockingAdapter : IOrchestrationAdapter
{
    public string Id => "blocking";
    public BlockingRun? LastRun { get; private set; }
    public OrchestrationAdapterCapabilities Capabilities { get; } = new(
        new HashSet<GraphCompileProfile>(Enum.GetValues<GraphCompileProfile>()), false, false, true, true);
    public ValueTask<IOrchestrationRun> PrepareAsync(OrchestrationPreparationRequest request, CancellationToken cancellationToken = default)
    {
        LastRun = new(request);
        return ValueTask.FromResult<IOrchestrationRun>(LastRun);
    }
}

sealed class BlockingRun(OrchestrationPreparationRequest request) : IOrchestrationRun
{
    private int _cancelCalls;
    public string RunId => request.Identity.RunId;
    public string? ConversationId => request.Identity.ConversationId;
    public string PlanFingerprint => request.Graph.PlanFingerprint;
    public OrchestrationCheckpointReference? LatestCheckpoint => null;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int CancelCalls => Volatile.Read(ref _cancelCalls);
    public bool Disposed { get; private set; }
    public async IAsyncEnumerable<OrchestrationEvent> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new(0, OrchestrationEventKind.RunStarted, DateTimeOffset.UtcNow);
        Started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
    public IAsyncEnumerable<OrchestrationEvent> ResumeAsync(OrchestrationCheckpointReference checkpoint, CancellationToken cancellationToken = default) => StartAsync(cancellationToken);
    public async IAsyncEnumerable<OrchestrationEvent> SubmitExternalResponseAsync(ExternalInputResponse response, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
    public ValueTask CancelAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _cancelCalls);
        return ValueTask.CompletedTask;
    }
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

sealed class CancelRaceAdapter : IOrchestrationAdapter
{
    public string Id => "cancel-race";
    public CancelRaceRun? LastRun { get; private set; }
    public OrchestrationAdapterCapabilities Capabilities { get; } = new(
        new HashSet<GraphCompileProfile>(Enum.GetValues<GraphCompileProfile>()), false, false, true, true);
    public ValueTask<IOrchestrationRun> PrepareAsync(OrchestrationPreparationRequest request, CancellationToken cancellationToken = default)
    {
        LastRun = new(request);
        return ValueTask.FromResult<IOrchestrationRun>(LastRun);
    }
}

sealed class CancelRaceRun(OrchestrationPreparationRequest request) : IOrchestrationRun
{
    private readonly TaskCompletionSource _releaseTerminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string RunId => request.Identity.RunId;
    public string? ConversationId => request.Identity.ConversationId;
    public string PlanFingerprint => request.Graph.PlanFingerprint;
    public OrchestrationCheckpointReference? LatestCheckpoint => null;
    public TaskCompletionSource AwaitingTerminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async IAsyncEnumerable<OrchestrationEvent> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new(0, OrchestrationEventKind.RunStarted, DateTimeOffset.UtcNow);
        AwaitingTerminal.TrySetResult();
        await _releaseTerminal.Task;
        yield return new(1, OrchestrationEventKind.RunCompleted, DateTimeOffset.UtcNow);
    }
    public IAsyncEnumerable<OrchestrationEvent> ResumeAsync(OrchestrationCheckpointReference checkpoint, CancellationToken cancellationToken = default) => StartAsync(cancellationToken);
    public IAsyncEnumerable<OrchestrationEvent> SubmitExternalResponseAsync(ExternalInputResponse response, CancellationToken cancellationToken = default) => StartAsync(cancellationToken);
    public ValueTask CancelAsync(CancellationToken cancellationToken = default)
    {
        _releaseTerminal.TrySetResult();
        return ValueTask.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class FakeActivator : IExecutionComponentActivator
{
    public FakeLease? LastLease { get; private set; }
    public ValueTask<IExecutionComponentLease> AcquireAsync(ComponentActivationRequest request, CancellationToken cancellationToken = default)
    {
        LastLease = new FakeLease(request.ComponentKey);
        return ValueTask.FromResult<IExecutionComponentLease>(LastLease);
    }
}

sealed class FakeLease(string componentKey) : IExecutionComponentLease
{
    public string ComponentKey { get; } = componentKey;
    public bool Disposed { get; private set; }
    public async IAsyncEnumerable<ComponentUpdate> InvokeAsync(ComponentInvocation invocation, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new(0, "completed", invocation.Input);
        await Task.CompletedTask;
    }
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

sealed record TestState(string Name);

sealed class UnsupportedFingerprintMetadata;

sealed class TestHandler : NodeHandler<TestState>
{
    public string? LastName { get; private set; }
    public override ValueTask<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TestState state, CancellationToken cancellationToken = default)
    {
        LastName = state.Name;
        return ValueTask.FromResult(NodeExecutionResult.Success());
    }
}

sealed class SingleServiceProvider(object service) : IServiceProvider
{
    public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(service) ? service : null;
}
