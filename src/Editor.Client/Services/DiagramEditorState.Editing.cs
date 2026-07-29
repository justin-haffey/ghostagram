using System.Collections.Immutable;
using Diagrams.Core.Commands;
using Diagrams.Core.Models;
using Diagrams.Core.Routing;
using Diagrams.Interop.JsPlumb;

namespace Editor.Client.Services;

public sealed partial class DiagramEditorState
{
    public Task AddNodeAsync(string stencilKey)
    {
        var created = stencils.CreateInstance(
            stencilKey,
            (Document.ViewportState.ScrollLeft / Math.Max(Document.ViewportState.Zoom, 0.1)) + 240,
            (Document.ViewportState.ScrollTop / Math.Max(Document.ViewportState.Zoom, 0.1)) + 160);

        Mutate(current => current with
        {
            Nodes = current.Nodes.Add(created.Node with { LayerId = ActiveLayerId, ZIndex = current.Nodes.Length + 1 }),
            Ports = current.Ports.AddRange(created.Ports)
        });

        SetSelection("node", created.Node.Id, [created.Node.Id], [], []);
        return Task.CompletedTask;
    }

    public Task AddGroupAsync()
    {
        var zoom = Math.Max(Document.ViewportState.Zoom, 0.1);
        var group = new DiagramGroup(
            $"group-{Guid.NewGuid():N}",
            "New Group",
            new DiagramBounds((Document.ViewportState.ScrollLeft / zoom) + 160, (Document.ViewportState.ScrollTop / zoom) + 140, 520, 320),
            false,
            new FreeformLayoutSpec(),
            ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase))
        {
            LayerId = ActiveLayerId,
            ZIndex = Document.Groups.Length + 1
        };

