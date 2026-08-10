using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Ghostagram.Server;
using Ghostagram.Server.Export;
using Ghostagram.Server.Layout;
using Ghostagram.Server.Mcp;
using Ghostagram.Server.Sessions;

var checks = new List<(string Name, Action Check)>
{
    ("deterministic output", VerifyDeterminism),
    ("directed flow and crossing monotonicity", VerifyDirectedFlow),
    ("nested compound containment", VerifyNestedGroups),
    ("group removal preserves and ungroups content", VerifyGroupRemoval),
    ("explicit group assignment supports reparenting and ungrouping", VerifyGroupAssignment),
    ("server rejects duplicate ids and cyclic group hierarchies", VerifyReducerStructuralValidation),
    ("server validates progressive node sections, editors, and presentation", VerifyProgressiveNodeValidation),
    ("property ports require an existing property on their node", VerifyPropertyPortValidation),
    ("cycles, components, and non-overlap", VerifyCyclesAndComponents),
    ("large sparse graph performance", VerifyPerformance),
    ("SVG preserves properties, property ports, and connector geometry", VerifyEnhancedSvgExport),
    ("server SVG mirrors progressive node presentation", VerifyProgressiveSvgExport),
    ("authoring capabilities expose the versioned operation schema", VerifyAuthoringCapabilities),
    ("browser presence tracks successful rendered revisions", () => VerifyDocumentChangeNotifier().GetAwaiter().GetResult()),
    ("layout, collaborative session, export, and replay", () => VerifyCommandPipeline().GetAwaiter().GetResult())
};

foreach (var (name, check) in checks)
{
    var timer = Stopwatch.StartNew();
    check();
    Console.WriteLine($"PASS {name} ({timer.Elapsed.TotalMilliseconds:F1} ms)");
}

static void VerifyDeterminism()
{
    var model = Model(
        [Node("a", 300, 200), Node("b", 0, 300), Node("c", 500, 20), Node("d", 50, 50)],
        [Edge("a", "c"), Edge("a", "d"), Edge("b", "c"), Edge("b", "d")]);
    var strategy = new GhostLayeredLayoutStrategy();
    var first = strategy.Compute(new("determinism", 4, model), new(CrossingSweeps: 12), 713, default);
    var second = strategy.Compute(new("determinism", 4, model), new(CrossingSweeps: 12), 713, default);
    Equal(JsonSerializer.Serialize(first.Operations), JsonSerializer.Serialize(second.Operations), "same input, options, version, and seed must produce byte-equivalent operations");
    True(first.Metrics.EstimatedCrossingsAfter <= first.Metrics.EstimatedCrossingsBefore, "crossing reduction must never retain a worse ordering");
}

static void VerifyDirectedFlow()
{
    var model = Model(
        [Node("start", 500, 400), Node("left", 100, 20), Node("right", 50, 500), Node("finish", 0, 0)],
        [Edge("start", "left"), Edge("start", "right"), Edge("left", "finish"), Edge("right", "finish")]);
    var result = new GhostLayeredLayoutStrategy().Compute(new("flow", 0, model), new(Direction: "right"), 0, default);
    var laidOut = GhostagramDocumentReducer.Apply(model, result.Operations);
    var boxes = Boxes(laidOut, "nodes");
    True(boxes["start"].X < boxes["left"].X && boxes["start"].X < boxes["right"].X, "source must precede the middle layer");
    True(boxes["left"].X < boxes["finish"].X && boxes["right"].X < boxes["finish"].X, "middle layer must precede the sink");
    True(result.Metrics.EstimatedCrossingsAfter <= result.Metrics.EstimatedCrossingsBefore, "crossings must be monotonic");
}

static void VerifyNestedGroups()
{
    var model = Model(
        [Node("review", 900, 700, "inner"), Node("approve", 100, 100, "outer"), Node("outside", 0, 0)],
        [Edge("outside", "review"), Edge("review", "approve")],
        [Group("outer", null), Group("inner", "outer")]);
    var result = new GhostLayeredLayoutStrategy().Compute(new("groups", 0, model), new(GroupPadding: 36, GroupHeader: 30), 11, default);
    var laidOut = GhostagramDocumentReducer.Apply(model, result.Operations);
    var nodes = Boxes(laidOut, "nodes");
    var groups = Boxes(laidOut, "groups");
    Contains(groups["inner"], nodes["review"], "inner group must contain its node");
    Contains(groups["outer"], groups["inner"], "outer group must contain the nested group");
    Contains(groups["outer"], nodes["approve"], "outer group must contain its direct node");
}

