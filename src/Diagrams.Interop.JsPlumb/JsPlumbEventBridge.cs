using Diagrams.Core.Models;
using Microsoft.JSInterop;

namespace Diagrams.Interop.JsPlumb;

public sealed record NodeMovedEvent(string NodeId, DiagramBounds Bounds);
public sealed record EdgeCreatedEvent(string SourcePortId, string TargetPortId);
public sealed record EdgeRelabeledEvent(string EdgeId, string Label);
public sealed record EdgeDeletedEvent(string EdgeId);
public sealed record WaypointInsertedEvent(string EdgeId, int SegmentIndex, double X, double Y);
public sealed record WaypointMovedEvent(string EdgeId, int WaypointIndex, double X, double Y);
public sealed record WaypointRemovedEvent(string EdgeId, int WaypointIndex);
public sealed record GroupMembershipChangedEvent(string NodeId, string? GroupId);
public sealed record GroupBoundsChangedEvent(string GroupId, DiagramBounds Bounds);
public sealed record GroupCollapseChangedEvent(string GroupId, bool Collapsed);
public sealed record SelectionChangedEvent(string Kind, string? Id, IReadOnlyList<string> NodeIds, IReadOnlyList<string> GroupIds, IReadOnlyList<string> EdgeIds);
public sealed record ViewportChangedEvent(ViewportState ViewportState);
public sealed record HotkeyPressedEvent(string Command);

public sealed class JsPlumbEventBridge
{
    public event Func<NodeMovedEvent, Task>? NodeMoved;
    public event Func<EdgeCreatedEvent, Task>? EdgeCreated;
    public event Func<EdgeRelabeledEvent, Task>? EdgeRelabeled;
    public event Func<EdgeDeletedEvent, Task>? EdgeDeleted;
    public event Func<WaypointInsertedEvent, Task>? WaypointInserted;
    public event Func<WaypointMovedEvent, Task>? WaypointMoved;
    public event Func<WaypointRemovedEvent, Task>? WaypointRemoved;
    public event Func<GroupMembershipChangedEvent, Task>? GroupMembershipChanged;
    public event Func<GroupBoundsChangedEvent, Task>? GroupBoundsChanged;
    public event Func<GroupCollapseChangedEvent, Task>? GroupCollapseChanged;
    public event Func<SelectionChangedEvent, Task>? SelectionChanged;
    public event Func<ViewportChangedEvent, Task>? ViewportChanged;
    public event Func<HotkeyPressedEvent, Task>? HotkeyPressed;

    [JSInvokable]
    public Task OnNodeMoved(string nodeId, double x, double y, double width, double height)
        => NodeMoved?.Invoke(new NodeMovedEvent(nodeId, new DiagramBounds(x, y, width, height))) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnEdgeCreated(string sourcePortId, string targetPortId)
        => EdgeCreated?.Invoke(new EdgeCreatedEvent(sourcePortId, targetPortId)) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnEdgeRelabeled(string edgeId, string label)
        => EdgeRelabeled?.Invoke(new EdgeRelabeledEvent(edgeId, label)) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnEdgeDeleted(string edgeId)
        => EdgeDeleted?.Invoke(new EdgeDeletedEvent(edgeId)) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnWaypointInserted(string edgeId, int segmentIndex, double x, double y)
        => WaypointInserted?.Invoke(new WaypointInsertedEvent(edgeId, segmentIndex, x, y)) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnWaypointMoved(string edgeId, int waypointIndex, double x, double y)
        => WaypointMoved?.Invoke(new WaypointMovedEvent(edgeId, waypointIndex, x, y)) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnWaypointRemoved(string edgeId, int waypointIndex)
        => WaypointRemoved?.Invoke(new WaypointRemovedEvent(edgeId, waypointIndex)) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnGroupMembershipChanged(string nodeId, string? groupId)
        => GroupMembershipChanged?.Invoke(new GroupMembershipChangedEvent(nodeId, groupId)) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnGroupBoundsChanged(string groupId, double x, double y, double width, double height)
        => GroupBoundsChanged?.Invoke(new GroupBoundsChangedEvent(groupId, new DiagramBounds(x, y, width, height))) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnGroupCollapseChanged(string groupId, bool collapsed)
        => GroupCollapseChanged?.Invoke(new GroupCollapseChangedEvent(groupId, collapsed)) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnSelectionChanged(string kind, string? id, string[]? nodeIds, string[]? groupIds, string[]? edgeIds)
        => SelectionChanged?.Invoke(new SelectionChangedEvent(
            kind,
            id,
            nodeIds ?? [],
            groupIds ?? [],
            edgeIds ?? [])) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnViewportChanged(double zoom, double scrollLeft, double scrollTop)
        => ViewportChanged?.Invoke(new ViewportChangedEvent(new ViewportState(zoom, scrollLeft, scrollTop))) ?? Task.CompletedTask;

    [JSInvokable]
    public Task OnHotkeyPressed(string command)
        => HotkeyPressed?.Invoke(new HotkeyPressedEvent(command)) ?? Task.CompletedTask;
}