        Mutate(current => current with { Groups = current.Groups.Add(group) });
        SetSelection("group", group.Id, [], [group.Id], []);
        return Task.CompletedTask;
    }

    public void Select(SelectionChangedEvent selection)
    {
        var nodeIds = selection.NodeIds.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var groupIds = selection.GroupIds.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var edgeIds = selection.EdgeIds.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        if (nodeIds.Count == 0 && groupIds.Count == 0 && edgeIds.Count == 0 && !string.IsNullOrWhiteSpace(selection.Id))
        {
            switch (selection.Kind)
            {
                case "node":
                    nodeIds = [selection.Id];
                    break;
                case "group":
                    groupIds = [selection.Id];
                    break;
                case "edge":
                    edgeIds = [selection.Id];
                    break;
            }
        }

        SetSelection(selection.Kind, selection.Id, nodeIds, groupIds, edgeIds);
    }

    public void MoveNode(NodeMovedEvent moved)
    {
        var node = Document.FindNode(moved.NodeId);
        var bounds = SnapBounds(moved.Bounds);
        if (node is null || node.Bounds == bounds)
        {
            return;
        }

        Apply(new MoveNodeCommand(moved.NodeId, bounds), save: false);
    }

    public void UpdateGroupMembership(GroupMembershipChangedEvent moved)
        => Apply(new SetNodeGroupCommand(moved.NodeId, moved.GroupId), save: false);

    public void UpdateGroupBounds(GroupBoundsChangedEvent changed)
    {
        var group = Document.FindGroup(changed.GroupId);
        var bounds = SnapBounds(changed.Bounds);
        if (group is null || group.Bounds == bounds)
        {
            return;
        }

        Apply(new SetGroupBoundsCommand(changed.GroupId, bounds), save: false);
    }

    public void UpdateGroupCollapse(GroupCollapseChangedEvent changed)
        => Apply(new SetGroupCollapsedCommand(changed.GroupId, changed.Collapsed), save: false);

    public void UpdateViewport(ViewportChangedEvent changed)
    {
        var current = Document.ViewportState;
        var updated = changed.ViewportState;
        if (Math.Abs(current.Zoom - updated.Zoom) < 0.001
            && Math.Abs(current.ScrollLeft - updated.ScrollLeft) < 0.5
            && Math.Abs(current.ScrollTop - updated.ScrollTop) < 0.5)
        {
            return;
        }

        Apply(new SetViewportCommand(updated), save: false, trackHistory: false);
    }

    public void Connect(EdgeCreatedEvent created)
    {
        var sourcePort = Document.FindPort(created.SourcePortId);
        var targetPort = Document.FindPort(created.TargetPortId);
        if (sourcePort is null || targetPort is null)
        {
            return;
        }

        var sourceNode = Document.FindNode(sourcePort.NodeId);
        var edge = new DiagramEdge(
            $"edge-{Guid.NewGuid():N}",
            created.SourcePortId,
            created.TargetPortId,
            ResolveConnector(Document.TemplateKind),
            string.Empty,
            ResolveMarkers(sourcePort.Role, targetPort.Role),
            ResolveAnimation(Document.TemplateKind),
            sourceNode?.StyleToken ?? "accent")
        {
            LayerId = sourceNode?.LayerId ?? ActiveLayerId
        };

        Mutate(current =>
        {
            if (current.Edges.Any(existing => existing.SourcePortId == edge.SourcePortId && existing.TargetPortId == edge.TargetPortId))
            {
                return current;
            }

            return current with { Edges = current.Edges.Add(edge) };
        });

        SetSelection("edge", edge.Id, [], [], [edge.Id]);
    }

    public void RelabelEdge(EdgeRelabeledEvent relabeled)
        => Apply(new SetEdgeLabelCommand(relabeled.EdgeId, relabeled.Label), save: false);

    public void DeleteEdge(EdgeDeletedEvent deleted)
        => Apply(new RemoveEdgeCommand(deleted.EdgeId), save: false);

    public void InsertWaypoint(WaypointInsertedEvent inserted)
        => Apply(
            new InsertEdgeWaypointCommand(
                inserted.EdgeId,
                inserted.SegmentIndex,
                SnapPoint(new DiagramPoint(inserted.X, inserted.Y))),
            save: false);

    public void MoveWaypoint(WaypointMovedEvent moved)
        => Apply(
            new MoveEdgeWaypointCommand(
                moved.EdgeId,
                moved.WaypointIndex,
                SnapPoint(new DiagramPoint(moved.X, moved.Y))),
            save: false);

    public void RemoveWaypoint(WaypointRemovedEvent removed)
        => Apply(new RemoveEdgeWaypointCommand(removed.EdgeId, removed.WaypointIndex), save: false);

    public Task UpdateNodeLabelAsync(string label)
    {
        if (SelectedNode is not null)
        {
            Apply(new UpdateNodeLabelCommand(SelectedNode.Id, label), save: false);
        }

        return Task.CompletedTask;
    }

    public Task UpdateNodePropertyAsync(string key, string? value)
    {
        if (SelectedNode is not null)
        {
            Apply(new SetNodePropertyCommand(SelectedNode.Id, key, value), save: false);
        }

        return Task.CompletedTask;
    }

    public Task UpdateSelectionTagsAsync(string tags)
    {
        var parsed = ParseTags(tags);
        Mutate(document => document with
        {
            Nodes = document.Nodes.Select(node => SelectedNodeIds.Contains(node.Id) ? node with { Tags = parsed } : node).ToImmutableArray(),
            Groups = document.Groups.Select(group => SelectedGroupIds.Contains(group.Id) ? group with { Tags = parsed } : group).ToImmutableArray(),
            Edges = document.Edges.Select(edge => SelectedEdgeIds.Contains(edge.Id) ? edge with { Tags = parsed } : edge).ToImmutableArray()
        });

        return Task.CompletedTask;
    }

    public Task UpdateSelectionLinkAsync(string? linkUri)
    {
        Mutate(document => document with
        {
            Nodes = document.Nodes.Select(node => SelectedNodeIds.Contains(node.Id) ? node with { LinkUri = linkUri } : node).ToImmutableArray(),
            Groups = document.Groups.Select(group => SelectedGroupIds.Contains(group.Id) ? group with { LinkUri = linkUri } : group).ToImmutableArray(),
            Edges = document.Edges.Select(edge => SelectedEdgeIds.Contains(edge.Id) ? edge with { LinkUri = linkUri } : edge).ToImmutableArray()
        });

        return Task.CompletedTask;
    }

    public Task ApplyLayoutAsync(LayoutKind kind)
    {
        Apply(new ApplyLayoutCommand(LayoutSpecDefaults.For(kind), SelectedGroup?.Id), save: false);
        return Task.CompletedTask;
    }

    public Task ClearSelectedEdgeWaypointsAsync()
    {
        if (SelectedEdge is not null)
        {
            Apply(new ClearEdgeWaypointsCommand(SelectedEdge.Id), save: false);
        }

        return Task.CompletedTask;
    }

    public Task UndoAsync()
    {
        var document = history.Undo();
        if (document is not null)
        {
            ReplaceStore(document, resetHistory: false);
            UpdateDerivedState();
            ScheduleAutosave();
            NotifyChanged(save: false);
        }

        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        var document = history.Redo();
        if (document is not null)
        {
            ReplaceStore(document, resetHistory: false);
            UpdateDerivedState();
            ScheduleAutosave();
            NotifyChanged(save: false);
        }

        return Task.CompletedTask;
    }

    public Task DeleteSelectionAsync()
    {
        if (SelectedNodeIds.Count == 0 && SelectedEdgeIds.Count == 0 && SelectedGroupIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        Mutate(current =>
        {
            var removedNodeIds = SelectedNodeIds;
            var removedGroupIds = SelectedGroupIds;
            var removedPortIds = current.Ports.Where(port => removedNodeIds.Contains(port.NodeId)).Select(port => port.Id).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

            return current with
            {
                Nodes = current.Nodes.Where(node => !removedNodeIds.Contains(node.Id)).ToImmutableArray(),
                Ports = current.Ports.Where(port => !removedPortIds.Contains(port.Id)).ToImmutableArray(),
                Edges = current.Edges.Where(edge =>
                        !SelectedEdgeIds.Contains(edge.Id)
                        && !removedPortIds.Contains(edge.SourcePortId)
                        && !removedPortIds.Contains(edge.TargetPortId))
                    .ToImmutableArray(),
                Groups = current.Groups.Where(group => !removedGroupIds.Contains(group.Id)).Select(group => group with
                {
                    ChildNodeIds = group.ChildNodeIds.Except(removedNodeIds).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)
                }).ToImmutableArray(),
                CommentThreads = current.CommentThreads.Where(thread => thread.Target.Id is null || !removedNodeIds.Contains(thread.Target.Id)).ToImmutableArray(),
                Annotations = current.Annotations.Where(annotation => annotation.Target.Id is null || !removedNodeIds.Contains(annotation.Target.Id)).ToImmutableArray()
            };
        });

        ClearSelection();
        NotifyChanged(save: false);
        return Task.CompletedTask;
    }

    public Task CopySelectionAsync()
    {
        _clipboard = BuildLibraryItem("Clipboard Selection", "Internal clipboard");
        return Task.CompletedTask;
    }

    public async Task DuplicateSelectionAsync()
    {
        await CopySelectionAsync();
        await PasteAsync();
    }

    public async Task PasteAsync()
    {
        if (_clipboard is null)
        {
            return;
        }

        await InsertLibraryItemAsync(_clipboard.Id);
    }

    public Task SelectAllAsync()
    {
        SetSelection(
            "node",
            Document.Nodes.FirstOrDefault()?.Id,
            Document.Nodes.Select(node => node.Id).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            Document.Groups.Select(group => group.Id).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            Document.Edges.Select(edge => edge.Id).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public Task NudgeSelectionAsync(double x, double y)
    {
        if (SelectedNodeIds.Count == 0 && SelectedGroupIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        Mutate(document => document with
        {
            Nodes = document.Nodes.Select(node => SelectedNodeIds.Contains(node.Id)
                ? node with { Bounds = SnapBounds(node.Bounds with { X = node.Bounds.X + x, Y = node.Bounds.Y + y }) }
                : node).ToImmutableArray(),
            Groups = document.Groups.Select(group => SelectedGroupIds.Contains(group.Id)
                ? group with { Bounds = SnapBounds(group.Bounds with { X = group.Bounds.X + x, Y = group.Bounds.Y + y }) }
                : group).ToImmutableArray(),
            Edges = document.Edges.Select(edge =>
            {
                if (edge.Waypoints.Length == 0)
                {
                    return edge;
                }

                var sourceNodeId = document.FindNodeIdByPort(edge.SourcePortId);
                var targetNodeId = document.FindNodeIdByPort(edge.TargetPortId);
                return sourceNodeId is not null
                       && targetNodeId is not null
                       && SelectedNodeIds.Contains(sourceNodeId)
                       && SelectedNodeIds.Contains(targetNodeId)
                    ? edge with { Waypoints = EdgeRouteResolver.OffsetWaypoints(edge.Waypoints, x, y) }
                    : edge;
            }).ToImmutableArray()
        });

        return Task.CompletedTask;
    }
}