static void VerifyGroupRemoval()
{
    var model = Model(
        [Node("direct", 20, 20, "outer"), Node("nested", 60, 60, "inner")],
        [],
        [Group("outer", null), Group("inner", "outer")]);
    var removed = GhostagramDocumentReducer.Apply(model, [DiagramOperations.RemoveGroup("outer")]);
    var nodes = removed.GetProperty("nodes").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetString()!);
    var groups = removed.GetProperty("groups").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetString()!);
    True(!groups.ContainsKey("outer"), "the removed group must no longer exist");
    True(nodes["direct"].GetProperty("groupId").ValueKind == JsonValueKind.Null, "direct members must become ungrouped");
    True(groups["inner"].GetProperty("parentGroupId").ValueKind == JsonValueKind.Null, "child groups must become top-level");
    Equal("inner", nodes["nested"].GetProperty("groupId").GetString()!, "nested members must retain their direct group");
}

static void VerifyGroupAssignment()
{
    var model = Model(
        [Node("custom", 20, 20, "left")],
        [],
        [Group("left", null), Group("right", null)]);
    var moved = GhostagramDocumentReducer.Apply(model,
    [
        DiagramOperations.Create("group.assignNode", new { nodeId = "custom", groupId = "right" }),
        DiagramOperations.Create("group.assignNode", new { nodeId = "custom", groupId = (string?)null })
    ]);
    var custom = moved.GetProperty("nodes").EnumerateArray().Single();
    True(custom.GetProperty("groupId").ValueKind == JsonValueKind.Null, "an explicit null assignment must remove group membership");
}

static void VerifyPropertyPortValidation()
{
    var model = Model([Node("property-node", 20, 20)], []);
    var invalidPort = DiagramOperations.Create("port.upsert", new
    {
        id = "property-node-value",
        nodeId = "property-node",
        direction = "source",
        propertyId = "missing"
    });
    try
    {
        GhostagramDocumentReducer.Apply(model, [invalidPort]);
        throw new InvalidOperationException("A dangling property port was accepted.");
    }
    catch (DiagramCommandException exception)
    {
        Equal("MISSING_REFERENCE", exception.Code, "dangling property ports must fail with a reference diagnostic");
    }
}

static void VerifyReducerStructuralValidation()
{
    var duplicate = Model([Node("same", 0, 0), Node("same", 100, 0)], []);
    ExpectDiagramError(duplicate, [], "DUPLICATE_ID", "duplicate node IDs must be rejected");

    var cyclic = Model([], [], [Group("outer", "inner"), Group("inner", "outer")]);
    ExpectDiagramError(cyclic, [], "INVALID_GROUP_HIERARCHY", "cyclic group parents must be rejected");
}

static void VerifyProgressiveNodeValidation()
{
    var missingSection = ProgressiveModel(new
    {
        id = "missing-section",
        x = 0,
        y = 0,
        width = 220,
        height = 100,
        properties = new[] { new { id = "name", name = "Name", type = "string", sectionId = "unknown" } },
        sections = Array.Empty<object>()
    });
    ExpectDiagramError(missingSection, [], "MISSING_REFERENCE", "property section references must be validated");

    var cyclicSections = ProgressiveModel(new
    {
        id = "section-cycle",
        x = 0,
        y = 0,
        width = 220,
        height = 100,
        properties = Array.Empty<object>(),
        sections = new[]
        {
            new { id = "first", title = "First", parentSectionId = "second", order = 0, collapsible = true },
            new { id = "second", title = "Second", parentSectionId = "first", order = 1, collapsible = true }
        }
    });
    ExpectDiagramError(cyclicSections, [], "INVALID_SECTION_HIERARCHY", "section parent cycles must be rejected");

    var invalidEditor = ProgressiveModel(new
    {
        id = "invalid-editor",
        x = 0,
        y = 0,
        width = 220,
        height = 100,
        properties = new[] { new { id = "enabled", name = "Enabled", type = "boolean", editor = new { kind = "range", minimum = 0, maximum = 1 } } },
        sections = Array.Empty<object>()
    });
    ExpectDiagramError(invalidEditor, [], "INVALID_MODEL", "editor and property types must be compatible");

    var nonCollapsible = ProgressiveModel(new
    {
        id = "invalid-presentation",
        x = 0,
        y = 0,
        width = 220,
        height = 100,
        properties = Array.Empty<object>(),
        sections = new[] { new { id = "fixed", title = "Fixed", order = 0, collapsible = false } },
        presentation = new { displayMode = "expanded", expandedHeight = 100, collapsedSectionIds = new[] { "fixed" } }
    });
    ExpectDiagramError(nonCollapsible, [], "INVALID_MODEL", "non-collapsible sections cannot be persisted as collapsed");
}

