using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Ghostagram.Server;
using Ghostagram.Server.Export;
using Ghostagram.Server.Layout;
using Ghostagram.Server.Sessions;

var checks = new List<(string Name, Action Check)>
{
    ("deterministic output", VerifyDeterminism),
    ("directed flow and crossing monotonicity", VerifyDirectedFlow),
    ("nested compound containment", VerifyNestedGroups),
    ("group removal preserves and ungroups content", VerifyGroupRemoval),
    ("explicit group assignment supports reparenting and ungrouping", VerifyGroupAssignment),
    ("cycles, components, and non-overlap", VerifyCyclesAndComponents),
    ("large sparse graph performance", VerifyPerformance),
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
    True(closed.Accepted && closed.Code == "SESSION_CLOSED", "session close must remove the participant");
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
