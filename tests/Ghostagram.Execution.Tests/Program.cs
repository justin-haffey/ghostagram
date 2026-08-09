using System.Text.Json;
using Ghostagram.Core;
using Ghostagram.Execution;
using Ghostagram.NodeSets.Maf;

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
var registry = new NodeTypeRegistry([new("tests", "Tests", [simpleType]), MafOrchestrationNodeSet.Descriptor]);
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
Assert(created1.Ports.Select(port => port.Id).SequenceEqual(["node-1:payload-in", "node-1:next"]), "Factory creates stable property-port identifiers.");
Assert(created1.Ports[0].PropertyId == "payload", "Factory preserves property-to-port association.");
Assert(created1.Node.Properties.Single(property => property.Id == "payload").Value?.GetProperty("id").GetInt32() == 7, "Factory preserves custom property values.");
Expect<ArgumentException>(() => factory.Create(request with { PropertyValues = new Dictionary<string, JsonElement?> { ["unknown"] = null } }), "Property 'unknown' is not defined by node type 'test.node@1'.");

var compiler = new GraphCompiler(registry);
var dag = Document(
    ["a", "b", "c"],
    [("ab", "a", "b"), ("ac", "a", "c")]);
var dagResult = compiler.Compile(dag, new(GraphCompileProfile.DagOnly));
Assert(dagResult.Succeeded, "DAG compilation succeeds.");
Assert(dagResult.Graph!.PlanFingerprint.Length == 64, "Compiled plans have a stable SHA-256 fingerprint.");
Assert(dagResult.Graph!.Stages.Select(stage => stage.Order).Distinct().Count() == 2, "DAG compiler emits concurrent topological stage orders.");
Assert(dagResult.Graph.Stages.Count == 2, "Ready components are grouped into deterministic concurrent stage buckets.");
Assert(dagResult.Graph.Stages.Where(stage => stage.Order == 1).SelectMany(stage => stage.NodeIds).SequenceEqual(["b", "c"]), "Independent successors share the same concurrent stage order.");

var cycle = Document(["a", "b"], [("ab", "a", "b"), ("ba", "b", "a")]);
var rejectedCycle = compiler.Compile(cycle, new(GraphCompileProfile.DagOnly));
Assert(!rejectedCycle.Succeeded, "DAG compilation rejects cycles.");
Assert(rejectedCycle.Diagnostics.Single().Code == GraphDiagnosticCodes.CycleNotAllowed, "Cycle rejection has a stable diagnostic code.");
Assert(rejectedCycle.Diagnostics.Single().Message == "DAG compilation does not allow cycle: a -> b -> a.", "Cycle diagnostics contain an exact deterministic path.");

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
var invalidExplicitGuard = compiler.Compile(cycle, new(GraphCompileProfile.BoundedCycles, [new("a", 5)]));
Assert(invalidExplicitGuard.Diagnostics.Any(diagnostic => diagnostic.Code == GraphDiagnosticCodes.CycleGuardInvalid), "Explicit guards must identify registered loop-controller nodes.");
var inferredGuard = compiler.Compile(controlledCycle, new(GraphCompileProfile.BoundedCycles));
Assert(inferredGuard.Succeeded && inferredGuard.Graph!.Stages.Single().LoopGuards!.Single() == new LoopGuard("a", 3), "Persisted loop-guard nodes compile without a duplicate hidden option.");
var native = compiler.Compile(cycle, new(GraphCompileProfile.AdapterNative));
Assert(native.Succeeded, "Adapter-native compilation permits agentic loops.");

var propertyChanged = dag with { Nodes = dag.Nodes.Select(node => node.Id == "a" ? node with { Properties = [new("value", "Value", DiagramPropertyTypes.Integer, Json("1"))] } : node).ToArray() };
var changedFingerprint = compiler.Compile(propertyChanged, new(GraphCompileProfile.DagOnly)).Graph!.PlanFingerprint;
Assert(changedFingerprint != dagResult.Graph.PlanFingerprint, "Execution property changes invalidate plan fingerprints and stale checkpoints.");

