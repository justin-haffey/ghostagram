using Ghostagram.Core;

namespace Ghostagram.Blazor;

/// <summary>Internal composition root used by declarative Razor diagram parts.</summary>
public sealed class DiagramCompositionContext
{
    private readonly List<DiagramNode> _nodes = [];
    private readonly List<DiagramPort> _ports = [];
    private readonly List<DiagramEdge> _edges = [];
    private readonly List<DiagramGroup> _groups = [];
    private readonly List<DiagramEdgeType> _edgeTypes = [];
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    internal DiagramCompositionContext(string documentId, string? groupId = null, string? nodeId = null)
    {
        DocumentId = documentId;
        GroupId = groupId;
        NodeId = nodeId;
    }

    public string DocumentId { get; }
    public string? GroupId { get; }
    public string? NodeId { get; }
    internal DiagramCompositionContext InGroup(string groupId)
    {
        var nested = new DiagramCompositionContext(DocumentId, groupId, null) { Root = Root };
        return nested;
    }
    internal DiagramCompositionContext InNode(string nodeId)
    {
        var nested = new DiagramCompositionContext(DocumentId, GroupId, nodeId) { Root = Root };
        return nested;
    }
    private DiagramCompositionContext Root { get; set; } = null!;

    internal void InitializeRoot() => Root = this;
    internal void Add(DiagramNode node) { Root.AddId(node.Id); Root._nodes.Add(node); }
    internal void Add(DiagramPort port) { Root.AddId(port.Id); Root._ports.Add(port); }
    internal void Add(DiagramEdge edge) { Root.AddId(edge.Id); Root._edges.Add(edge); }
    internal void Add(DiagramGroup group) { Root.AddId(group.Id); Root._groups.Add(group); }
    internal void Add(DiagramEdgeType edgeType) { Root.AddId(edgeType.Id); Root._edgeTypes.Add(edgeType); }
    internal DiagramDocument Build() => new(DocumentId, _nodes.ToArray(), _ports.ToArray(), _edges.ToArray(), _groups.ToArray(), new DiagramViewport(), _edgeTypes.ToArray());
    private void AddId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Every declarative Ghostagram item requires an Id.");
        if (!_ids.Add(id)) throw new InvalidOperationException($"Ghostagram diagram item id '{id}' is duplicated.");
    }
}