static JsonElement ProgressiveModel(object node) => JsonSerializer.SerializeToElement(new
{
    nodes = new[] { node },
    ports = Array.Empty<object>(),
    edges = Array.Empty<object>(),
    groups = Array.Empty<object>(),
    edgeTypes = Array.Empty<object>(),
    selection = Array.Empty<string>(),
    viewport = new { x = 0, y = 0, zoom = 1 }
});

static void ExpectDiagramError(JsonElement model, IReadOnlyList<GhostagramOperation> operations, string code, string message)
{
    try
    {
        GhostagramDocumentReducer.Apply(model, operations);
        throw new InvalidOperationException(message);
    }
    catch (DiagramCommandException exception)
    {
        Equal(code, exception.Code, message);
    }
}

static void VerifyCyclesAndComponents()
{
    var model = Model(
        [Node("a", 0, 0), Node("b", 0, 0), Node("c", 0, 0), Node("d", 0, 0), Node("e", 0, 0), Node("isolated", 0, 0)],
        [Edge("a", "b"), Edge("b", "c"), Edge("c", "a"), Edge("d", "e")]);
    var result = new GhostLayeredLayoutStrategy().Compute(new("cycles", 0, model), new(), 97, default);
    var boxes = Boxes(GhostagramDocumentReducer.Apply(model, result.Operations), "nodes").Values.ToArray();
    for (var left = 0; left < boxes.Length; left++)
        for (var right = left + 1; right < boxes.Length; right++)
            True(!Overlaps(boxes[left], boxes[right]), $"nodes {boxes[left].Id} and {boxes[right].Id} must not overlap");
    True(result.Metrics.CyclicComponentCount >= 1, "the directed cycle must be reported");
    True(result.Metrics.ComponentCount >= 3, "disconnected and isolated components must be reported");
}

static void VerifyPerformance()
{
    const int count = 5_000;
    var nodes = Enumerable.Range(0, count).Select(index => Node($"n{index:D4}", index % 20 * 100, index / 20 * 70)).ToArray();
    var edges = new List<(string Source, string Target)>();
    for (var index = 0; index < count - 1; index++) edges.Add(($"n{index:D4}", $"n{index + 1:D4}"));
    for (var index = 0; index < count - 17; index += 2) edges.Add(($"n{index:D4}", $"n{index + 17:D4}"));
    var model = Model(nodes, edges);
    var timer = Stopwatch.StartNew();
    var result = new GhostLayeredLayoutStrategy().Compute(new("large", 0, model), new(CrossingSweeps: 4), 1234, default);
    timer.Stop();
    Equal(count, result.Metrics.NodeCount, "all nodes must be included");
    True(timer.Elapsed < TimeSpan.FromSeconds(5), $"5,000-node sparse graph layout took {timer.Elapsed.TotalMilliseconds:F0} ms");
}

