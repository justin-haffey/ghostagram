using System.Collections.Immutable;
using Diagrams.Core.Models;
using Diagrams.Core.Routing;

namespace Editor.Client.Services;

public sealed partial class DiagramEditorState
{
    public async Task SaveSelectionToLibraryAsync(string name, string description, CancellationToken cancellationToken = default)
    {
        var libraryItem = BuildLibraryItem(name, description);
        if (libraryItem is null)
        {
            return;
        }

        await repository.SaveLibraryItemAsync(libraryItem, cancellationToken);
        await RefreshCatalogAsync(cancellationToken);
        NotifyChanged(save: false);
    }

    public async Task InsertLibraryItemAsync(string libraryItemId, CancellationToken cancellationToken = default)
    {
        var item = LibraryItems.FirstOrDefault(entry => entry.Id == libraryItemId)
            ?? (_clipboard is not null && _clipboard.Id == libraryItemId ? _clipboard : null);
        if (item is null)
        {
            return;
        }

        var zoom = Math.Max(Document.ViewportState.Zoom, 0.1);
        var insertionX = (Document.ViewportState.ScrollLeft / zoom) + 220;
        var insertionY = (Document.ViewportState.ScrollTop / zoom) + 180;

        Mutate(current =>
        {
            var nodeIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var portIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var groupIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var nodes = item.Fragment.Nodes.Select(node =>
            {
                var newId = $"node-{Guid.NewGuid():N}";
                nodeIdMap[node.Id] = newId;
                return node with
                {
                    Id = newId,
                    Bounds = node.Bounds with { X = node.Bounds.X + insertionX, Y = node.Bounds.Y + insertionY },
                    LayerId = ActiveLayerId,
                    PortIds = node.PortIds.Select(portId =>
                    {
                        var nextPortId = $"port-{Guid.NewGuid():N}";
                        portIdMap[portId] = nextPortId;
                        return nextPortId;
                    }).ToImmutableArray()
                };
            }).ToImmutableArray();

            var groups = item.Fragment.Groups.Select(group =>
            {
                var newId = $"group-{Guid.NewGuid():N}";
                groupIdMap[group.Id] = newId;
                return group with
                {
                    Id = newId,
                    Bounds = group.Bounds with { X = group.Bounds.X + insertionX, Y = group.Bounds.Y + insertionY },
                    LayerId = ActiveLayerId,
                    ChildNodeIds = group.ChildNodeIds.Select(childId => nodeIdMap.GetValueOrDefault(childId, childId))
                        .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)
                };
            }).ToImmutableArray();

            var ports = item.Fragment.Ports.Select(port => port with
            {
                Id = portIdMap.GetValueOrDefault(port.Id, $"port-{Guid.NewGuid():N}"),
                NodeId = nodeIdMap.GetValueOrDefault(port.NodeId, port.NodeId)
            }).ToImmutableArray();

            var edges = item.Fragment.Edges.Select(edge => edge with
            {
                Id = $"edge-{Guid.NewGuid():N}",
                SourcePortId = portIdMap.GetValueOrDefault(edge.SourcePortId, edge.SourcePortId),
                TargetPortId = portIdMap.GetValueOrDefault(edge.TargetPortId, edge.TargetPortId),
                LayerId = ActiveLayerId,
                Waypoints = EdgeRouteResolver.OffsetWaypoints(edge.Waypoints, insertionX, insertionY)
            }).ToImmutableArray();

            var rebasedNodes = nodes.Select(node => node with
            {
                GroupId = node.GroupId is null ? null : groupIdMap.GetValueOrDefault(node.GroupId, node.GroupId)
            }).ToImmutableArray();

            return current with
            {
                Nodes = current.Nodes.AddRange(rebasedNodes),
                Ports = current.Ports.AddRange(ports),
                Edges = current.Edges.AddRange(edges),
                Groups = current.Groups.AddRange(groups)
            };
        });

