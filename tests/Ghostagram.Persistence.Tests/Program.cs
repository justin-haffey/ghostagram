using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Ghostagram.Core;
using Ghostagram.Server;
using Ghostagram.Server.Components.Pages;
using Ghostagram.Server.Persistence;

var checks = new List<(string Name, Func<Task> Check)>
{
    ("complete palette catalog round trip", VerifyRoundTripAsync),
    ("catalog list is deterministic and lightweight", VerifyListAsync),
    ("catalog survives repository restart", VerifyRestartAsync),
    ("stale palette writers cannot overwrite a newer revision", VerifyOptimisticConcurrencyAsync),
    ("healthy catalogs remain listable beside corrupt files", VerifyMixedHealthyAndCorruptListAsync),
    ("invalid identifiers and snapshots fail closed", VerifyInvalidInputAsync),
    ("document file store survives restart and isolates corrupt files", VerifyDocumentStoreRestartAsync),
    ("document file store deletes only the requested durable diagram", VerifyDocumentDeleteAsync),
    ("diagram Save As preserves unknown graph fields", VerifyLosslessDiagramCloneAsync),
    ("recovery-mode Save As preserves document extension data", VerifyLosslessRecoverySaveAsAsync),
    ("selected node capture preserves schema-v1 palette fidelity", VerifyPaletteNodeCaptureFidelityAsync),
    ("selected node capture does not retain mutable source metadata", VerifyPaletteNodeCaptureSourceImmutabilityAsync),
    ("selected node capture rejects registered definitions", VerifyPaletteNodeCaptureRegisteredRejectionAsync),
    ("selected node capture falls back unsupported anchors", VerifyPaletteNodeCaptureAnchorFallbackAsync),
    ("node properties editor restores persisted connection point values", VerifyPortEditorProjectionAsync),
    ("laboratory palette apply and capture preserve complete definitions", VerifyLaboratoryPaletteProjectionAsync)
};

foreach (var (name, check) in checks)
{
    var timer = Stopwatch.StartNew();
    await check();
    Console.WriteLine("PASS " + name + " (" + timer.Elapsed.TotalMilliseconds.ToString("F1") + " ms)");
}

static async Task VerifyRoundTripAsync()
{
    await WithRepositoryAsync(async (repository, directory) =>
    {
        using var sourceJson = JsonDocument.Parse("{\"nested\":{\"enabled\":true},\"items\":[1,2,3]}");
        var snapshot = Snapshot(
            "team-palette",
            "Team palette",
            DateTimeOffset.Parse("2026-08-09T13:00:00Z"),
            sourceJson.RootElement);

        True((await repository.CreateAsync(snapshot, default)).Accepted, "new catalog should be created");
        var loaded = await repository.GetAsync("team-palette", default)
            ?? throw new Exception("saved catalog was not found");

        Equal(PaletteCatalogSchema.CurrentVersion, loaded.SchemaVersion, "schema version");
        Equal("Team palette", loaded.Catalog.Name, "catalog name");
        Equal(1, loaded.CustomGroups.Count, "custom group count");
        Equal("mdi:robot-outline", loaded.CustomNodes.Single().Icon, "node icon");
        Equal("#5b5bd6", loaded.CustomNodes.Single().Style.BorderColor, "node style");
        Equal("right", loaded.CustomNodes.Single().Ports.Single().Side, "port side");
        Equal("prompt", loaded.CustomNodes.Single().Ports.Single().PropertyId, "property port binding");
        Equal("string", loaded.CustomNodes.Single().Properties.Single().Type, "property type");
        Equal("hello", loaded.CustomNodes.Single().Properties.Single().DefaultValue?.GetString(), "property default");
        Equal("Agent Tools", loaded.Placements.Single(item => item.ItemId == "agent-task").GroupId, "item placement");
        True(loaded.ExpandedGroupIds.SequenceEqual(["Workflow", "Agent Tools"]), "expanded groups must round trip in order");
        True(loaded.IsOpen && loaded.IsPinned, "palette presentation state must round trip");
        True(loaded.Catalog.Attributes["layout"].GetProperty("nested").GetProperty("enabled").GetBoolean(), "nested JsonElement metadata must remain readable");
        True(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "atomic save must not leave temporary files");
    });
}