static void VerifyEnhancedSvgExport()
{
    var model = new JsonObject
    {
        ["nodes"] = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "source", ["label"] = "Agent", ["icon"] = "mdi:robot-outline",
                ["x"] = 0, ["y"] = 0, ["width"] = 180, ["height"] = 100,
                ["properties"] = new JsonArray
                {
                    new JsonObject { ["id"] = "prompt", ["name"] = "prompt", ["label"] = "Prompt", ["value"] = "Hello" }
                }
            },
            new JsonObject { ["id"] = "target", ["label"] = "Output", ["x"] = 300, ["y"] = 0, ["width"] = 160, ["height"] = 80 }
        },
        ["ports"] = new JsonArray
        {
            new JsonObject { ["id"] = "source-prompt", ["nodeId"] = "source", ["direction"] = "source", ["propertyId"] = "prompt", ["order"] = 0 },
            new JsonObject { ["id"] = "source-next", ["nodeId"] = "source", ["direction"] = "source", ["order"] = 1 },
            new JsonObject { ["id"] = "target-in", ["nodeId"] = "target", ["direction"] = "target", ["anchor"] = "left" }
        },
        ["edges"] = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "edge", ["sourcePortId"] = "source-prompt", ["targetPortId"] = "target-in", ["connector"] = "bezier",
                ["overlays"] = new JsonArray
                {
                    new JsonObject { ["type"] = "diamond-open", ["location"] = 0 },
                    new JsonObject { ["type"] = "erd-zero-many", ["location"] = 1 }
                }
            },
            new JsonObject
            {
                ["id"] = "typed-edge", ["sourcePortId"] = "source-next", ["targetPortId"] = "target-in", ["type"] = "uml-aggregation"
            }
        },
        ["groups"] = new JsonArray(),
        ["edgeTypes"] = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "uml-aggregation", ["connector"] = "straight",
                ["overlays"] = new JsonArray
                {
                    new JsonObject { ["type"] = "diamond-open", ["location"] = 0 },
                    new JsonObject { ["type"] = "plain-arrow", ["location"] = 1 }
                }
            }
        }
    };
    var artifact = new SvgDiagramExporter().Export(new("svg", 3, JsonSerializer.SerializeToElement(model)));
    True(artifact.Content.Contains(">Prompt</text>", StringComparison.Ordinal), "property label must be exported");
    True(artifact.Content.Contains(">Hello</text>", StringComparison.Ordinal), "property value must be exported");
    True(artifact.Content.Contains(" C ", StringComparison.Ordinal), "Bezier connector must remain curved in SVG");
    True(artifact.Content.Contains("M 180 40 C", StringComparison.Ordinal), "property-bound edge must originate at the rendered property row");
    True(artifact.Content.Contains("data-port-id=\"source-prompt\"", StringComparison.Ordinal), "property-bound port must be exported");
    True(artifact.Content.Contains("data-port-id=\"source-prompt\" cx=\"180\" cy=\"40\" data-port-kind=\"property\"><rect", StringComparison.Ordinal), "property-bound ports must export as field sockets");
    True(artifact.Content.Contains("data-port-id=\"source-next\" cx=\"180\" cy=\"61\" data-port-kind=\"node\"", StringComparison.Ordinal), "ordinary node ports must remain circular connectors");
    True(artifact.Content.Contains("data-port-id=\"source-next\" cx=\"180\" cy=\"61\"", StringComparison.Ordinal), "ordered complex-node ports must retain their row slot instead of using node height");
    True(artifact.Content.Contains("marker-start=\"url(#gp-diamond-open)\"", StringComparison.Ordinal), "server SVG must preserve the selected UML source marker");
    True(artifact.Content.Contains("marker-end=\"url(#gp-erd-zero-many)\"", StringComparison.Ordinal), "server SVG must preserve the selected crow's-foot target marker");
    True(artifact.Content.Contains("id=\"gp-erd-zero-many\"", StringComparison.Ordinal) && artifact.Content.Contains("<circle cx=\"9\"", StringComparison.Ordinal), "server SVG must define the composite zero-to-many marker");
    True(artifact.Content.Contains("data-edge-id=\"typed-edge\"><path d=\"M 180 61 L 300 40\"", StringComparison.Ordinal), "server SVG must inherit connector geometry from a reusable edge type");
    True(artifact.Content.Contains("data-edge-id=\"typed-edge\"><path", StringComparison.Ordinal)
        && artifact.Content.Contains("marker-start=\"url(#gp-diamond-open)\" marker-end=\"url(#gp-plain-arrow)\"", StringComparison.Ordinal),
        "server SVG must inherit both endpoint markers from a reusable edge type");
}

