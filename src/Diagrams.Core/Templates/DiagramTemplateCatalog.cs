using System.Collections.Immutable;
using Diagrams.Core.Abstractions;
using Diagrams.Core.Models;

namespace Diagrams.Core.Templates;

public sealed record TemplateDefinition(
    TemplateKind Kind,
    string DisplayName,
    string Description,
    LayoutSpec DefaultLayout,
    string AccentToken);

public sealed class DiagramTemplateCatalog(
    DiagramStencilCatalog stencils,
    IDiagramLayoutEngine layoutEngine)
{
    private readonly ImmutableArray<TemplateDefinition> _templates =
    [
        new(TemplateKind.Flowchart, "Flowchart", "Operational and process flow mapping.", new HierarchyTopDownLayoutSpec(), "accent"),
        new(TemplateKind.OrgChart, "Org Chart", "People and team structure.", new HierarchyTopDownLayoutSpec(), "accent"),
        new(TemplateKind.UmlClass, "UML Class", "Class and component relationships.", new HierarchyLeftRightLayoutSpec(), "ink"),
        new(TemplateKind.UmlSequence, "UML Sequence", "Participants and interactions over time.", new GridLayoutSpec(Columns: 4), "ink"),
        new(TemplateKind.Erd, "ERD", "Entity and data relationships.", new GridLayoutSpec(Columns: 2), "copper"),
        new(TemplateKind.C4Context, "C4 Context", "System context and boundaries.", new RadialLayoutSpec(), "accent"),
        new(TemplateKind.BpmnLite, "BPMN Lite", "Process stages, gateways, and events.", new HierarchyTopDownLayoutSpec(), "signal"),
        new(TemplateKind.NetworkTopology, "Network", "Infrastructure zones and links.", new RadialLayoutSpec(), "ink"),
        new(TemplateKind.MindMap, "Mind Map", "Idea branching and concept clustering.", new MindMapLayoutSpec(), "accent"),
        new(TemplateKind.DataFlow, "Data Flow", "Pipelines, sources, and storage.", new HierarchyLeftRightLayoutSpec(), "signal"),
        new(TemplateKind.StateDependency, "State", "States, events, and dependencies.", new TreeLayoutSpec(), "signal")
    ];

    public IReadOnlyList<TemplateDefinition> GetBuiltIns() => _templates;

    public TemplateDefinition Get(TemplateKind kind) => _templates.First(template => template.Kind == kind);

    public DiagramDocument Create(TemplateKind kind)
    {
        var template = Get(kind);
        var builder = new TemplateBuilder(stencils, kind);

        switch (kind)
        {
            case TemplateKind.Flowchart:
                {
                    var intake = builder.AddNode("process", "Intake");
                    var review = builder.AddNode("decision", "Review");
                    var approve = builder.AddNode("process", "Approve");
                    var revise = builder.AddNode("process", "Revise");
                    builder.AddEdge(intake, PortRole.Output, review, PortRole.Input, ConnectorKind.Flowchart, "submit", EdgeAnimationKind.Flow);
                    builder.AddEdge(review, PortRole.Output, approve, PortRole.Input, ConnectorKind.Flowchart, "yes", EdgeAnimationKind.Flow);
                    builder.AddEdge(review, PortRole.Dependency, revise, PortRole.Input, ConnectorKind.Flowchart, "needs work", EdgeAnimationKind.Pulse);
                    builder.AddGroup("Review Loop", new DiagramBounds(0, 0, 720, 320), new FreeformLayoutSpec(), approve.Node.Id, revise.Node.Id);
                    break;
                }
            case TemplateKind.OrgChart:
                {
                    var ceo = builder.AddNode("actor", "CEO");
                    var ops = builder.AddNode("actor", "Operations");
                    var design = builder.AddNode("actor", "Design");
                    var engineering = builder.AddNode("actor", "Engineering");
                    builder.AddEdge(ceo, PortRole.Output, ops, PortRole.Input, ConnectorKind.Straight, "leads");
                    builder.AddEdge(ceo, PortRole.Output, design, PortRole.Input, ConnectorKind.Straight, "leads");
                    builder.AddEdge(ceo, PortRole.Output, engineering, PortRole.Input, ConnectorKind.Straight, "leads");
                    break;
                }
            case TemplateKind.UmlClass:
                {
                    var client = builder.AddNode("service", "DiagramClient");
                    var state = builder.AddNode("service", "EditorState");
                    var adapter = builder.AddNode("service", "JsPlumbAdapter");
                    var store = builder.AddNode("service", "GraphStore");
                    builder.AddEdge(client, PortRole.Output, state, PortRole.Input, ConnectorKind.Bezier, "uses");
                    builder.AddEdge(state, PortRole.Dependency, adapter, PortRole.Input, ConnectorKind.Bezier, "depends on");
                    builder.AddEdge(state, PortRole.Event, store, PortRole.Input, ConnectorKind.Bezier, "syncs", EdgeAnimationKind.Flow, styleToken: "accent");
                    break;
                }
            case TemplateKind.UmlSequence:
                {
                    var caller = builder.AddNode("lifeline", "Client");
                    var api = builder.AddNode("lifeline", "API");
                    var worker = builder.AddNode("lifeline", "Worker");
                    var store = builder.AddNode("lifeline", "Storage");
                    builder.AddEdge(caller, PortRole.Output, api, PortRole.Input, ConnectorKind.Straight, "request");
                    builder.AddEdge(api, PortRole.Output, worker, PortRole.Input, ConnectorKind.Straight, "dispatch");
                    builder.AddEdge(worker, PortRole.Output, store, PortRole.Input, ConnectorKind.Straight, "persist");
                    builder.AddEdge(store, PortRole.Output, api, PortRole.Input, ConnectorKind.Straight, "ack");
                    break;
                }
            case TemplateKind.Erd:
                {
                    var customer = builder.AddNode("database", "Customer");
                    var order = builder.AddNode("database", "Order");
                    var invoice = builder.AddNode("database", "Invoice");
                    builder.AddEdge(customer, PortRole.Association, order, PortRole.Association, ConnectorKind.Straight, "1..n", styleToken: "copper");
                    builder.AddEdge(order, PortRole.Association, invoice, PortRole.Association, ConnectorKind.Straight, "1..1", styleToken: "copper");
                    break;
                }
            case TemplateKind.C4Context:
                {
                    var user = builder.AddNode("persona", "Operations User");
                    var system = builder.AddNode("container", "Diagram Studio");
                    var identity = builder.AddNode("service", "Identity");
                    var analytics = builder.AddNode("service", "Analytics");
                    builder.AddGroup("Platform Boundary", new DiagramBounds(0, 0, 980, 480), new FreeformLayoutSpec(), system.Node.Id, identity.Node.Id, analytics.Node.Id);
                    builder.AddEdge(user, PortRole.Output, system, PortRole.Input, ConnectorKind.Bezier, "uses");
                    builder.AddEdge(system, PortRole.Output, identity, PortRole.Input, ConnectorKind.Bezier, "authenticates");
                    builder.AddEdge(system, PortRole.Event, analytics, PortRole.Input, ConnectorKind.Bezier, "telemetry", EdgeAnimationKind.Flow, "accent");
                    break;
                }
            case TemplateKind.BpmnLite:
                {
                    var start = builder.AddNode("event", "Start");
                    var task = builder.AddNode("process", "Assess Request");
                    var gateway = builder.AddNode("gateway", "Approved?");
                    var end = builder.AddNode("event", "Complete");
                    var rework = builder.AddNode("process", "Request Rework");
                    builder.AddEdge(start, PortRole.Event, task, PortRole.Input, ConnectorKind.Flowchart, "begin");
                    builder.AddEdge(task, PortRole.Output, gateway, PortRole.Input, ConnectorKind.Flowchart, "submit");
                    builder.AddEdge(gateway, PortRole.Output, end, PortRole.Event, ConnectorKind.Flowchart, "yes");
                    builder.AddEdge(gateway, PortRole.Dependency, rework, PortRole.Input, ConnectorKind.Flowchart, "no");
                    break;
                }
            case TemplateKind.NetworkTopology:
                {
                    var edge = builder.AddNode("device", "Edge Firewall");
                    var api = builder.AddNode("service", "API");
                    var workers = builder.AddNode("service", "Workers");
                    var db = builder.AddNode("database", "SQL");
                    builder.AddGroup("Core Zone", new DiagramBounds(0, 0, 880, 460), new FreeformLayoutSpec(), api.Node.Id, workers.Node.Id, db.Node.Id);
                    builder.AddEdge(edge, PortRole.Bidirectional, api, PortRole.Input, ConnectorKind.Bezier, "https", EdgeAnimationKind.Flow, "ink");
                    builder.AddEdge(api, PortRole.Output, workers, PortRole.Input, ConnectorKind.Bezier, "events", EdgeAnimationKind.Flow, "accent");
                    builder.AddEdge(workers, PortRole.Output, db, PortRole.Association, ConnectorKind.Bezier, "write", EdgeAnimationKind.Pulse, "copper");
                    break;
                }
            case TemplateKind.MindMap:
                {
                    var vision = builder.AddNode("event", "Vision");
                    var ui = builder.AddNode("process", "Experience");
                    var runtime = builder.AddNode("service", "Runtime");
                    var data = builder.AddNode("storage", "Persistence");
                    var ops = builder.AddNode("device", "Operations");
                    builder.AddEdge(vision, PortRole.Event, ui, PortRole.Input, ConnectorKind.StateMachine, "focus");
                    builder.AddEdge(vision, PortRole.Event, runtime, PortRole.Input, ConnectorKind.StateMachine, "platform");
                    builder.AddEdge(vision, PortRole.Event, data, PortRole.Input, ConnectorKind.StateMachine, "structure");
                    builder.AddEdge(vision, PortRole.Event, ops, PortRole.Input, ConnectorKind.StateMachine, "delivery");
                    break;
                }
            case TemplateKind.DataFlow:
                {
                    var source = builder.AddNode("service", "Source");
                    var transform = builder.AddNode("process", "Transform");
                    var sink = builder.AddNode("storage", "Lakehouse");
                    var notify = builder.AddNode("event", "Alert");
                    builder.AddEdge(source, PortRole.Output, transform, PortRole.Input, ConnectorKind.Flowchart, "stream", EdgeAnimationKind.Flow, "accent");
                    builder.AddEdge(transform, PortRole.Output, sink, PortRole.Input, ConnectorKind.Flowchart, "persist", EdgeAnimationKind.Flow, "copper");
                    builder.AddEdge(transform, PortRole.Event, notify, PortRole.Event, ConnectorKind.Bezier, "status", EdgeAnimationKind.Pulse, "signal");
                    break;
                }
            case TemplateKind.StateDependency:
                {
                    var draft = builder.AddNode("event", "Draft");
                    var review = builder.AddNode("process", "Review");
                    var approved = builder.AddNode("event", "Approved");
                    var published = builder.AddNode("event", "Published");
                    builder.AddEdge(draft, PortRole.Event, review, PortRole.Input, ConnectorKind.StateMachine, "submit");
                    builder.AddEdge(review, PortRole.Output, approved, PortRole.Event, ConnectorKind.StateMachine, "accept");
                    builder.AddEdge(approved, PortRole.Event, published, PortRole.Event, ConnectorKind.StateMachine, "release");
                    break;
                }
        }

        var document = builder.Build();
        return layoutEngine.ApplyLayout(document, template.DefaultLayout);
    }

    private sealed class TemplateBuilder(DiagramStencilCatalog stencils, TemplateKind templateKind)
    {
        private readonly List<DiagramNode> _nodes = [];
        private readonly List<DiagramPort> _ports = [];
        private readonly List<DiagramEdge> _edges = [];
        private readonly List<DiagramGroup> _groups = [];

        public (DiagramNode Node, ImmutableArray<DiagramPort> Ports) AddNode(string stencilKey, string label, string? groupId = null)
        {
            var created = stencils.CreateInstance(stencilKey, 0, 0, label, groupId);
            _nodes.Add(created.Node);
            _ports.AddRange(created.Ports);
            return created;
        }

        public void AddGroup(string label, DiagramBounds bounds, LayoutSpec spec, params string[] childNodeIds)
        {
            _groups.Add(new DiagramGroup(
                $"group-{Guid.NewGuid():N}",
                label,
                bounds,
                false,
                spec,
                childNodeIds.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)));
        }

        public void AddEdge(
            (DiagramNode Node, ImmutableArray<DiagramPort> Ports) source,
            PortRole sourceRole,
            (DiagramNode Node, ImmutableArray<DiagramPort> Ports) target,
            PortRole targetRole,
            ConnectorKind connectorKind,
            string label,
            EdgeAnimationKind animation = EdgeAnimationKind.None,
            string? styleToken = null)
        {
            var sourcePort = source.Ports.First(port => port.Role == sourceRole);
            var targetPort = target.Ports.First(port => port.Role == targetRole);
            _edges.Add(new DiagramEdge(
                $"edge-{Guid.NewGuid():N}",
                sourcePort.Id,
                targetPort.Id,
                connectorKind,
                label,
                EdgeMarkersFor(sourceRole, targetRole),
                animation,
                styleToken ?? source.Node.StyleToken));
        }

        public DiagramDocument Build()
        {
            var updatedNodes = _nodes.Select(node =>
            {
                var groupId = _groups.FirstOrDefault(group => group.ChildNodeIds.Contains(node.Id))?.Id;
                return groupId is null ? node : node with { GroupId = groupId };
            }).ToImmutableArray();

            return new DiagramDocument
            {
                Metadata = DiagramMetadata.Create($"{templateKind} Diagram"),
                TemplateKind = templateKind,
                Nodes = updatedNodes,
                Ports = _ports.ToImmutableArray(),
                Edges = _edges.ToImmutableArray(),
                Groups = _groups.ToImmutableArray(),
                ViewportState = ViewportState.Default,
                Styles = DiagramStyleCatalog.Defaults
            }.Touch();
        }

        private static EdgeMarkers EdgeMarkersFor(PortRole sourceRole, PortRole targetRole)
            => (sourceRole, targetRole) switch
            {
                (_, PortRole.Inheritance) => new EdgeMarkers(MarkerKind.None, MarkerKind.Triangle),
                (_, PortRole.Aggregation) => new EdgeMarkers(MarkerKind.None, MarkerKind.HollowDiamond),
                (_, PortRole.Composition) => new EdgeMarkers(MarkerKind.None, MarkerKind.Diamond),
                (PortRole.Bidirectional, _) or (_, PortRole.Bidirectional) => new EdgeMarkers(MarkerKind.Arrow, MarkerKind.Arrow),
                (_, PortRole.Association) => new EdgeMarkers(MarkerKind.None, MarkerKind.None),
                _ => new EdgeMarkers(MarkerKind.None, MarkerKind.Arrow)
            };
    }
}