static async Task VerifyListAsync()
{
    await WithRepositoryAsync(async (repository, _) =>
    {
        True((await repository.CreateAsync(Snapshot("older", "Older", DateTimeOffset.Parse("2026-08-08T13:00:00Z")), default)).Accepted, "older create");
        True((await repository.CreateAsync(Snapshot("newer-b", "Newer B", DateTimeOffset.Parse("2026-08-09T13:00:00Z")), default)).Accepted, "newer-b create");
        True((await repository.CreateAsync(Snapshot("newer-a", "Newer A", DateTimeOffset.Parse("2026-08-09T13:00:00Z")), default)).Accepted, "newer-a create");

        var summaries = await repository.ListAsync(default);
        True(summaries.Select(item => item.CatalogId).SequenceEqual(["newer-a", "newer-b", "older"]), "list must sort by update time then ID");
        Equal(1, summaries[0].CustomGroupCount, "summary group count");
        Equal(1, summaries[0].CustomNodeCount, "summary node count");
    });
}

static async Task VerifyRestartAsync()
{
    var directory = TestDirectory();
    try
    {
        using (var first = new FilePaletteCatalogRepository(directory))
            True((await first.CreateAsync(Snapshot("restartable", "Restartable", DateTimeOffset.UtcNow), default)).Accepted, "restartable create");

        using var restarted = new FilePaletteCatalogRepository(directory);
        var loaded = await restarted.GetAsync("restartable", default);
        Equal("Restartable", loaded?.Catalog.Name, "new repository instance must load previous state");
        Equal("Agent Tools", loaded?.ExpandedGroupIds.Last(), "expanded group state must survive restart");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task VerifyOptimisticConcurrencyAsync()
{
    var directory = TestDirectory();
    try
    {
        using var first = new FilePaletteCatalogRepository(directory);
        using var second = new FilePaletteCatalogRepository(directory);
        var created = Snapshot("shared", "Shared", DateTimeOffset.UtcNow);
        True((await first.CreateAsync(created, default)).Accepted, "shared catalog create");

        var firstUpdate = Snapshot("shared", "First writer", created.Catalog.UpdatedAtUtc.AddMinutes(1), revision: 2);
        var secondUpdate = Snapshot("shared", "Second writer", created.Catalog.UpdatedAtUtc.AddMinutes(2), revision: 2);
        var commits = await Task.WhenAll(
            first.TrySaveAsync(firstUpdate, expectedRevision: 1, default),
            second.TrySaveAsync(secondUpdate, expectedRevision: 1, default));

        Equal(1, commits.Count(result => result.Accepted), "accepted stale-writer commits");
        Equal(1, commits.Count(result => result.Code == "REVISION_CONFLICT"), "revision conflicts");
        var loaded = await first.GetAsync("shared", default) ?? throw new Exception("shared catalog missing");
        Equal(2L, loaded.Catalog.Revision, "winning revision");
        True(loaded.Catalog.Name is "First writer" or "Second writer", "one complete writer snapshot must win");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task VerifyMixedHealthyAndCorruptListAsync()
{
    await WithRepositoryAsync(async (repository, directory) =>
    {
        True((await repository.CreateAsync(Snapshot("healthy", "Healthy", DateTimeOffset.UtcNow), default)).Accepted, "healthy create");
        Directory.CreateDirectory(directory);
        var corruptPath = Path.Combine(directory, "corrupt.json");
        const string corruptContent = "{not-json";
        await File.WriteAllTextAsync(corruptPath, corruptContent);

        var summaries = await repository.ListAsync(default);
        True(summaries.Select(summary => summary.CatalogId).SequenceEqual(["healthy"]), "corrupt catalog must not hide healthy catalogs");
        Equal(corruptContent, await File.ReadAllTextAsync(corruptPath), "corrupt recovery evidence");
        await ThrowsAsync<InvalidDataException>(() => repository.CreateAsync(Snapshot("corrupt", "Recovery", DateTimeOffset.UtcNow), default), "create over corrupt catalog");
        Equal(corruptContent, await File.ReadAllTextAsync(corruptPath), "failed create must not overwrite corrupt catalog");
    });
}

static async Task VerifyInvalidInputAsync()
{
    await WithRepositoryAsync(async (repository, directory) =>
    {
        await ThrowsAsync<ArgumentException>(() => repository.GetAsync("../escape", default), "path-like catalog ID");
        await ThrowsAsync<ArgumentException>(
            () => repository.CreateAsync(Snapshot("valid", "Valid", DateTimeOffset.UtcNow) with { SchemaVersion = 99 }, default),
            "unsupported schema");

        var duplicateGroup = Snapshot("duplicates", "Duplicates", DateTimeOffset.UtcNow);
        duplicateGroup = duplicateGroup with { CustomGroups = [duplicateGroup.CustomGroups[0], duplicateGroup.CustomGroups[0]] };
        await ThrowsAsync<ArgumentException>(() => repository.CreateAsync(duplicateGroup, default), "duplicate group IDs");

        var brokenPort = Snapshot("broken-port", "Broken port", DateTimeOffset.UtcNow);
        var node = brokenPort.CustomNodes.Single();
        brokenPort = brokenPort with
        {
            CustomNodes = [node with { Ports = [node.Ports.Single() with { PropertyId = "missing" }] }]
        };
        await ThrowsAsync<ArgumentException>(() => repository.CreateAsync(brokenPort, default), "missing property binding");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "corrupt.json"), "{not-json");
        await ThrowsAsync<InvalidDataException>(() => repository.GetAsync("corrupt", default), "malformed persisted catalog");
    });
}

static async Task VerifyDocumentStoreRestartAsync()
{
    var directory = TestDirectory("documents");
    try
    {
        using var model = JsonDocument.Parse("""
        {"documentId":"restartable-diagram","nodes":[],"ports":[],"edges":[],"groups":[],"edgeTypes":[],"selection":[],"viewport":{"x":0,"y":0,"zoom":1}}
        """);
        var now = DateTimeOffset.UtcNow;
        var first = new FileDocumentStore(directory);
        await first.SaveAsync(new StoredDocument
        {
            DocumentId = "restartable-diagram",
            DisplayName = "Restartable diagram",
            CreatedUtc = now,
            UpdatedUtc = now,
            Revision = 3,
            Model = model.RootElement.Clone()
        }, default);

        var restarted = new FileDocumentStore(directory);
        var loaded = await restarted.LoadAsync("restartable-diagram", default) ?? throw new Exception("restarted document missing");
        Equal(3L, loaded.Revision, "restarted document revision");
        Equal("Restartable diagram", loaded.DisplayName, "restarted document display name");

        var corruptPath = Path.Combine(directory, "corrupt.json");
        const string corruptContent = "{not-json";
        await File.WriteAllTextAsync(corruptPath, corruptContent);
        var summaries = await restarted.ListAsync(default);
        True(summaries.Select(summary => summary.DocumentId).SequenceEqual(["restartable-diagram"]), "corrupt document must not hide healthy documents");
        Equal(corruptContent, await File.ReadAllTextAsync(corruptPath), "corrupt document recovery evidence");
        True(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "document commits must not leave temporary files");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task VerifyDocumentDeleteAsync()
{
    var directory = TestDirectory("document-delete");
    try
    {
        var store = new FileDocumentStore(directory);
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(new StoredDocument
        {
            DocumentId = "delete-me",
            DisplayName = "Delete me",
            CreatedUtc = now,
            UpdatedUtc = now,
            Revision = 0,
            Model = JsonSerializer.SerializeToElement(new { documentId = "delete-me", nodes = Array.Empty<object>(), ports = Array.Empty<object>(), edges = Array.Empty<object>(), groups = Array.Empty<object>(), edgeTypes = Array.Empty<object>(), selection = Array.Empty<string>(), viewport = new { x = 0, y = 0, zoom = 1 } })
        }, default);
        await store.SaveAsync(new StoredDocument
        {
            DocumentId = "keep-me",
            DisplayName = "Keep me",
            CreatedUtc = now,
            UpdatedUtc = now,
            Revision = 0,
            Model = JsonSerializer.SerializeToElement(new { documentId = "keep-me", nodes = Array.Empty<object>(), ports = Array.Empty<object>(), edges = Array.Empty<object>(), groups = Array.Empty<object>(), edgeTypes = Array.Empty<object>(), selection = Array.Empty<string>(), viewport = new { x = 0, y = 0, zoom = 1 } })
        }, default);

        True(await store.DeleteAsync("delete-me", default), "existing diagram should delete");
        True(!await store.DeleteAsync("delete-me", default), "repeated delete should report not found");
        True(await store.LoadAsync("delete-me", default) is null, "deleted diagram must be absent");
        True(await store.LoadAsync("keep-me", default) is not null, "other diagram must remain");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task VerifyLosslessDiagramCloneAsync()
{
    var directory = TestDirectory("clone");
    try
    {
        using var sourceJson = JsonDocument.Parse("""
        {
          "documentId":"source-diagram",
          "futureDocument":{"mode":"agentic","nested":[1,{"ok":true}]},
          "nodes":[{"id":"node-1","x":10,"y":20,"width":220,"height":120,"label":"Rich node","futureNode":{"v":2},"style":{"background":"#fff","futureStyle":"kept"},"properties":[{"id":"prompt","name":"Prompt","type":"string","value":"hello","futureProperty":9}]}],
          "ports":[{"id":"node-1-out","nodeId":"node-1","direction":"source","anchor":"right","futurePort":{"shape":"diamond"}}],
          "edges":[],
          "groups":[{"id":"group-1","x":0,"y":0,"width":400,"height":300,"futureGroup":"kept"}],
          "edgeTypes":[{"id":"flow","futureEdgeType":true}],
          "selection":["node-1"],
          "viewport":{"x":4,"y":5,"zoom":1.2,"futureViewport":"kept"}
        }
        """);
        var now = DateTimeOffset.UtcNow;
        var store = new FileDocumentStore(directory);
        await store.SaveAsync(new StoredDocument
        {
            DocumentId = "source-diagram",
            DisplayName = "Source diagram",
            CreatedUtc = now,
            UpdatedUtc = now,
            Revision = 7,
            Model = sourceJson.RootElement.Clone()
        }, default);

        var service = new DiagramCommandService(store, new DocumentCommandQueue(), new NoopDocumentEventPublisher());
        var result = await service.CloneAsync("source-diagram", "cloned-diagram", "Cloned diagram", default);
        True(result.Accepted, "lossless clone should be accepted");
        Equal(0L, result.Revision, "clone starts an independent revision history");

        var clone = await store.LoadAsync("cloned-diagram", default) ?? throw new Exception("clone missing");
        Equal("cloned-diagram", clone.Model.GetProperty("documentId").GetString(), "clone document identity");
        Equal("agentic", clone.Model.GetProperty("futureDocument").GetProperty("mode").GetString(), "document extension data");
        Equal(2, clone.Model.GetProperty("nodes")[0].GetProperty("futureNode").GetProperty("v").GetInt32(), "node extension data");
        Equal("kept", clone.Model.GetProperty("nodes")[0].GetProperty("style").GetProperty("futureStyle").GetString(), "style extension data");
        Equal(9, clone.Model.GetProperty("nodes")[0].GetProperty("properties")[0].GetProperty("futureProperty").GetInt32(), "property extension data");
        Equal("diamond", clone.Model.GetProperty("ports")[0].GetProperty("futurePort").GetProperty("shape").GetString(), "port extension data");
        Equal("kept", clone.Model.GetProperty("groups")[0].GetProperty("futureGroup").GetString(), "group extension data");
        Equal("kept", clone.Model.GetProperty("viewport").GetProperty("futureViewport").GetString(), "viewport extension data");

        var source = await store.LoadAsync("source-diagram", default) ?? throw new Exception("source missing after clone");
        Equal("source-diagram", source.Model.GetProperty("documentId").GetString(), "source identity remains unchanged");
        Equal(7L, source.Revision, "source revision remains unchanged");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task VerifyLosslessRecoverySaveAsAsync()
{
    var directory = TestDirectory("recovery-save-as");
    try
    {
        using var model = JsonDocument.Parse("""
        {
          "documentId":"recovery-source",
          "futureDocument":{"preserve":"root"},
          "nodes":[{"id":"node-1","x":1,"y":2,"futureNode":"preserve"}],
          "ports":[],"edges":[],"groups":[],"edgeTypes":[],"selection":[],
          "viewport":{"x":0,"y":0,"zoom":1,"futureViewport":42}
        }
        """);
        var store = new FileDocumentStore(directory);
        var service = new DiagramCommandService(store, new DocumentCommandQueue(), new NoopDocumentEventPublisher());
        var result = await service.CreateFromSnapshotAsync("recovered-copy", "Recovered copy", model.RootElement, default);
        True(result.Accepted, "recovery Save As should be accepted");

        var recovered = await store.LoadAsync("recovered-copy", default) ?? throw new Exception("recovery copy missing");
        Equal("recovered-copy", recovered.Model.GetProperty("documentId").GetString(), "recovery copy identity");
        Equal("root", recovered.Model.GetProperty("futureDocument").GetProperty("preserve").GetString(), "recovery document extension data");
        Equal("preserve", recovered.Model.GetProperty("nodes")[0].GetProperty("futureNode").GetString(), "recovery node extension data");
        Equal(42, recovered.Model.GetProperty("viewport").GetProperty("futureViewport").GetInt32(), "recovery viewport extension data");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task VerifyPaletteNodeCaptureFidelityAsync()
{
    using var defaultValue = JsonDocument.Parse("{\"prompt\":\"hello\"}");
    using var propertyMetadata = JsonDocument.Parse("{\"source\":\"designer\"}");
    using var extension = JsonDocument.Parse("{\"keep\":true}");
    var property = new DiagramNodeProperty(
        "prompt", "Prompt", DiagramPropertyTypes.Json, defaultValue.RootElement.Clone(), DiagramPropertyModes.DisplayAndEdit,
        "Prompt text", Connectable: true, Options: ["hello", "goodbye"], Metadata: propertyMetadata.RootElement.Clone(),
        SectionId: "details", Editor: new DiagramPropertyEditor(DiagramPropertyEditorKinds.Multiline, "Write a prompt"))
    {
        ExtensionData = new Dictionary<string, JsonElement> { ["futureProperty"] = extension.RootElement.Clone() }
    };
    var node = new DiagramNode(
        "capture-me", 40, 80, 224, 144, "Capture me", Icon: "mdi:robot-outline",
        Style: new DiagramNodeStyle("#fafafa", "#123456", "#111111", "center")
        {
            ExtensionData = new Dictionary<string, JsonElement> { ["futureStyle"] = extension.RootElement.Clone() }
        },
        Properties: [property],
        Sections: [new DiagramNodeSection("details", "Details", Order: 2)],
        Presentation: new DiagramNodePresentation(DiagramNodeDisplayModes.Compact, 180, ["details"]))
    {
        ExtensionData = new Dictionary<string, JsonElement>
        {
            ["description"] = JsonSerializer.SerializeToElement("Captured description"),
            ["futureNode"] = extension.RootElement.Clone()
        }
    };
    var endpoint = new DiagramEndpoint("rectangle", 14, "#aabbcc", "#ddeeff", 3)
    {
        ExtensionData = new Dictionary<string, JsonElement> { ["futureEndpoint"] = extension.RootElement.Clone() }
    };
    var ports = new[]
    {
        new DiagramPort("prompt-in", node.Id, "target", Anchor: "left", Endpoint: endpoint, PropertyId: "prompt", Label: "Prompt input", Order: 1)
        {
            ExtensionData = new Dictionary<string, JsonElement> { ["futurePort"] = extension.RootElement.Clone() }
        },
        new DiagramPort("prompt-out", node.Id, "source", Anchor: "bottom", PropertyId: "prompt", Label: "Prompt output", Order: 3),
        new DiagramPort("other", "other-node", "source", Anchor: "right")
    };

    var captured = PaletteNodeDefinitionCapture.Capture(node, ports);
    Equal(node.Id, captured.Id, "captured node ID");
    Equal("Capture me", captured.Label, "captured label");
    Equal("Captured description", captured.Description, "captured description");
    Equal(224d, captured.Width, "captured width");
    Equal("mdi:robot-outline", captured.Icon, "captured icon");
    Equal("#123456", captured.Style.BorderColor, "captured border color");
    Equal("center", captured.Style.TextAlign, "captured text alignment");
    Equal(2, captured.Ports.Count, "captured node port count");
    var input = captured.Ports.Single(port => port.Id == "prompt-in");
    Equal("left", input.Side, "captured port side");
    Equal("left", input.Anchor, "captured string anchor");
    Equal("Prompt input", input.Label, "captured port label");
    Equal("prompt", input.PropertyId, "captured port property link");
    Equal(1, input.Order, "captured port order");
    Equal("rectangle", input.Endpoint?.Type, "captured endpoint type");
    Equal((double?)14d, input.Endpoint?.Size, "captured endpoint size");
    Equal(true, input.Metadata["futurePort"].GetProperty("keep").GetBoolean(), "captured port extension metadata");
    Equal(true, input.Metadata["ghostagram.endpointExtensions"].GetProperty("futureEndpoint").GetProperty("keep").GetBoolean(), "captured endpoint extension metadata");
    var capturedProperty = captured.Properties.Single();
    Equal("hello", capturedProperty.DefaultValue?.GetProperty("prompt").GetString(), "captured property default value");
    Equal("both", capturedProperty.Direction, "captured property direction");
    Equal(0, capturedProperty.Order, "captured property order");
    Equal("details", capturedProperty.Metadata["ghostagram.sectionId"].GetString(), "captured property section");
    Equal(DiagramPropertyEditorKinds.Multiline, capturedProperty.Metadata["ghostagram.editor"].GetProperty("kind").GetString(), "captured property editor");
    Equal("designer", capturedProperty.Metadata["ghostagram.propertyMetadata"].GetProperty("source").GetString(), "captured property metadata");
    Equal(true, captured.Metadata["futureNode"].GetProperty("keep").GetBoolean(), "captured node extension metadata");
    Equal("Details", captured.Metadata["ghostagram.sections"][0].GetProperty("title").GetString(), "captured sections metadata");
    Equal(DiagramNodeDisplayModes.Compact, captured.Metadata["ghostagram.presentation"].GetProperty("displayMode").GetString(), "captured presentation metadata");
    return Task.CompletedTask;
}

static Task VerifyPaletteNodeCaptureSourceImmutabilityAsync()
{
    var sourceMetadata = new Dictionary<string, JsonElement>
    {
        ["futureNode"] = JsonSerializer.SerializeToElement(new { value = "original" })
    };
    var node = new DiagramNode("immutable", 0, 0, Label: "Immutable") { ExtensionData = sourceMetadata };
    var captured = PaletteNodeDefinitionCapture.Capture(node, []);
    sourceMetadata["futureNode"] = JsonSerializer.SerializeToElement(new { value = "changed" });

    Equal("original", captured.Metadata["futureNode"].GetProperty("value").GetString(), "captured metadata must be cloned");
    Equal("changed", node.ExtensionData?["futureNode"].GetProperty("value").GetString(), "source node remains independently mutable");
    Equal(0d, node.X, "capture must not mutate source node geometry");
    return Task.CompletedTask;
}

static async Task VerifyPaletteNodeCaptureRegisteredRejectionAsync()
{
    var registered = new DiagramNode("registered", 0, 0, Label: "Registered", TypeId: "acme.registered");
    await ThrowsAsync<ArgumentException>(() => Task.Run(() => PaletteNodeDefinitionCapture.Capture(registered, [])), "registered node capture");
}

static Task VerifyPaletteNodeCaptureAnchorFallbackAsync()
{
    var node = new DiagramNode("anchors", 0, 0, Label: "Anchors");
    var captured = PaletteNodeDefinitionCapture.Capture(node,
    [
        new DiagramPort("target", node.Id, "target", Anchor: new { unsupported = true }),
        new DiagramPort("source", node.Id, "source", Anchor: null),
        new DiagramPort("both", node.Id, "both", Anchor: "diagonal")
    ]);

    Equal("left", captured.Ports.Single(port => port.Id == "target").Anchor, "target fallback anchor");
    Equal("right", captured.Ports.Single(port => port.Id == "source").Anchor, "source fallback anchor");
    Equal("right", captured.Ports.Single(port => port.Id == "both").Anchor, "both fallback anchor");
    return Task.CompletedTask;
}

static Task VerifyPortEditorProjectionAsync()
{
    var flags = BindingFlags.Static | BindingFlags.NonPublic;
    var sideFromAnchor = typeof(Home).GetMethod("PortSideFromAnchor", flags)
        ?? throw new Exception("Home.PortSideFromAnchor was not found");
    Equal("left", (string)sideFromAnchor.Invoke(null, [JsonSerializer.SerializeToElement("left")])!, "persisted string anchor side");
    Equal("top", (string)sideFromAnchor.Invoke(null, [JsonSerializer.SerializeToElement(new { x = 0.5, y = 0 })])!, "persisted object anchor side");
    Equal("bottom", (string)sideFromAnchor.Invoke(null, [JsonSerializer.SerializeToElement(new[] { 0.5, 1.0 })])!, "persisted relative anchor side");

    var port = new DiagramPort(
        "node-abc-port-3-4",
        "node-abc",
        "source",
        Anchor: JsonSerializer.SerializeToElement("top"));
    var draftType = typeof(Home).GetNestedType("PortEditorDraft", BindingFlags.NonPublic)
        ?? throw new Exception("Home.PortEditorDraft was not found");
    var draft = Activator.CreateInstance(
        draftType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: [port.Id, port],
        culture: null) ?? throw new Exception("Home.PortEditorDraft could not be constructed");
    Equal("port-3", (string)draftType.GetProperty("Label")!.GetValue(draft)!, "derived connection point label");
    Equal("top", (string)draftType.GetProperty("Side")!.GetValue(draft)!, "connection point side");
    Equal("source", (string)draftType.GetProperty("Direction")!.GetValue(draft)!, "connection point direction");
    return Task.CompletedTask;
}

static Task VerifyLaboratoryPaletteProjectionAsync()
{
    using var metadata = JsonDocument.Parse("{\"owner\":\"qa\",\"nested\":{\"level\":2}}");
    var source = Snapshot("projection", "Projection", DateTimeOffset.UtcNow, metadata.RootElement);
    var home = new Home();
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var apply = typeof(Home).GetMethod("ApplyPaletteCatalog", flags)
        ?? throw new Exception("Home.ApplyPaletteCatalog was not found");
    var capture = typeof(Home).GetMethod("CapturePaletteCatalog", flags)
        ?? throw new Exception("Home.CapturePaletteCatalog was not found");

    apply.Invoke(home, [source]);
    var captured = (PaletteCatalogSnapshot)(capture.Invoke(home,
        [source.CatalogId, source.Catalog.Name, source.Catalog.Revision + 1, source.Catalog.CreatedAtUtc])
        ?? throw new Exception("Home palette capture returned null"));

    Equal(source.Catalog.Description, captured.Catalog.Description, "catalog description through Home");
    Equal("qa", captured.Catalog.Attributes["layout"].GetProperty("owner").GetString(), "catalog metadata through Home");
    var sourceGroup = source.CustomGroups.Single();
    var capturedGroup = captured.CustomGroups.Single(group => group.Id == sourceGroup.Id);
    Equal(sourceGroup.Label, capturedGroup.Label, "group label through Home");
    Equal(sourceGroup.Description, capturedGroup.Description, "group description through Home");
    Equal(sourceGroup.Icon, capturedGroup.Icon, "group icon through Home");

    var sourceNode = source.CustomNodes.Single();
    var capturedNode = captured.CustomNodes.Single(node => node.Id == sourceNode.Id);
    Equal(sourceNode.Icon, capturedNode.Icon, "node icon through Home");
    Equal(sourceNode.Style, capturedNode.Style, "node style through Home");
    var sourcePort = sourceNode.Ports.Single();
    var capturedPort = capturedNode.Ports.Single();
    Equal(sourcePort.Anchor, capturedPort.Anchor, "port anchor through Home");
    Equal(sourcePort.Label, capturedPort.Label, "port label through Home");
    Equal(sourcePort.PropertyId, capturedPort.PropertyId, "port property binding through Home");
    Equal(sourcePort.Endpoint, capturedPort.Endpoint, "port endpoint through Home");
    Equal(sourceNode.Properties.Single().DefaultValue?.GetString(), capturedNode.Properties.Single().DefaultValue?.GetString(), "property default through Home");
    Equal(sourceNode.Properties.Single().Direction, capturedNode.Properties.Single().Direction, "property direction through Home");
    Equal("Agent Tools", captured.Placements.Single(placement => placement.ItemId == "agent-task").GroupId, "custom placement through Home");
    Equal(0, captured.Placements.Single(placement => placement.ItemId == "agent-task").Order, "custom placement order through Home");
    True(source.ExpandedGroupIds.ToHashSet(StringComparer.Ordinal).SetEquals(captured.ExpandedGroupIds), "expanded groups through Home");
    True(captured.IsOpen && captured.IsPinned, "palette presentation state through Home");
    return Task.CompletedTask;
}

static PaletteCatalogSnapshot Snapshot(string id, string name, DateTimeOffset updatedAt, JsonElement? metadata = null, long revision = 1)
{
    var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
    {
        ["layout"] = metadata?.Clone() ?? JsonSerializer.SerializeToElement(new { density = "comfortable" })
    };
    var property = new PalettePropertyDefinitionSnapshot(
        "prompt", "Prompt", "string", JsonSerializer.SerializeToElement("hello"), "edit", "Prompt", true,
        ["hello", "goodbye"], "both", 0, new Dictionary<string, JsonElement>());
    var port = new PalettePortDefinitionSnapshot(
        "prompt-out", "right", "source", "right", "Prompt", "prompt", 0,
        new PaletteEndpointSnapshot("dot", 10, "#5b5bd6", "#ffffff", 2),
        new Dictionary<string, JsonElement>());
    var node = new PaletteNodeDefinitionSnapshot(
        "agent-task", "Agent task", "Invokes an agent task", 220, 112, false, "mdi:robot-outline",
        new PaletteNodeStyleSnapshot("#5b5bd6", "#ffffff", "#172033", "left"),
        [port], [property], new Dictionary<string, JsonElement>());

    return new(
        PaletteCatalogSchema.CurrentVersion,
        id,
        new PaletteCatalogMetadata(name, "A saved custom palette", revision, updatedAt.AddDays(-1), updatedAt, attributes),
        [new PaletteGroupSnapshot("Agent Tools", "Agent Tools", "Custom agent nodes", "mdi:robot-outline", 0, new Dictionary<string, JsonElement>())],
        [node],
        [new PaletteItemPlacementSnapshot("agent-task", "Agent Tools", 0), new PaletteItemPlacementSnapshot("start", "Workflow", 0)],
        ["Workflow", "Agent Tools"],
        IsOpen: true,
        IsPinned: true);
}

static async Task WithRepositoryAsync(Func<FilePaletteCatalogRepository, string, Task> action)
{
    var directory = TestDirectory();
    try
    {
        using var repository = new FilePaletteCatalogRepository(directory);
        await action(repository, directory);
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static string TestDirectory(string kind = "palette") =>
    Path.Combine(Path.GetTempPath(), $"ghostagram-{kind}-tests-" + Guid.NewGuid().ToString("N"));

static async Task ThrowsAsync<TException>(Func<Task> action, string subject) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new Exception("Expected " + typeof(TException).Name + " for " + subject + ".");
}

static void Equal<T>(T expected, T actual, string subject)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception("Expected " + subject + " to be '" + expected + "', but was '" + actual + "'.");
}

static void True(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

file sealed class NoopDocumentEventPublisher : IDocumentEventPublisher
{
    public Task PublishCommittedAsync(Ghostagram.Contracts.DiagramChange change, CancellationToken cancellationToken) => Task.CompletedTask;
}
