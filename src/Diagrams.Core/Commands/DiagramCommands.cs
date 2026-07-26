using System.Collections.Immutable;
using System.Threading;
using Diagrams.Core.Abstractions;
using Diagrams.Core.Models;
using Diagrams.Core.Templates;

namespace Diagrams.Core.Commands;

public interface IDiagramCommand
{
    DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context);
}

public sealed record DiagramCommandContext(
    DiagramStencilCatalog Stencils,
    IDiagramLayoutEngine LayoutEngine,
    TimeProvider TimeProvider);

public sealed class DiagramGraphStore
{
    private DiagramDocument _current;
    private readonly DiagramCommandContext _context;

    public DiagramGraphStore(
        DiagramDocument initial,
        DiagramStencilCatalog stencils,
        IDiagramLayoutEngine layoutEngine,
        TimeProvider? timeProvider = null)
    {
        _current = initial;
        _context = new DiagramCommandContext(stencils, layoutEngine, timeProvider ?? TimeProvider.System);
    }

    public DiagramDocument Snapshot => Volatile.Read(ref _current);

    public DiagramDocument Apply(IDiagramCommand command)
    {
        while (true)
        {
            var current = Snapshot;
            var next = command.Apply(current, _context).Touch(_context.TimeProvider);

            if (ReferenceEquals(current, next))
            {
                return current;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, next, current), current))
            {
                return next;
            }
        }
    }
}

public sealed record ReplaceDocumentCommand(DiagramDocument Document) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context) => Document;
}

public sealed record AddNodeCommand(string StencilKey, double X, double Y, string? Label = null, string? GroupId = null) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
    {
        var created = context.Stencils.CreateInstance(StencilKey, X, Y, Label, GroupId);
        return current with
        {
            Nodes = current.Nodes.Add(created.Node),
            Ports = current.Ports.AddRange(created.Ports),
            Groups = GroupId is null
                ? current.Groups
                : current.Groups.Select(group => group.Id == GroupId
                    ? group with { ChildNodeIds = group.ChildNodeIds.Add(created.Node.Id) }
                    : group).ToImmutableArray()
        };
    }
}

public sealed record MoveNodeCommand(string NodeId, DiagramBounds Bounds) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => current with
        {
            Nodes = current.Nodes.Select(node => node.Id == NodeId ? node with { Bounds = Bounds } : node).ToImmutableArray()
        };
}

public sealed record UpdateNodeLabelCommand(string NodeId, string Label) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => current with
        {
            Nodes = current.Nodes.Select(node => node.Id == NodeId ? node with { Label = Label } : node).ToImmutableArray()
        };
}

public sealed record SetNodePropertyCommand(string NodeId, string Key, string? Value) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => current with
        {
            Nodes = current.Nodes.Select(node => node.Id == NodeId
                ? node with { Properties = node.Properties.SetItem(Key, Value) }
                : node).ToImmutableArray()
        };
}

public sealed record AddEdgeCommand(
    string SourcePortId,
    string TargetPortId,
    ConnectorKind ConnectorKind,
    string Label,
    EdgeMarkers Markers,
    EdgeAnimationKind Animation,
    string StyleToken) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
    {
        if (current.Ports.All(port => port.Id != SourcePortId) || current.Ports.All(port => port.Id != TargetPortId))
        {
            return current;
        }

        if (current.Edges.Any(edge => edge.SourcePortId == SourcePortId && edge.TargetPortId == TargetPortId))
        {
            return current;
        }

        return current with
        {
            Edges = current.Edges.Add(new DiagramEdge(
                $"edge-{Guid.NewGuid():N}",
                SourcePortId,
                TargetPortId,
                ConnectorKind,
                Label,
                Markers,
                Animation,
                StyleToken))
        };
    }
}

public sealed record RemoveEdgeCommand(string EdgeId) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => current with
        {
            Edges = current.Edges.Where(edge => edge.Id != EdgeId).ToImmutableArray()
        };
}

public sealed record SetEdgeLabelCommand(string EdgeId, string Label) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => current with
        {
            Edges = current.Edges.Select(edge => edge.Id == EdgeId ? edge with { Label = Label } : edge).ToImmutableArray()
        };
}