var duplicatePropertyNode = new DiagramNode("bad", 0, 0, Properties: [new("x", "X"), new("x", "X again")]);
var duplicateProperty = compiler.Compile(new("bad", [duplicatePropertyNode], [], []), new(GraphCompileProfile.DagOnly));
Assert(duplicateProperty.Diagnostics.Single().Code == GraphDiagnosticCodes.DuplicateProperty, "Compiler rejects imported duplicate property IDs.");
var missingProperty = compiler.Compile(new("bad-port", [new DiagramNode("bad", 0, 0)], [new DiagramPort("p", "bad", PropertyId: "missing")], []), new(GraphCompileProfile.DagOnly));
Assert(missingProperty.Diagnostics.Single().Code == GraphDiagnosticCodes.PortPropertyMissing, "Compiler rejects ports attached to missing properties.");
var invalidPortDirection = compiler.Compile(new("bad-direction", [new DiagramNode("n", 0, 0)], [new DiagramPort("p", "n", "sideways")], []), new(GraphCompileProfile.DagOnly));
Assert(invalidPortDirection.Diagnostics.Single().Code == GraphDiagnosticCodes.PortDirectionInvalid, "Compiler rejects invalid imported port directions.");
var reversedEdge = compiler.Compile(new("reversed", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0)], [new DiagramPort("a-in", "a", "target"), new DiagramPort("b-out", "b", "source")], [new DiagramEdge("e", "a-in", "b-out")]), new(GraphCompileProfile.DagOnly));
Assert(reversedEdge.Diagnostics.Single().Code == GraphDiagnosticCodes.EdgeDirectionInvalid, "Compiler rejects reversed edge topology.");
var duplicateEdges = compiler.Compile(new("duplicate-edge", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0)], [new DiagramPort("a-out", "a", "source"), new DiagramPort("b-in", "b", "target")], [new DiagramEdge("e", "a-out", "b-in"), new DiagramEdge("e", "a-out", "b-in")]), new(GraphCompileProfile.DagOnly));
Assert(duplicateEdges.Diagnostics.Single().Code == GraphDiagnosticCodes.DuplicateEdge, "Compiler rejects duplicate edge IDs.");
var scopeMismatch = compiler.Compile(new("scope", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0)], [new DiagramPort("a-out", "a", "source", "alpha"), new DiagramPort("b-in", "b", "target", "beta")], [new DiagramEdge("e", "a-out", "b-in")]), new(GraphCompileProfile.DagOnly));
Assert(scopeMismatch.Diagnostics.Single().Code == GraphDiagnosticCodes.EdgeScopeMismatch, "Compiler rejects incompatible port scopes.");
var overCapacity = compiler.Compile(new("capacity", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0), new DiagramNode("c", 200, 0)], [new DiagramPort("a-out", "a", "source", MaxConnections: 1), new DiagramPort("b-in", "b", "target"), new DiagramPort("c-in", "c", "target")], [new DiagramEdge("ab", "a-out", "b-in"), new DiagramEdge("ac", "a-out", "c-in")]), new(GraphCompileProfile.DagOnly));
Assert(overCapacity.Diagnostics.Single().Code == GraphDiagnosticCodes.PortCapacityExceeded, "Compiler enforces MaxConnections capacity.");
var denyPolicy = new DiagramConnectionPolicy(DenyNodeIds: ["b"]);
var policyViolation = compiler.Compile(new("policy", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0)], [new DiagramPort("a-out", "a", "source", ConnectionPolicy: denyPolicy), new DiagramPort("b-in", "b", "target")], [new DiagramEdge("e", "a-out", "b-in")]), new(GraphCompileProfile.DagOnly));
Assert(policyViolation.Diagnostics.Single().Code == GraphDiagnosticCodes.ConnectionPolicyViolation, "Compiler enforces allow and deny connection policies.");
var emptyAllowPolicy = new DiagramConnectionPolicy(AllowPortIds: []);
var emptyAllowViolation = compiler.Compile(new("empty-allow", [new DiagramNode("a", 0, 0), new DiagramNode("b", 100, 0)], [new DiagramPort("a-out", "a", "source", ConnectionPolicy: emptyAllowPolicy), new DiagramPort("b-in", "b", "target")], [new DiagramEdge("e", "a-out", "b-in")]), new(GraphCompileProfile.DagOnly));
Assert(emptyAllowViolation.Diagnostics.Single().Code == GraphDiagnosticCodes.ConnectionPolicyViolation, "A defined empty allow-list denies every connection, matching the browser runtime.");
var samePortSelfLoop = compiler.Compile(new("same-port-loop", [new DiagramNode("a", 0, 0)], [new DiagramPort("a-both", "a", "both", MaxConnections: 1)], [new DiagramEdge("loop", "a-both", "a-both")]), new(GraphCompileProfile.AdapterNative));
Assert(samePortSelfLoop.Succeeded, "A same-port self-loop consumes one MaxConnections slot, matching the browser runtime.");
var registeredDocument = new DiagramDocument("registered", [created1.Node], created1.Ports, []);
Assert(compiler.Compile(registeredDocument, new(GraphCompileProfile.DagOnly)).Succeeded, "Factory-created registered ports satisfy descriptor schema.");
var tamperedRegisteredPorts = created1.Ports.Select((port, index) => index == 0 ? port with { Label = "Tampered" } : port).ToArray();
var portSchemaMismatch = compiler.Compile(registeredDocument with { Ports = tamperedRegisteredPorts }, new(GraphCompileProfile.DagOnly));
Assert(portSchemaMismatch.Diagnostics.Single().Code == GraphDiagnosticCodes.NodePortSchemaMismatch, "Compiler rejects registered node ports that drift from their descriptor.");
var missingRequired = compiler.Compile(new("required", [new DiagramNode("n", 0, 0, Properties: [new("name", "Name", Required: true)])], [], []), new(GraphCompileProfile.DagOnly));
Assert(missingRequired.Diagnostics.Single().Code == GraphDiagnosticCodes.RequiredPropertyMissing, "Compiler rejects missing required execution properties.");
var invalidPrimitive = compiler.Compile(new("invalid", [new DiagramNode("n", 0, 0, Properties: [new("count", "Count", DiagramPropertyTypes.Integer, Json("\"many\""))])], [], []), new(GraphCompileProfile.DagOnly));
Assert(invalidPrimitive.Diagnostics.Single().Code == GraphDiagnosticCodes.PropertyValueInvalid, "Compiler rejects incompatible built-in primitive values.");
var invalidEnum = compiler.Compile(new("invalid-enum", [new DiagramNode("n", 0, 0, Properties: [new("choice", "Choice", DiagramPropertyTypes.Enum, Json("\"other\""), Options: ["one", "two"])])], [], []), new(GraphCompileProfile.DagOnly));
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
Assert(maf.NodeTypes.All(type => type.Ports.All(port => port.PropertyId is null || type.Properties.Any(property => property.Id == port.PropertyId))), "MAF property ports reference declared properties.");
Assert(maf.NodeTypes.Single(type => type.TypeId == "maf.join").Properties.Single(property => property.Id == "strategy").Options!.SequenceEqual(["all", "any", "quorum"]), "Join exposes usable all, any, and quorum choices.");
Assert(maf.NodeTypes.Single(type => type.TypeId == "maf.data-capture").Ports.Count(port => port.PropertyId == "value") == 2, "Data Capture exposes separate input and output property ports.");
Assert(typeof(MafOrchestrationNodeSet).Assembly.GetReferencedAssemblies().All(name => !name.Name!.Contains("Microsoft.Agents", StringComparison.OrdinalIgnoreCase)), "The MAF node set has no Microsoft Agent Framework binary dependency.");

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