static void VerifyProgressiveSvgExport()
{
    static JsonObject ProgressiveNode(string id, string displayMode = "expanded", JsonArray? collapsedSectionIds = null) => new()
    {
        ["id"] = id, ["label"] = id, ["x"] = 0, ["y"] = 0, ["width"] = 240,
        ["height"] = displayMode == "collapsed" ? 30 : 190,
        ["sections"] = new JsonArray
        {
            new JsonObject { ["id"] = "identity", ["title"] = "Identity", ["order"] = 10 },
            new JsonObject { ["id"] = "preferences", ["title"] = "Preferences", ["order"] = 20 },
            new JsonObject { ["id"] = "advanced", ["title"] = "Advanced", ["parentSectionId"] = "preferences", ["order"] = 10 }
        },
        ["properties"] = new JsonArray
        {
            new JsonObject { ["id"] = "name", ["name"] = "Name", ["value"] = "Ada", ["sectionId"] = "identity" },
            new JsonObject { ["id"] = "theme", ["name"] = "Theme", ["value"] = "Dark", ["sectionId"] = "preferences" },
            new JsonObject
            {
                ["id"] = "confidence", ["name"] = "Confidence", ["value"] = 0.8, ["type"] = "decimal", ["sectionId"] = "advanced",
                ["editor"] = new JsonObject { ["kind"] = "range" }
            }
        },
        ["presentation"] = new JsonObject
        {
            ["displayMode"] = displayMode, ["expandedHeight"] = 190,
            ["collapsedSectionIds"] = collapsedSectionIds ?? new JsonArray()
        }
    };

    static string Export(JsonObject node, params JsonObject[] ports)
    {
        var model = new JsonObject
        {
            ["nodes"] = new JsonArray(node),
            ["ports"] = new JsonArray(ports.Cast<JsonNode?>().ToArray()),
            ["edges"] = new JsonArray(), ["groups"] = new JsonArray(), ["edgeTypes"] = new JsonArray()
        };
        return new SvgDiagramExporter().Export(new("progressive", 1, JsonSerializer.SerializeToElement(model))).Content;
    }

    var simple = Export(new JsonObject { ["id"] = "Simple", ["label"] = "Simple", ["x"] = 0, ["y"] = 0, ["width"] = 120, ["height"] = 60 });
    True(simple.Contains("<text x=\"12\" y=\"20\"", StringComparison.Ordinal) && simple.Contains(">Simple</text>", StringComparison.Ordinal),
        "simple-node titles must export in the top header rather than vertically centered");

    var nested = Export(
        ProgressiveNode("Nested"),
        new JsonObject { ["id"] = "confidence-port", ["nodeId"] = "Nested", ["direction"] = "source", ["propertyId"] = "confidence" });
    True(nested.Contains("data-section-id=\"advanced\" x=\"16\" y=\"133\"", StringComparison.Ordinal),
        "nested section headings must use the runtime depth and cursor projection");
    True(nested.Contains("data-port-id=\"confidence-port\" cx=\"240\" cy=\"155\"", StringComparison.Ordinal),
        "expanded range property ports must anchor at the projected 28px row center");

    var compactNode = ProgressiveNode("Compact", "compact");
    compactNode["sections"] = new JsonArray(new JsonObject { ["id"] = "details", ["title"] = "Details" });
    compactNode["properties"] = new JsonArray(new JsonObject
    {
        ["id"] = "note", ["name"] = "Note", ["value"] = "Text", ["sectionId"] = "details",
        ["editor"] = new JsonObject { ["kind"] = "multiline" }
    });
    var compact = Export(compactNode, new JsonObject { ["id"] = "compact-note", ["nodeId"] = "Compact", ["direction"] = "source", ["propertyId"] = "note" });
    True(compact.Contains("data-port-id=\"compact-note\" cx=\"240\" cy=\"62\"", StringComparison.Ordinal),
        "compact mode must reduce every projected property row to 18px");

    var collapsedSection = Export(
        ProgressiveNode("SectionCollapsed", collapsedSectionIds: new JsonArray("preferences")),
        new JsonObject { ["id"] = "section-proxy", ["nodeId"] = "SectionCollapsed", ["direction"] = "source", ["propertyId"] = "confidence" });
    True(collapsedSection.Contains("data-section-id=\"preferences\" x=\"8\" y=\"89\"", StringComparison.Ordinal)
        && !collapsedSection.Contains("data-section-id=\"advanced\"", StringComparison.Ordinal),
        "collapsing a section must omit its nested headings and property content");
    True(collapsedSection.Contains("data-port-id=\"section-proxy\" cx=\"240\" cy=\"85\"", StringComparison.Ordinal),
        "a hidden descendant property port must proxy to its collapsed section header center");

    var collapsedNode = Export(
        ProgressiveNode("Collapsed", "collapsed"),
        new JsonObject { ["id"] = "node-proxy", ["nodeId"] = "Collapsed", ["direction"] = "target", ["propertyId"] = "confidence" });
    True(!collapsedNode.Contains("data-section-id=", StringComparison.Ordinal) && collapsedNode.Contains("data-port-id=\"node-proxy\" cx=\"0\" cy=\"15\"", StringComparison.Ordinal),
        "a collapsed node must omit its body and proxy property ports to the node header center");

    var reducerModel = new JsonObject
    {
        ["nodes"] = new JsonArray(ProgressiveNode("Preserved")), ["ports"] = new JsonArray(), ["edges"] = new JsonArray(),
        ["groups"] = new JsonArray(), ["edgeTypes"] = new JsonArray()
    };
    var reduced = GhostagramDocumentReducer.Apply(
        JsonSerializer.SerializeToElement(reducerModel),
        [new GhostagramOperation("node.upsert", JsonSerializer.SerializeToElement(new { id = "Preserved", label = "Updated" }))]);
    var preservedNode = reduced.GetProperty("nodes")[0];
    True(preservedNode.GetProperty("sections").GetArrayLength() == 3
        && preservedNode.GetProperty("presentation").GetProperty("expandedHeight").GetInt32() == 190
        && preservedNode.GetProperty("properties")[2].GetProperty("sectionId").GetString() == "advanced",
        "partial server node upserts must preserve progressive fields that are not part of the update");
}