public sealed record SetEdgeWaypointsCommand(string EdgeId, ImmutableArray<DiagramPoint> Waypoints) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => DiagramEdgeCommandHelpers.UpdateEdge(current, EdgeId, edge => edge with { Waypoints = Waypoints });
}

public sealed record InsertEdgeWaypointCommand(string EdgeId, int SegmentIndex, DiagramPoint Point) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => DiagramEdgeCommandHelpers.UpdateEdge(current, EdgeId, edge =>
        {
            var insertIndex = Math.Clamp(SegmentIndex, 0, edge.Waypoints.Length);
            return edge with { Waypoints = edge.Waypoints.Insert(insertIndex, Point) };
        });
}

public sealed record MoveEdgeWaypointCommand(string EdgeId, int WaypointIndex, DiagramPoint Point) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => DiagramEdgeCommandHelpers.UpdateEdge(current, EdgeId, edge =>
        {
            if (WaypointIndex < 0 || WaypointIndex >= edge.Waypoints.Length)
            {
                return edge;
            }

            return edge with { Waypoints = edge.Waypoints.SetItem(WaypointIndex, Point) };
        });
}

public sealed record RemoveEdgeWaypointCommand(string EdgeId, int WaypointIndex) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => DiagramEdgeCommandHelpers.UpdateEdge(current, EdgeId, edge =>
        {
            if (WaypointIndex < 0 || WaypointIndex >= edge.Waypoints.Length)
            {
                return edge;
            }

            return edge with { Waypoints = edge.Waypoints.RemoveAt(WaypointIndex) };
        });
}

public sealed record ClearEdgeWaypointsCommand(string EdgeId) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => DiagramEdgeCommandHelpers.UpdateEdge(current, EdgeId, edge => edge with { Waypoints = [] });
}

public sealed record AddGroupCommand(string Label, DiagramBounds Bounds, LayoutSpec LayoutSpec) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => current with
        {
            Groups = current.Groups.Add(new DiagramGroup(
                $"group-{Guid.NewGuid():N}",
                Label,
                Bounds,
                false,
                LayoutSpec,
                ImmutableHashSet<string>.Empty))
        };
}

public sealed record SetGroupCollapsedCommand(string GroupId, bool Collapsed) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => current with
        {
            Groups = current.Groups.Select(group => group.Id == GroupId ? group with { Collapsed = Collapsed } : group).ToImmutableArray()
        };
}

public sealed record SetGroupBoundsCommand(string GroupId, DiagramBounds Bounds) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => current with
        {
            Groups = current.Groups.Select(group => group.Id == GroupId ? group with { Bounds = Bounds } : group).ToImmutableArray()
        };
}

public sealed record SetNodeGroupCommand(string NodeId, string? GroupId) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
    {
        var updatedNodes = current.Nodes.Select(node => node.Id == NodeId ? node with { GroupId = GroupId } : node).ToImmutableArray();
        var updatedGroups = current.Groups.Select(group =>
        {
            var children = group.ChildNodeIds.Remove(NodeId);
            if (group.Id == GroupId)
            {
                children = children.Add(NodeId);
            }

            return group with { ChildNodeIds = children };
        }).ToImmutableArray();

        return current with
        {
            Nodes = updatedNodes,
            Groups = updatedGroups
        };
    }
}

public sealed record SetViewportCommand(ViewportState State) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => current with { ViewportState = State };
}

public sealed record ApplyLayoutCommand(LayoutSpec Spec, string? GroupId = null) : IDiagramCommand
{
    public DiagramDocument Apply(DiagramDocument current, DiagramCommandContext context)
        => context.LayoutEngine.ApplyLayout(current, Spec, GroupId);
}

internal static class DiagramEdgeCommandHelpers
{
    public static DiagramDocument UpdateEdge(DiagramDocument current, string edgeId, Func<DiagramEdge, DiagramEdge> update)
    {
        var changed = false;
        var edges = current.Edges.Select(edge =>
        {
            if (!string.Equals(edge.Id, edgeId, StringComparison.OrdinalIgnoreCase))
            {
                return edge;
            }

            changed = true;
            return update(edge);
        }).ToImmutableArray();

        return changed ? current with { Edges = edges } : current;
    }
}
