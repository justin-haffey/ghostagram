using System.Collections.Concurrent;
using System.Collections.Immutable;
using Diagrams.Core.Commands;
using Diagrams.Core.Layouts;
using Diagrams.Core.Models;
using Diagrams.Core.Serialization;
using Diagrams.Core.Templates;
using Diagrams.Core.Validation;

namespace Diagrams.Core.Tests;

public sealed class DiagramCoreTests
{
    private readonly DiagramStencilCatalog _stencils = new();
    private readonly DiagramLayoutEngine _layoutEngine = new();
    private readonly DiagramDocumentSerializer _serializer = new();
    private readonly DiagramValidationEngine _validationEngine = new();

    [Fact]
    public void TemplateCatalog_Creates_All_BuiltIn_Templates()
    {
        var catalog = new DiagramTemplateCatalog(_stencils, _layoutEngine);
        var builtIns = catalog.GetBuiltIns();

        Assert.True(builtIns.Count >= 11);

        foreach (var template in builtIns)
        {
            var document = catalog.Create(template.Kind);
            Assert.Equal(template.Kind, document.TemplateKind);
            Assert.Equal(2, document.SchemaVersion);
            Assert.NotEmpty(document.Nodes);
            Assert.NotEmpty(document.Ports);
        }
    }

    [Fact]
    public void Serializer_RoundTrips_SchemaVersion_Comments_And_Document_Content()
    {
        var catalog = new DiagramTemplateCatalog(_stencils, _layoutEngine);
        var original = catalog.Create(TemplateKind.C4Context) with
        {
            Tags = ["shared", "v15"],
            CommentThreads =
            [
                new CommentThread(
                    "thread-1",
                    new AnnotationTarget("node", "node-1"),
                    "Review",
                    [new CommentEntry("comment-1", "Architect", "Looks good", DateTimeOffset.UtcNow)],
                    CommentStatus.Open)
            ]
        };

        var json = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(json);

        Assert.Equal(2, roundTripped.SchemaVersion);
        Assert.Equal(original.TemplateKind, roundTripped.TemplateKind);
        Assert.Equal(original.Nodes.Length, roundTripped.Nodes.Length);
        Assert.Equal(original.Edges.Length, roundTripped.Edges.Length);
        Assert.Contains("shared", roundTripped.Tags);
        Assert.Single(roundTripped.CommentThreads);
    }

    [Fact]
    public void Serializer_Loads_Legacy_V1_Documents()
    {
        const string legacyJson = """
            {
              "schemaVersion": 1,
              "metadata": {
                "title": "Legacy Diagram",
                "description": "",
                "createdUtc": "2026-04-02T00:00:00+00:00",
                "updatedUtc": "2026-04-02T00:00:00+00:00"
              },
              "templateKind": "Flowchart",
              "canvas": {
                "width": 6000,
                "height": 4000,
                "gridSize": 24
              },
              "viewportState": {
                "zoom": 1,
                "scrollLeft": 0,
                "scrollTop": 0
              },
              "nodes": [],
              "ports": [],
              "edges": [],
              "groups": [],
              "styles": []
            }
            """;

        var document = _serializer.Deserialize(legacyJson);

        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal(TemplateKind.Flowchart, document.TemplateKind);
        Assert.NotNull(document.DocumentId);
        Assert.NotEmpty(document.Layers);
    }

    [Fact]
    public void ValidationEngine_Finds_Label_Port_Layer_And_Overlap_Issues()
    {
        var baseLayer = DiagramLayerCatalog.Defaults[0];
        var lockedLayer = new LayerDefinition("layer-locked", "Locked", true, true, 1);

        var firstNode = new DiagramNode(
            "node-a",
            "process",
            new DiagramBounds(100, 100, 160, 90),
            string.Empty,
            ["port-a"],
            null,
            ImmutableDictionary<string, string?>.Empty,
            "accent")
        {
            LayerId = lockedLayer.Id
        };

        var secondNode = new DiagramNode(
            "node-b",
            "process",
            new DiagramBounds(140, 120, 160, 90),
            "Second",
            ["port-b"],
            null,
            ImmutableDictionary<string, string?>.Empty,
            "accent")
        {
            LayerId = lockedLayer.Id
        };

        var document = new DiagramDocument
        {
            Layers = [baseLayer, lockedLayer],
            Nodes = [firstNode, secondNode],
            Ports =
            [
                new DiagramPort("port-a", "node-a", PortSide.Right, PortRole.Output, EndpointKind.Dot, "Continuous", 4, "Output")
            ],
            Edges =
            [
                new DiagramEdge("edge-a", "port-a", "missing-port", ConnectorKind.Flowchart, string.Empty, new EdgeMarkers(MarkerKind.None, MarkerKind.Arrow), EdgeAnimationKind.Flow, "accent")
            ],
            Groups =
            [
                new DiagramGroup("group-empty", "Empty", new DiagramBounds(0, 0, 300, 200), false, new FreeformLayoutSpec(), ImmutableHashSet<string>.Empty)
            ]
        };

        var issues = _validationEngine.Validate(document);
        var codes = issues.Select(issue => issue.Code).ToArray();

        Assert.Contains("NODE_LABEL_MISSING", codes);
        Assert.Contains("NODE_ON_LOCKED_LAYER", codes);
        Assert.Contains("EDGE_PORT_MISSING", codes);
        Assert.Contains("GROUP_EMPTY", codes);
        Assert.Contains("NODE_OVERLAP", codes);
    }

    [Fact]
    public void GraphStore_Remains_Consistent_Under_Parallel_Command_Load()
    {
        var catalog = new DiagramTemplateCatalog(_stencils, _layoutEngine);
        var store = new DiagramGraphStore(catalog.Create(TemplateKind.Flowchart), _stencils, _layoutEngine);
        var failures = new ConcurrentQueue<Exception>();

        Parallel.ForEach(Enumerable.Range(0, 24), index =>
        {
            try
            {
                store.Apply(new AddNodeCommand("process", 100 + (index * 20), 140 + (index * 10), $"Node {index}"));
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        Assert.Empty(failures);
        Assert.True(store.Snapshot.Nodes.Length >= 28);
    }

    [Fact]
    public void Grid_Layout_Is_Deterministic()
    {
        var catalog = new DiagramTemplateCatalog(_stencils, _layoutEngine);
        var original = catalog.Create(TemplateKind.Erd);

        var first = _layoutEngine.ApplyLayout(original, new GridLayoutSpec(Columns: 2));
        var second = _layoutEngine.ApplyLayout(original, new GridLayoutSpec(Columns: 2));

        Assert.Equal(first.Nodes.Select(node => node.Bounds), second.Nodes.Select(node => node.Bounds));
    }
}