static void VerifyAuthoringCapabilities()
{
    var capabilities = new GhostagramCapabilityCatalog().Describe();
    Equal("1.0.0", capabilities.AuthoringSchemaVersion, "capability result must expose the embedded schema version");
    True(capabilities.Operations.Contains("edgeType.upsert"), "capabilities must advertise reusable edge type authoring");
    True(capabilities.Overlays.Contains("triangle-open") && capabilities.Overlays.Contains("erd-zero-many"), "capabilities must advertise UML and ERD markers");
    var definitions = capabilities.AuthoringSchema.GetProperty("$defs");
    True(definitions.TryGetProperty("diagramDocument", out _), "schema must define complete documents");
    True(definitions.GetProperty("operation").GetProperty("oneOf").GetArrayLength() >= 10, "schema must define the supported operation union");
}

static async Task VerifyDocumentChangeNotifier()
{
    var notifier = new DocumentChangeNotifier();
    var delivered = 0;
    using var current = notifier.Subscribe("live", 3, (change, _) => { delivered++; return Task.CompletedTask; });
    var initial = notifier.GetPresence("live");
    True(initial.ViewCount == 1 && initial.OldestRevision == 3 && initial.LatestRevision == 3, "subscription must advertise its initial rendered revision");

    var change = new DiagramChange("live", 4, "agent", "command-4", [], DateTimeOffset.UtcNow);
    await notifier.NotifyAsync(change, default);
    var advanced = notifier.GetPresence("live");
    True(delivered == 1 && advanced.OldestRevision == 4 && advanced.LatestRevision == 4, "successful delivery must advance the browser acknowledgement");

    using var stale = notifier.Subscribe("live", 2, (_, _) => throw new InvalidOperationException("render failed"));
    await notifier.NotifyAsync(change with { Revision = 5, CommandId = "command-5" }, default);
    var mixed = notifier.GetPresence("live");
    True(mixed.ViewCount == 2 && mixed.OldestRevision == 2 && mixed.LatestRevision == 5, "a failed renderer must remain visibly stale without failing the durable commit");
    stale.Dispose();
    var recovered = notifier.GetPresence("live");
    True(recovered.ViewCount == 1 && recovered.OldestRevision == 5, "disposing a failed browser must remove stale presence");

    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var rendered = new System.Collections.Concurrent.ConcurrentQueue<long>();
    using var slow = notifier.Subscribe("coalesced", 0, async (next, _) =>
    {
        rendered.Enqueue(next.Revision);
        if (next.Revision == 1)
        {
            started.SetResult();
            await release.Task;
        }
    });
    var timer = Stopwatch.StartNew();
    notifier.Notify(change with { DocumentId = "coalesced", Revision = 1, CommandId = "coalesced-1" });
    timer.Stop();
    True(timer.Elapsed < TimeSpan.FromMilliseconds(50), "a slow browser must not delay the authoritative publisher");
    await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
    notifier.Notify(change with { DocumentId = "coalesced", Revision = 2, CommandId = "coalesced-2" });
    notifier.Notify(change with { DocumentId = "coalesced", Revision = 3, CommandId = "coalesced-3" });
    release.SetResult();
    var deadline = Stopwatch.StartNew();
    while (notifier.GetPresence("coalesced").LatestRevision < 3 && deadline.Elapsed < TimeSpan.FromSeconds(1))
        await Task.Delay(5);
    Equal("1,3", string.Join(',', rendered), "a slow view must finish its current render then coalesce the burst to the latest revision");
    True(notifier.GetPresence("coalesced").LatestRevision == 3, "coalesced delivery must acknowledge the latest successfully rendered revision");
}