        await SaveAsync(cancellationToken);
    }

    public Task AlignSelectionAsync(string mode)
    {
        if (SelectedNodes.Count < 2)
        {
            return Task.CompletedTask;
        }

        Mutate(document =>
        {
            var nodes = SelectedNodes.ToArray();
            var reference = mode switch
            {
                "left" => nodes.Min(node => node.Bounds.X),
                "right" => nodes.Max(node => node.Bounds.Right),
                "top" => nodes.Min(node => node.Bounds.Y),
                "bottom" => nodes.Max(node => node.Bounds.Bottom),
                _ => 0
            };

            return document with
            {
                Nodes = document.Nodes.Select(node =>
                {
                    if (!SelectedNodeIds.Contains(node.Id))
                    {
                        return node;
                    }

                    return mode switch
                    {
                        "left" => node with { Bounds = node.Bounds with { X = reference } },
                        "right" => node with { Bounds = node.Bounds with { X = reference - node.Bounds.Width } },
                        "top" => node with { Bounds = node.Bounds with { Y = reference } },
                        "bottom" => node with { Bounds = node.Bounds with { Y = reference - node.Bounds.Height } },
                        _ => node
                    };
                }).ToImmutableArray()
            };
        });

        return Task.CompletedTask;
    }

    public Task DistributeSelectionAsync(string axis)
    {
        if (SelectedNodes.Count < 3)
        {
            return Task.CompletedTask;
        }

        Mutate(document =>
        {
            var nodes = axis == "horizontal"
                ? SelectedNodes.OrderBy(node => node.Bounds.X).ToArray()
                : SelectedNodes.OrderBy(node => node.Bounds.Y).ToArray();

            var first = axis == "horizontal" ? nodes.First().Bounds.X : nodes.First().Bounds.Y;
            var last = axis == "horizontal" ? nodes.Last().Bounds.X : nodes.Last().Bounds.Y;
            var step = (last - first) / Math.Max(1, nodes.Length - 1);

            return document with
            {
                Nodes = document.Nodes.Select(node =>
                {
                    var index = Array.FindIndex(nodes, candidate => candidate.Id == node.Id);
                    if (index < 0)
                    {
                        return node;
                    }

                    return axis == "horizontal"
                        ? node with { Bounds = node.Bounds with { X = first + (step * index) } }
                        : node with { Bounds = node.Bounds with { Y = first + (step * index) } };
                }).ToImmutableArray()
            };
        });

        return Task.CompletedTask;
    }

    public Task AddLayerAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.CompletedTask;
        }

        var layer = new LayerDefinition($"layer-{Guid.NewGuid():N}", name, true, false, Document.Layers.Length);
        Mutate(document => document with { Layers = document.Layers.Add(layer) });
        ActiveLayerId = layer.Id;
        NotifyChanged(save: false);
        return Task.CompletedTask;
    }

    public Task ToggleLayerVisibilityAsync(string layerId)
    {
        Mutate(document => document with
        {
            Layers = document.Layers.Select(layer => layer.Id == layerId ? layer with { IsVisible = !layer.IsVisible } : layer).ToImmutableArray()
        });

        return Task.CompletedTask;
    }

    public Task ToggleLayerLockAsync(string layerId)
    {
        Mutate(document => document with
        {
            Layers = document.Layers.Select(layer => layer.Id == layerId ? layer with { IsLocked = !layer.IsLocked } : layer).ToImmutableArray()
        });

        return Task.CompletedTask;
    }

    public Task BringSelectionForwardAsync()
    {
        Mutate(document => document with
        {
            Nodes = document.Nodes.Select(node => SelectedNodeIds.Contains(node.Id) ? node with { ZIndex = node.ZIndex + 1 } : node).ToImmutableArray(),
            Groups = document.Groups.Select(group => SelectedGroupIds.Contains(group.Id) ? group with { ZIndex = group.ZIndex + 1 } : group).ToImmutableArray()
        });

        return Task.CompletedTask;
    }

    public Task SendSelectionBackwardAsync()
    {
        Mutate(document => document with
        {
            Nodes = document.Nodes.Select(node => SelectedNodeIds.Contains(node.Id) ? node with { ZIndex = Math.Max(0, node.ZIndex - 1) } : node).ToImmutableArray(),
            Groups = document.Groups.Select(group => SelectedGroupIds.Contains(group.Id) ? group with { ZIndex = Math.Max(0, group.ZIndex - 1) } : group).ToImmutableArray()
        });

        return Task.CompletedTask;
    }

    public Task AddCommentAsync(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        var thread = new CommentThread(
            $"thread-{Guid.NewGuid():N}",
            ResolvePrimaryTarget(),
            string.IsNullOrWhiteSpace(title) ? "Review Comment" : title,
            [new CommentEntry($"comment-{Guid.NewGuid():N}", "Designer", message, DateTimeOffset.UtcNow)],
            CommentStatus.Open);

        Mutate(document => document with { CommentThreads = document.CommentThreads.Add(thread) });
        return Task.CompletedTask;
    }

    public Task AddAnnotationAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        var annotation = new DiagramAnnotation(
            $"annotation-{Guid.NewGuid():N}",
            ResolvePrimaryTarget(),
            text,
            DateTimeOffset.UtcNow,
            "Designer");

        Mutate(document => document with { Annotations = document.Annotations.Add(annotation) });
        return Task.CompletedTask;
    }

    public Task ResolveCommentAsync(string threadId)
    {
        Mutate(document => document with
        {
            CommentThreads = document.CommentThreads.Select(thread => thread.Id == threadId ? thread with { Status = CommentStatus.Resolved } : thread).ToImmutableArray()
        });

        return Task.CompletedTask;
    }

    private LibraryItemDefinition? BuildLibraryItem(string name, string description)
    {
        var selectedNodeIds = SelectedNodeIds.Union(SelectedGroups.SelectMany(group => group.ChildNodeIds))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedGroups = Document.Groups.Where(group => SelectedGroupIds.Contains(group.Id)).ToImmutableArray();
        var selectedNodes = Document.Nodes.Where(node => selectedNodeIds.Contains(node.Id)).ToImmutableArray();

        if (selectedNodes.Length == 0 && selectedGroups.Length == 0)
        {
            return null;
        }

        var selectedPorts = Document.Ports.Where(port => selectedNodeIds.Contains(port.NodeId)).ToImmutableArray();
        var selectedPortIds = selectedPorts.Select(port => port.Id).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var left = new[] { selectedNodes.Any() ? selectedNodes.Min(node => node.Bounds.X) : double.MaxValue, selectedGroups.Any() ? selectedGroups.Min(group => group.Bounds.X) : double.MaxValue }.Min();
        var top = new[] { selectedNodes.Any() ? selectedNodes.Min(node => node.Bounds.Y) : double.MaxValue, selectedGroups.Any() ? selectedGroups.Min(group => group.Bounds.Y) : double.MaxValue }.Min();
        var selectedEdges = Document.Edges
            .Where(edge => selectedPortIds.Contains(edge.SourcePortId) && selectedPortIds.Contains(edge.TargetPortId))
            .Select(edge => edge with
            {
                Waypoints = EdgeRouteResolver.OffsetWaypoints(edge.Waypoints, -left, -top)
            })
            .ToImmutableArray();

        var fragment = new DiagramDocument
        {
            DocumentId = $"fragment-{Guid.NewGuid():N}",
            Metadata = DiagramMetadata.Create(name, description),
            TemplateKind = Document.TemplateKind,
            Nodes = selectedNodes.Select(node => node with
            {
                Bounds = node.Bounds with { X = node.Bounds.X - left, Y = node.Bounds.Y - top }
            }).ToImmutableArray(),
            Ports = selectedPorts,
            Edges = selectedEdges,
            Groups = selectedGroups.Select(group => group with
            {
                Bounds = group.Bounds with { X = group.Bounds.X - left, Y = group.Bounds.Y - top }
            }).ToImmutableArray(),
            Layers = Document.Layers.Where(layer =>
                    selectedNodes.Any(node => node.LayerId == layer.Id)
                    || selectedGroups.Any(group => group.LayerId == layer.Id)
                    || selectedEdges.Any(edge => edge.LayerId == layer.Id))
                .ToImmutableArray(),
            Styles = Document.Styles
        };

        return new LibraryItemDefinition(
            $"library-{Guid.NewGuid():N}",
            name,
            description,
            Document.TemplateKind,
            DateTimeOffset.UtcNow,
            fragment);
    }
}