static async Task VerifyCommandPipeline()
{
    var model = Model([Node("a", 0, 0), Node("b", 0, 0)], [Edge("a", "b")]);
    var store = new MemoryStore(new StoredDocument { DocumentId = "pipeline", Revision = 0, Model = model });
    var publisher = new RecordingPublisher();
    var commands = new DiagramCommandService(store, new DocumentCommandQueue(), publisher);
    var resolver = new DiagramLayoutStrategyResolver([new GhostLayeredLayoutStrategy()]);
    var layouts = new DiagramLayoutService(commands, resolver);
    var previewRequest = new DiagramLayoutRequest("pipeline", "tester", "preview", 0, "ghost-layered", 5, true);
    var preview = await layouts.ExecuteAsync(previewRequest, default);
    True(preview.Accepted && preview.Code == "LAYOUT_PREVIEW", "dry run must return a preview");
    Equal(0, store.SaveCount, "preview must not persist");

    var commitRequest = previewRequest with { CommandId = "layout-1", DryRun = false };
    var committed = await layouts.ExecuteAsync(commitRequest, default);
    True(committed.Accepted && committed.Code == "COMMITTED" && committed.ProposedRevision == 1, "layout must commit revision 1");
    Equal(1, store.SaveCount, "commit must persist exactly once");
    Equal(1, publisher.Count, "commit must publish exactly once");

    var replay = await layouts.ExecuteAsync(commitRequest, default);
    True(replay.Accepted && replay.Code == "IDEMPOTENT_REPLAY", "same layout intent must replay idempotently");
    Equal(1, store.SaveCount, "replay must not persist again");
    Equal(1, publisher.Count, "replay must not republish");

    var sessions = new DiagramSessionService(commands, layouts, new SvgDiagramExporter());
    var opened = await sessions.OpenAsync("pipeline", "agent-one", null, default);
    True(opened.Accepted && opened.Session is not null && opened.Session.Revision == 1, "agent must open the authoritative revision");
    var sessionId = opened.Session!.SessionId;
    var edit = new GhostagramOperation("node.upsert", JsonSerializer.SerializeToElement(new { id = "a", label = "Agent edited" }));
    var applied = await sessions.ApplyAsync(sessionId, "agent-one", "agent-edit-1", 1, [edit], default);
    True(applied.Accepted && applied.Code == "COMMITTED" && applied.Revision == 2, "session edit must commit revision 2");
    Equal(2, publisher.Count, "session edit must publish through the shared realtime boundary");
    var read = await sessions.ReadAsync(sessionId, "agent-one", 1, default);
    True(read.Accepted && read.Revision == 2 && read.Changes.Count == 1, "session sync must return the committed change");
    var exported = await sessions.ExportSvgAsync(sessionId, "agent-one", default);
    True(exported.Accepted && exported.Revision == 2 && exported.Content?.StartsWith("<svg", StringComparison.Ordinal) == true, "session export must return revision-pinned SVG");
    var closed = await sessions.CloseAsync(sessionId, "agent-one");
    True(closed.Accepted && closed.Code == "SESSION_CLOSED" && closed.Session?.DocumentId == "pipeline" && closed.Session.Revision == 2,
        "session close must identify the document and final authoritative revision");

    var createStore = new MemoryStore(new StoredDocument { DocumentId = "seed", Revision = 0, Model = Model([], []) });
    var createCommands = new DiagramCommandService(createStore, new DocumentCommandQueue(), new RecordingPublisher());
    var createLayouts = new DiagramLayoutService(createCommands, resolver);
    var createSessions = new DiagramSessionService(createCommands, createLayouts, new SvgDiagramExporter());
    var initialOperations = new[] { new GhostagramOperation("node.upsert", JsonSerializer.SerializeToElement(new { id = "created", x = 8, y = 12 })) };
    var created = await createSessions.CreateAsync("created-diagram", "creator", "create-command", initialOperations, default);
    True(created.Accepted && created.Command?.Code == "COMMITTED" && created.Session?.Revision == 1, "create must commit and open its first session");
    var createReplay = await createSessions.CreateAsync("created-diagram", "creator", "create-command", initialOperations, default);
    True(createReplay.Accepted && createReplay.Command?.Code == "IDEMPOTENT_REPLAY" && createReplay.Session?.Revision == 1,
        "an uncertain create retry with the exact payload must replay and reopen safely");
    var createKeyReuse = await createSessions.CreateAsync("created-diagram", "creator", "create-command",
        [new GhostagramOperation("node.upsert", JsonSerializer.SerializeToElement(new { id = "different", x = 0, y = 0 }))], default);
    True(!createKeyReuse.Accepted && createKeyReuse.Code == "IDEMPOTENCY_KEY_REUSED", "a create command id cannot be reused for a different payload");
    var createCollision = await createSessions.CreateAsync("created-diagram", "creator", "different-command", initialOperations, default);
    True(!createCollision.Accepted && createCollision.Code == "DIAGRAM_EXISTS", "a different create intent must not overwrite an existing diagram");

    var copyOperations = ImmutableArray.Create(new GhostagramOperation(
        "node.upsert",
        JsonSerializer.SerializeToElement(new { id = "copy-node", x = 12, y = 20, width = 120, height = 60, label = "Copied" }),
        "copy-node"));
    var copied = await commands.CreateAsync("pipeline-copy", "Pipeline copy", "tester", "copy-1", copyOperations, default);
    True(copied.Accepted && copied.Revision == 1 && copied.Snapshot?.DocumentId == "pipeline-copy", "Save As must create an independent revisioned document");
    var duplicate = await commands.CreateAsync("pipeline-copy", "Duplicate", "tester", "copy-2", copyOperations, default);
    True(!duplicate.Accepted && duplicate.Code == "DOCUMENT_EXISTS", "Save As must not overwrite an existing document");
}

static JsonElement Model(
    IEnumerable<JsonObject> nodes,
    IEnumerable<(string Source, string Target)> edges,
    IEnumerable<JsonObject>? groups = null)
{
    var nodeArray = new JsonArray(nodes.Select(node => node.DeepClone()).ToArray());
    var portArray = new JsonArray();
    foreach (var node in nodeArray.OfType<JsonObject>())
    {
        var id = node["id"]!.GetValue<string>();
        portArray.Add(new JsonObject { ["id"] = $"{id}-in", ["nodeId"] = id, ["direction"] = "target" });
        portArray.Add(new JsonObject { ["id"] = $"{id}-out", ["nodeId"] = id, ["direction"] = "source" });
    }
    var edgeArray = new JsonArray();
    var sequence = 0;
    foreach (var edge in edges)
        edgeArray.Add(new JsonObject { ["id"] = $"e{sequence++}", ["sourcePortId"] = $"{edge.Source}-out", ["targetPortId"] = $"{edge.Target}-in" });
    var root = new JsonObject
    {
        ["nodes"] = nodeArray,
        ["ports"] = portArray,
        ["edges"] = edgeArray,
        ["groups"] = new JsonArray((groups ?? []).Select(group => group.DeepClone()).ToArray()),
        ["edgeTypes"] = new JsonArray(),
        ["selection"] = new JsonArray(),
        ["viewport"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["zoom"] = 1 }
    };
    return JsonSerializer.SerializeToElement(root);
}

static JsonObject Node(string id, double x, double y, string? groupId = null)
{
    var node = new JsonObject { ["id"] = id, ["x"] = x, ["y"] = y, ["width"] = 110, ["height"] = 56, ["label"] = id };
    if (groupId is not null) node["groupId"] = groupId;
    return node;
}

static JsonObject Group(string id, string? parent)
{
    var group = new JsonObject { ["id"] = id, ["x"] = 0, ["y"] = 0, ["width"] = 180, ["height"] = 120, ["label"] = id };
    if (parent is not null) group["parentGroupId"] = parent;
    return group;
}

static (string Source, string Target) Edge(string source, string target) => (source, target);

static Dictionary<string, Box> Boxes(JsonElement model, string collection)
    => model.GetProperty(collection).EnumerateArray().ToDictionary(
        item => item.GetProperty("id").GetString()!,
        item => new Box(
            item.GetProperty("id").GetString()!,
            item.GetProperty("x").GetDouble(),
            item.GetProperty("y").GetDouble(),
            item.GetProperty("width").GetDouble(),
            item.GetProperty("height").GetDouble()),
        StringComparer.Ordinal);

static bool Overlaps(Box left, Box right)
    => left.X < right.X + right.Width && left.X + left.Width > right.X && left.Y < right.Y + right.Height && left.Y + left.Height > right.Y;

static void Contains(Box outer, Box inner, string message)
    => True(inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height, message);

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}. Expected '{expected}', got '{actual}'.");
}

sealed record Box(string Id, double X, double Y, double Width, double Height);

sealed class MemoryStore(StoredDocument document) : IDocumentStore
{
    public int SaveCount { get; private set; }
    public Task<StoredDocument?> LoadAsync(string documentId, CancellationToken cancellationToken)
        => Task.FromResult<StoredDocument?>(documentId == document.DocumentId ? document : null);
    public Task SaveAsync(StoredDocument saved, CancellationToken cancellationToken) { document = saved; SaveCount++; return Task.CompletedTask; }
}

sealed class RecordingPublisher : IDocumentEventPublisher
{
    public int Count { get; private set; }
    public Task PublishCommittedAsync(DiagramChange change, CancellationToken cancellationToken) { Count++; return Task.CompletedTask; }
}
