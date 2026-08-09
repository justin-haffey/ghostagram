using System.Collections.Immutable;
using System.Text.Json;
using Ghostagram.Blazor;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Ghostagram.Execution;
using Ghostagram.Server.Layout;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Ghostagram.Server.Components.Pages;

public partial class Home : IAsyncDisposable
{
    private const string DocumentId = "laboratory-design";
    private const string ActorId = "ghostagram-laboratory";
    private const int GridSize = 16;
    private const int MaxDesignedProperties = 17;

    [Inject] private DiagramCommandService Commands { get; set; } = default!;
    [Inject] private DiagramLayoutService Layouts { get; set; } = default!;
    [Inject] private INodeTypeRegistry NodeTypes { get; set; } = default!;
    [Inject] private INodeFactory NodeFactory { get; set; } = default!;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GhostDiagramOptions _options = new(Height: "100%", GridSize: GridSize, MinZoom: .25, MaxZoom: 2.5, RespectReducedMotion: false, ModulePath: "/ghostagram/ghostagram.js?v=20260808.3");
    private readonly List<PaletteCategory> _paletteCategories = CreatePaletteCategories();
    private readonly List<NodeTemplate> _templates = CreateBuiltInTemplates();
    private readonly List<DraftPort> _draftPorts = [];
    private readonly List<DraftProperty> _draftProperties = [];
    private readonly HashSet<string> _expandedCategories = new(StringComparer.Ordinal) { "Workflow" };
    private readonly Stack<DiagramDocument> _undo = new();
    private readonly Stack<DiagramDocument> _redo = new();
    private readonly SemaphoreSlim _eventGate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);

    private DiagramDocument _document = EmptyDocument();
    private NodeDraft _draft = new();
    private NodeStyleDraft _styleDraft = new();
    private GhostDiagram? _diagram;
    private GhostPalette? _palette;
    private ElementReference _canvasDropZone;
    private ElementReference _canvasDropOverlay;
    private bool _paletteTargetAttached;
    private bool _paletteOpen = true;
    private bool _palettePinned = true;
    private bool _isDesignerOpen;
    private bool _isPaletteGroupEditorOpen;
    private bool _isStyleEditorOpen;
    private bool _isExportOpen;
    private bool _busy;
    private string _activity = "Loading saved design…";
    private string _layoutDirection = "right";
    private string _edgeConnector = "flowchart";
    private bool _edgeAnimated;
    private string? _exportedSvg;
    private string _paletteGroupName = string.Empty;
    private string? _styleNodeId;
    private long _revision;
    private int _templateSequence;
    private int _portSequence;
    private int _propertySequence;
    private long _lastBrowserEventId;

    protected override async Task OnInitializedAsync()
    {
        AddRegisteredNodeSets();
        await LoadOrCreateDocumentAsync();
        SyncEdgeControlsFromDocument();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _paletteTargetAttached || _palette is null) return;
        try
        {
            await _palette.AttachDropTargetAsync(_canvasDropZone, _canvasDropOverlay);
            _paletteTargetAttached = true;
        }
        catch (Exception exception)
        {
            _activity = $"Palette drag setup failed: {exception.Message}";
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadOrCreateDocumentAsync()
    {
        try
        {
            var snapshot = await Commands.GetSnapshotAsync(DocumentId, CancellationToken.None);
            if (snapshot is not null)
            {
                _document = Deserialize(snapshot);
                _revision = snapshot.Revision;
                await RepairSavedIconsAsync();
            }

            if (snapshot is null || IsEmpty(_document))
            {
                var starter = CreateStarterDocument();
                var operations = OperationsToTransform(_document, starter);
                await SubmitOperationsAsync(operations, "Created the starter design", recordHistory: false);
            }
            else
            {
                _activity = "Loaded saved design";
            }
        }
        catch (Exception exception)
        {
            _document = CreateStarterDocument();
            _revision = 0;
            _activity = $"Opened a recoverable local design: {exception.Message}";
        }
    }

    private async Task RepairSavedIconsAsync()
    {
        var operations = new List<GhostagramOperation>();
        foreach (var node in _document.Nodes)
        {
            var icon = NormalizeIcon(node.Icon);
            if (!string.Equals(icon, node.Icon, StringComparison.Ordinal)) operations.Add(DiagramOperations.Upsert(node with { Icon = icon }));
        }
        foreach (var group in _document.Groups)
        {
            var icon = NormalizeIcon(group.Icon);
            if (!string.Equals(icon, group.Icon, StringComparison.Ordinal)) operations.Add(DiagramOperations.Upsert(group with { Icon = icon }));
        }
        if (operations.Count > 0) await SubmitOperationsAsync(operations, "Repaired saved node metadata", false);
    }

    private void TogglePalette() => _paletteOpen = !_paletteOpen;

    private void TogglePalettePin()
    {
        _palettePinned = !_palettePinned;
        _paletteOpen = true;
    }

    private void SetCategoryExpanded(string category, bool expanded)
    {
        if (expanded) _expandedCategories.Add(category);
        else _expandedCategories.Remove(category);
    }

    private void SetCategoryExpanded(GhostPaletteGroupToggleRequest request) => SetCategoryExpanded(request.GroupId, request.Expanded);

    private void OpenDesigner()
    {
        if (!_paletteCategories.Any(category => string.Equals(category.Name, _draft.Category, StringComparison.Ordinal)))
            _draft.Category = "Custom";
        _isDesignerOpen = true;
    }
    private void CloseDesigner() => _isDesignerOpen = false;
    private void OpenPaletteGroupEditor()
    {
        _paletteGroupName = string.Empty;
        _isPaletteGroupEditorOpen = true;
    }
    private void ClosePaletteGroupEditor() => _isPaletteGroupEditorOpen = false;
    private void CloseStyleEditor() => _isStyleEditorOpen = false;
    private void CloseExport() => _isExportOpen = false;

    private bool HasSelectedGroups => _document.Groups.Any(group => _document.Selection.Contains(group.Id, StringComparer.Ordinal));
    private bool HasSelectedEdges => _document.Edges.Any(edge => _document.Selection.Contains(edge.Id, StringComparer.Ordinal));
    private bool CanEditSelectedNode => SelectedNode() is not null;
    private string EdgeApplyLabel => HasSelectedEdges ? "Apply selected" : "Apply all edges";
    private string StyleNodeLabel => _document.Nodes.SingleOrDefault(node => node.Id == _styleNodeId)?.Label ?? "Node style";
    private IReadOnlyList<GhostPaletteGroup> PaletteGroups => _paletteCategories
        .Select(category => new GhostPaletteGroup(category.Name, category.Name, _expandedCategories.Contains(category.Name), !category.IsSystem))
        .ToArray();
    private IReadOnlyList<GhostPaletteItem> PaletteItems => _templates
        .Select(template => new GhostPaletteItem(template.Id, template.Category, template.Label, template.Description, template.Outline, template.IsCustom))
        .ToArray();

    private DiagramNode? SelectedNode()
    {
        if (_document.Selection.Count != 1) return null;
        var selected = _document.Selection.ToHashSet(StringComparer.Ordinal);
        return _document.Nodes.SingleOrDefault(node => selected.Contains(node.Id));
    }

    private void AddPaletteGroup()
    {
        var name = _paletteGroupName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _activity = "Enter a name for the palette group";
            return;
        }
        if (_paletteCategories.Any(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            _activity = $"A palette group named {name} already exists";
            return;
        }

        var customIndex = _paletteCategories.FindIndex(category => category.Name == "Custom");
        _paletteCategories.Insert(customIndex < 0 ? _paletteCategories.Count : customIndex, new PaletteCategory(name, false));
        _expandedCategories.Add(name);
        _draft.Category = name;
        _isPaletteGroupEditorOpen = false;
        _paletteOpen = true;
        _activity = $"Added palette group {name}";
    }

    private void RemovePaletteGroup(string name)
    {
        var category = _paletteCategories.SingleOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (category is null || category.IsSystem) return;

        var moved = 0;
        for (var index = 0; index < _templates.Count; index++)
        {
            if (!string.Equals(_templates[index].Category, name, StringComparison.Ordinal)) continue;
            _templates[index] = _templates[index] with { Category = "Custom" };
            moved++;
        }
        _paletteCategories.Remove(category);
        _expandedCategories.Remove(name);
        _expandedCategories.Add("Custom");
        if (string.Equals(_draft.Category, name, StringComparison.Ordinal)) _draft.Category = "Custom";
        _activity = moved == 0
            ? $"Removed empty palette group {name}"
            : $"Removed palette group {name} and moved {moved} node type{(moved == 1 ? string.Empty : "s")} to Custom";
    }

    private Task MovePaletteItemAsync(GhostPaletteMoveRequest request)
    {
        var templateId = request.ItemId;
        var categoryName = request.GroupId;
        var index = _templates.FindIndex(item => item.Id == templateId);
        if (index < 0 || !_paletteCategories.Any(category => category.Name == categoryName)) return Task.CompletedTask;
        if (_templates[index].Category == categoryName) return Task.CompletedTask;

        var label = _templates[index].Label;
        _templates[index] = _templates[index] with { Category = categoryName };
        _expandedCategories.Add(categoryName);
        _activity = $"Moved {label} to {categoryName}";
        return InvokeAsync(StateHasChanged);
    }

    private void DeleteCustomTemplate(string templateId)
    {
        var template = _templates.SingleOrDefault(item => item.Id == templateId && item.IsCustom);
        if (template is null) return;
        _templates.Remove(template);
        _activity = $"Deleted custom node type {template.Label} from the palette";
    }

    private void OpenNodeStyleEditor()
    {
        var node = SelectedNode();
        if (node is null) return;
        var style = node.Style ?? new DiagramNodeStyle();
        _styleNodeId = node.Id;
        _styleDraft = new NodeStyleDraft
        {
            Background = NormalizeColor(style.Background, "#ffffff"),
            Outline = NormalizeColor(style.BorderColor, "#4177de"),
            TextColor = NormalizeColor(style.Color, "#172033"),
            TextAlign = NormalizeTextAlign(style.TextAlign)
        };
        _isStyleEditorOpen = true;
    }

    private async Task ApplyNodeStyleAsync()
    {
        var node = _document.Nodes.SingleOrDefault(item => item.Id == _styleNodeId);
        if (node is null)
        {
            _isStyleEditorOpen = false;
            return;
        }

        var style = new DiagramNodeStyle(
            NormalizeColor(_styleDraft.Background, "#ffffff"),
            NormalizeColor(_styleDraft.Outline, "#4177de"),
            NormalizeColor(_styleDraft.TextColor, "#172033"),
            NormalizeTextAlign(_styleDraft.TextAlign));
        if (await SubmitOperationsAsync([DiagramOperations.Upsert(node with { Style = style })], $"Updated {node.Label ?? "node"} style"))
            _isStyleEditorOpen = false;
    }

    private async Task AddGroupAsync()
    {
        if (_diagram is null || _busy) return;
        try
        {
            var center = await _diagram.CanvasCenterAsync();
            await AddPaletteItemAtCanvasPointAsync("group", center);
        }
        catch (Exception exception)
        {
            _activity = $"Unable to add a group: {exception.Message}";
        }
    }

    private async Task DropPaletteItemAsync(GhostPaletteDropRequest request)
    {
        if (_diagram is null) return;

        try
        {
            var hit = await _diagram.HitTestClientPointAsync(request.ClientX, request.ClientY);
            if (hit.Inside) await AddPaletteItemAtCanvasPointAsync(request.ItemId, new GhostagramCanvasPoint(hit.X, hit.Y));
        }
        catch (Exception exception)
        {
            _activity = $"Unable to add palette item: {exception.Message}";
            await ResynchronizeAsync();
        }
        finally
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task InvokePaletteItemAsync(string templateId)
    {
        if (_diagram is null) return;
        try
        {
            await AddPaletteItemAtCanvasPointAsync(templateId, await _diagram.CanvasCenterAsync());
        }
        catch (Exception exception)
        {
            _activity = $"Unable to add palette item: {exception.Message}";
        }
    }

    private async Task AddPaletteItemAtCanvasPointAsync(string templateId, GhostagramCanvasPoint point)
    {
        var template = _templates.SingleOrDefault(item => item.Id == templateId);
        if (template is null) return;

        if (template.IsGroup)
        {
            var group = new DiagramGroup(
                NextId("group"),
                Snap(point.X - template.Width / 2),
                Snap(point.Y - template.Height / 2),
                template.Width,
                template.Height,
                template.Label,
                Icon: template.Icon);
            await SubmitOperationsAsync([DiagramOperations.Upsert(group)], $"Added {template.Label}");
        }
        else
        {
            var id = NextId("node");
            var x = Snap(point.X - template.Width / 2);
            var y = Snap(point.Y - template.Height / 2);
            var groupId = DiagramGroupMembership.ResolveGroupId(_document.Groups, x, y, template.Width, template.Height);
            if (template.RegisteredTypeId is not null)
            {
                var created = NodeFactory.Create(new(id, template.RegisteredTypeId, x, y, template.RegisteredTypeVersion, GroupId: groupId));
                var styledPorts = created.Ports.Select(port => port with
                {
                    Endpoint = new DiagramEndpoint("dot", 10, template.Outline, "#ffffff", 2)
                });
                await SubmitOperationsAsync(
                    [DiagramOperations.Upsert(created.Node), .. styledPorts.Select(DiagramOperations.Upsert)],
                    $"Added {template.Label}");
            }
            else
            {
                var properties = template.Properties.Select(property => property.Property with { Value = property.Value?.Clone() }).ToArray();
                var node = new DiagramNode(id, x, y, template.Width, template.Height, template.Label, GroupId: groupId, Icon: template.Icon,
                    Style: new DiagramNodeStyle(template.Background, template.Outline, template.TextColor, template.TextAlign), Properties: properties);
                var ports = template.Ports.Select((port, index) => new DiagramPort(
                    $"{id}-{port.Id}-{index + 1}", id, port.Direction, Anchor: port.Side,
                    Endpoint: new DiagramEndpoint("dot", 11, template.Outline, "#ffffff", 2))).ToArray();
                var operations = new List<GhostagramOperation> { DiagramOperations.Upsert(node) };
                operations.AddRange(ports.Select(DiagramOperations.Upsert));
                operations.AddRange(properties.Where(property => property.Connectable).SelectMany((property, index) =>
                    PropertyPorts(id, property, template.Properties.Single(templateProperty => templateProperty.Id == property.Id).Direction, index, template.Outline)).Select(DiagramOperations.Upsert));
                await SubmitOperationsAsync(operations, $"Added {template.Label}");
            }
        }

        if (!_palettePinned) _paletteOpen = false;
    }

    private void AddConnectionPoint(string side) => _draftPorts.Add(new DraftPort($"draft-port-{++_portSequence}", side, _draft.Direction));
    private void AddTopConnectionPoint() => AddConnectionPoint("top");
    private void AddRightConnectionPoint() => AddConnectionPoint("right");
    private void AddBottomConnectionPoint() => AddConnectionPoint("bottom");
    private void AddLeftConnectionPoint() => AddConnectionPoint("left");
    private void RemoveConnectionPoint(string id) => _draftPorts.RemoveAll(port => port.Id == id);
    private void AddDraftProperty()
    {
        if (_draftProperties.Count >= MaxDesignedProperties)
        {
            _activity = $"Node types support up to {MaxDesignedProperties} visible properties.";
            return;
        }
        _draftProperties.Add(new DraftProperty($"property-{++_propertySequence}"));
    }
    private void RemoveDraftProperty(string id) => _draftProperties.RemoveAll(property => property.Id == id);

    private void AddCustomTemplate()
    {
        var label = string.IsNullOrWhiteSpace(_draft.Label) ? "Custom node" : _draft.Label.Trim();
        var outline = NormalizeColor(_draft.Outline, "#d24686");
        var background = NormalizeColor(_draft.Background, "#ffffff");
        var textColor = NormalizeColor(_draft.TextColor, "#172033");
        var textAlign = NormalizeTextAlign(_draft.TextAlign);
        var category = _paletteCategories.Any(item => item.Name == _draft.Category) ? _draft.Category : "Custom";
        var ports = _draftPorts.Select((port, index) => new PortTemplate($"port-{index + 1}", port.Side, port.Direction)).ToArray();
        var properties = _draftProperties.Select(CreatePropertyTemplate).ToArray();
        var minimumPropertyHeight = properties.Count(property => property.Mode != DiagramPropertyModes.Hidden) * 21 + 48;
        _templates.Add(new NodeTemplate($"custom-{++_templateSequence}", category, label, "Custom component",
            Math.Clamp(_draft.Width, 96, 360), Math.Clamp(Math.Max(_draft.Height, minimumPropertyHeight), 48, 420), outline, background, textColor, textAlign,
            ports, false, NormalizeIcon(_draft.Icon) ?? "mdi:shape-outline", true, properties));
        _draft = new NodeDraft();
        _draftPorts.Clear();
        _draftProperties.Clear();
        _isDesignerOpen = false;
        _paletteOpen = true;
        _expandedCategories.Add(category);
        _activity = $"Added {label} to {category}";
    }

    private async Task OnGhostagramEvent(GhostagramEvent envelope)
    {
        if (_diagram is null || envelope.Origin != "browser" || string.IsNullOrWhiteSpace(envelope.Type)) return;
        await _eventGate.WaitAsync();
        try
        {
            if (envelope.EventId <= _lastBrowserEventId) return;
            _lastBrowserEventId = envelope.EventId;
            await ProcessGhostagramEventAsync(envelope);
        }
        catch (Exception exception)
        {
            _activity = $"Recovered from an invalid browser change: {exception.Message}";
            await ResynchronizeAsync();
        }
        finally
        {
            _eventGate.Release();
        }
    }

    private async Task ProcessGhostagramEventAsync(GhostagramEvent envelope)
    {
        switch (envelope.Type)
        {
            case "selection.changed":
                var selection = StringArray(envelope.Payload, "ids");
                await SubmitOperationsAsync([DiagramOperations.Select(selection)], $"Selected {selection.Count} item{(selection.Count == 1 ? string.Empty : "s")}", false);
                SyncEdgeControlsFromSelection(selection);
                break;
            case "node.move.commit": await CommitNodeMoveAsync(envelope.Payload); break;
            case "node.resize.commit": await CommitNodeAsync(envelope.Payload, node => node with { Width = Number(envelope.Payload, "width"), Height = Number(envelope.Payload, "height") }, "Resized node"); break;
            case "node.rotate.commit": await CommitNodeAsync(envelope.Payload, node => node with { Rotation = Number(envelope.Payload, "rotation") }, "Rotated node"); break;
            case "node.label.commit": await CommitNodeAsync(envelope.Payload, node => node with { Label = NullableLabel(envelope.Payload, "label") }, "Updated node label"); break;
            case "node.property.commit": await CommitNodePropertyAsync(envelope.Payload); break;
            case "nodes.move.commit": await CommitNodePositionsAsync(envelope.Payload, "nodes", "Moved nodes"); break;
            case "selection.move.commit": await CommitNodeAndGroupPositionsAsync(envelope.Payload, "Moved selection"); break;
            case "group.move.commit": await CommitGroupMoveAsync(envelope.Payload); break;
            case "group.resize.commit": await CommitGroupAsync(envelope.Payload, group => group with { Width = Number(envelope.Payload, "width"), Height = Number(envelope.Payload, "height") }, "Resized group"); break;
            case "group.visibilityRequested": await SetGroupVisibilityAsync(envelope.Payload); break;
            case "group.label.commit": await CommitGroupAsync(envelope.Payload, group => group with { Label = NullableLabel(envelope.Payload, "label") }, "Updated group label"); break;
            case "edge.createRequested": await CreateEdgeAsync(envelope.Payload); break;
            case "edge.reconnectRequested": await ReconnectEdgeAsync(envelope.Payload); break;
            case "edge.label.commit": await CommitEdgeAsync(envelope.Payload, edge => edge with { Label = NullableLabel(envelope.Payload, "label") }, "Updated edge label"); break;
            case "edge.labelPosition.commit": await CommitEdgeAsync(envelope.Payload, edge => edge with { LabelOffsetX = Number(envelope.Payload, "labelOffsetX"), LabelOffsetY = Number(envelope.Payload, "labelOffsetY") }, "Moved edge label"); break;
            case "edge.waypointsRequested": await CommitEdgeAsync(envelope.Payload, edge => edge with { Waypoints = Points(envelope.Payload, "waypoints") }, "Updated edge waypoints"); break;
            case "edge.detachRequested": await DeleteEdgesAsync([String(envelope.Payload, "edgeId")]); break;
            case "selection.deleteRequested": await DeleteSelectionAsync(envelope.Payload); break;
            case "viewport.changed":
                var viewport = Viewport(envelope.Payload);
                if (viewport != _document.Viewport)
                    await SubmitOperationsAsync([DiagramOperations.SetViewport(viewport)], "Updated viewport", false);
                break;
            case "history.undoRequested": await UndoAsync(); break;
            case "history.redoRequested": await RedoAsync(); break;
        }
    }

    private async Task CommitNodeAsync(JsonElement payload, Func<DiagramNode, DiagramNode> update, string activity)
    {
        var current = _document.Nodes.SingleOrDefault(node => node.Id == String(payload, "nodeId"));
        if (current is not null) await SubmitOperationsAsync([DiagramOperations.Upsert(update(current))], activity);
    }

    private async Task CommitNodePropertyAsync(JsonElement payload)
    {
        var node = _document.Nodes.SingleOrDefault(item => item.Id == String(payload, "nodeId"));
        var propertyId = String(payload, "propertyId");
        if (node is null || string.IsNullOrWhiteSpace(propertyId) || !payload.TryGetProperty("value", out var value)) return;
        var properties = node.Properties.Select(property => property.Id == propertyId ? property with { Value = value.Clone() } : property).ToArray();
        if (properties.SequenceEqual(node.Properties)) return;
        await SubmitOperationsAsync([DiagramOperations.Upsert(node with { Properties = properties })], $"Updated {properties.Single(property => property.Id == propertyId).Label ?? propertyId}");
    }

    private async Task CommitNodeMoveAsync(JsonElement payload)
    {
        var current = _document.Nodes.SingleOrDefault(node => node.Id == String(payload, "nodeId"));
        if (current is null) return;
        var hasGroupId = TryNullableString(payload, "groupId", out var groupId);
        var operations = new List<GhostagramOperation>
        {
            DiagramOperations.Upsert(current with { X = Number(payload, "x"), Y = Number(payload, "y") })
        };
        AddNodeGroupAssignment(operations, current, groupId, hasGroupId);
        await SubmitOperationsAsync(operations, "Moved node");
    }

    private async Task CommitNodePositionsAsync(JsonElement payload, string property, string activity)
    {
        var updates = NodePositions(payload, property).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var operations = new List<GhostagramOperation>();
        AddNodePositionOperations(operations, updates);
        if (operations.Count > 0) await SubmitOperationsAsync(operations, activity);
    }

    private async Task CommitGroupAsync(JsonElement payload, Func<DiagramGroup, DiagramGroup> update, string activity)
    {
        var current = _document.Groups.SingleOrDefault(group => group.Id == String(payload, "groupId"));
        if (current is not null) await SubmitOperationsAsync([DiagramOperations.Upsert(update(current))], activity);
    }

    private Task SetGroupVisibilityAsync(JsonElement payload)
    {
        var hidden = Boolean(payload, "hidden");
        return CommitGroupAsync(payload, group => group with { Collapsed = hidden }, hidden ? "Hid group contents" : "Showed group contents");
    }

    private async Task CommitNodeAndGroupPositionsAsync(JsonElement payload, string activity)
    {
        var nodes = NodePositions(payload, "nodes").ToDictionary(item => item.Id, StringComparer.Ordinal);
        var groups = GroupPositions(payload, "groups").ToDictionary(item => item.Id, StringComparer.Ordinal);
        var operations = new List<GhostagramOperation>();
        operations.AddRange(_document.Groups.Where(group => groups.ContainsKey(group.Id)).Select(group => DiagramOperations.Upsert(group with { X = groups[group.Id].X, Y = groups[group.Id].Y })));
        AddNodePositionOperations(operations, nodes);
        if (operations.Count > 0) await SubmitOperationsAsync(operations, activity);
    }

    private async Task CommitGroupMoveAsync(JsonElement payload)
    {
        var id = String(payload, "groupId");
        var current = _document.Groups.SingleOrDefault(group => group.Id == id);
        if (current is null) return;
        var hasParent = TryNullableString(payload, "parentGroupId", out var parentId);
        var groupUpdates = GroupPositions(payload, "groups").Where(item => item.Id != id).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var nodeUpdates = NodePositions(payload, "nodes").ToDictionary(item => item.Id, StringComparer.Ordinal);
        var operations = new List<GhostagramOperation>
        {
            DiagramOperations.Upsert(current with { X = Number(payload, "x"), Y = Number(payload, "y") })
        };
        if (hasParent && !string.Equals(current.ParentGroupId, parentId, StringComparison.Ordinal))
            operations.Add(DiagramOperations.Create("group.assignGroup", new { groupId = id, parentGroupId = parentId }));
        operations.AddRange(_document.Groups.Where(group => groupUpdates.ContainsKey(group.Id)).Select(group => DiagramOperations.Upsert(group with { X = groupUpdates[group.Id].X, Y = groupUpdates[group.Id].Y })));
        AddNodePositionOperations(operations, nodeUpdates);
        await SubmitOperationsAsync(operations, "Moved group");
    }

    private void AddNodePositionOperations(List<GhostagramOperation> operations, IReadOnlyDictionary<string, NodePosition> positions)
    {
        foreach (var node in _document.Nodes.Where(node => positions.ContainsKey(node.Id)))
        {
            var position = positions[node.Id];
            operations.Add(DiagramOperations.Upsert(node with { X = position.X, Y = position.Y }));
            AddNodeGroupAssignment(operations, node, position.GroupId, position.HasGroupId);
        }
    }

    private static void AddNodeGroupAssignment(List<GhostagramOperation> operations, DiagramNode node, string? groupId, bool hasGroupId)
    {
        if (hasGroupId && !string.Equals(node.GroupId, groupId, StringComparison.Ordinal))
            operations.Add(DiagramOperations.Create("group.assignNode", new { nodeId = node.Id, groupId }));
    }

    private async Task CommitEdgeAsync(JsonElement payload, Func<DiagramEdge, DiagramEdge> update, string activity)
    {
        var current = _document.Edges.SingleOrDefault(edge => edge.Id == String(payload, "edgeId"));
        if (current is not null) await SubmitOperationsAsync([DiagramOperations.Upsert(update(current))], activity);
    }

    private async Task CreateEdgeAsync(JsonElement payload)
    {
        var source = String(payload, "sourcePortId");
        var target = String(payload, "targetPortId");
        if (_document.Edges.Any(edge => edge.SourcePortId == source && edge.TargetPortId == target)) return;
        var edge = StyledEdge(NextId("edge"), source, target, "flow");
        await SubmitOperationsAsync([DiagramOperations.Upsert(edge)], "Connected ports");
    }

    private Task ReconnectEdgeAsync(JsonElement payload) => CommitEdgeAsync(payload,
        edge => edge with { SourcePortId = String(payload, "sourcePortId"), TargetPortId = String(payload, "targetPortId") }, "Reconnected edge");

    private async Task ApplyEdgeSettingsAsync()
    {
        var selected = _document.Selection.ToHashSet(StringComparer.Ordinal);
        var edges = _document.Edges.Where(edge => selected.Contains(edge.Id)).ToArray();
        if (edges.Length == 0) edges = _document.Edges.ToArray();
        if (edges.Length == 0) return;
        var operations = edges.Select(edge => DiagramOperations.Upsert(edge with
        {
            Connector = _edgeConnector,
            ConnectorOptions = _edgeConnector == "flowchart" ? new DiagramFlowchartOptions(32, 0) : null,
            Waypoints = edge.Connector == _edgeConnector ? edge.Waypoints : [],
            Animation = _edgeAnimated
        })).ToArray();
        await SubmitOperationsAsync(operations, $"Updated {edges.Length} edge{(edges.Length == 1 ? string.Empty : "s")}");
    }

    private async Task OnEdgeConnectorChangedAsync(string value)
    {
        _edgeConnector = value;
        var edges = SelectedEdges();
        if (edges.Length == 0) return;
        var operations = edges.Select(edge => DiagramOperations.Upsert(edge with
        {
            Connector = value,
            ConnectorOptions = value == "flowchart" ? new DiagramFlowchartOptions(32, 0) : null,
            Waypoints = edge.Connector == value ? edge.Waypoints : []
        })).ToArray();
        await SubmitOperationsAsync(operations, $"Changed {SelectedEdgeDescription(edges.Length)} to {(value == "bezier" ? "curved" : "square")}");
    }

    private async Task OnEdgeAnimatedChangedAsync(bool value)
    {
        _edgeAnimated = value;
        var edges = SelectedEdges();
        if (edges.Length == 0) return;
        var operations = edges.Select(edge => DiagramOperations.Upsert(edge with { Animation = value })).ToArray();
        await SubmitOperationsAsync(operations, $"{(value ? "Animated" : "Stopped animating")} {SelectedEdgeDescription(edges.Length)}");
    }

    private DiagramEdge[] SelectedEdges()
    {
        var selected = _document.Selection.ToHashSet(StringComparer.Ordinal);
        return _document.Edges.Where(edge => selected.Contains(edge.Id)).ToArray();
    }

    private static string SelectedEdgeDescription(int count) => count == 1 ? "selected edge" : $"{count} selected edges";

    private async Task ApplyLayoutAsync()
    {
        if (_busy) return;
        await _commandGate.WaitAsync();
        _busy = true;
        var previous = _document;
        try
        {
            var result = await Layouts.ExecuteAsync(new DiagramLayoutRequest(
                DocumentId, ActorId, CommandId("layout"), _revision, GhostLayeredLayoutStrategy.AlgorithmName, 42, false,
                new DiagramLayoutOptions(Direction: _layoutDirection)), CancellationToken.None);
            if (!result.Accepted || result.Commit?.Snapshot is null)
            {
                await AcceptRejectedResultAsync(result.Commit, result.Message);
                return;
            }

            _document = Deserialize(result.Commit.Snapshot);
            _revision = result.Commit.Revision;
            _undo.Push(previous);
            _redo.Clear();
            if (_diagram is not null) await _diagram.ReplaceAsync(_document, _revision);
            _activity = $"Sorted {_layoutDirection.Replace("right", "left to right").Replace("down", "top to bottom").Replace("left", "right to left").Replace("up", "bottom to top")}";
        }
        catch (Exception exception)
        {
            _activity = $"Unable to sort layout: {exception.Message}";
            await ResynchronizeAsync(fetchSnapshot: true);
        }
        finally
        {
            _busy = false;
            _commandGate.Release();
        }
    }

    private Task DeleteSelectedAsync() => DeleteSelectionAsync(_document.Selection, [], []);

    private async Task RemoveSelectedGroupFramesAsync()
    {
        var selected = _document.Selection.ToHashSet(StringComparer.Ordinal);
        var groupIds = _document.Groups.Where(group => selected.Contains(group.Id)).Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        if (groupIds.Count == 0) return;

        var remainingSelection = _document.Selection.Where(id => !groupIds.Contains(id)).ToArray();
        var operations = groupIds.OrderByDescending(GroupDepth).ThenBy(id => id, StringComparer.Ordinal).Select(DiagramOperations.RemoveGroup).ToList();
        operations.Add(DiagramOperations.Select(remainingSelection));
        await SubmitOperationsAsync(operations, $"Removed {groupIds.Count} group frame{(groupIds.Count == 1 ? string.Empty : "s")}");
    }

    private Task DeleteSelectionAsync(JsonElement payload) => DeleteSelectionAsync(
        StringArray(payload, "nodeIds"), StringArray(payload, "edgeIds"), StringArray(payload, "groupIds"));

    private async Task DeleteSelectionAsync(IEnumerable<string> nodeIds, IEnumerable<string> edgeIds, IEnumerable<string> groupIds)
    {
        var selected = _document.Selection.ToHashSet(StringComparer.Ordinal);
        var nodes = nodeIds.Concat(_document.Nodes.Where(node => selected.Contains(node.Id)).Select(node => node.Id)).ToHashSet(StringComparer.Ordinal);
        var edges = edgeIds.Concat(_document.Edges.Where(edge => selected.Contains(edge.Id)).Select(edge => edge.Id)).ToHashSet(StringComparer.Ordinal);
        var groups = groupIds.Concat(_document.Groups.Where(group => selected.Contains(group.Id)).Select(group => group.Id)).ToHashSet(StringComparer.Ordinal);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var child in _document.Groups.Where(group => group.ParentGroupId is not null && groups.Contains(group.ParentGroupId)))
                changed |= groups.Add(child.Id);
        }

        nodes.UnionWith(_document.Nodes.Where(node => node.GroupId is not null && groups.Contains(node.GroupId)).Select(node => node.Id));
        var ports = _document.Ports.Where(port => nodes.Contains(port.NodeId)).Select(port => port.Id).ToHashSet(StringComparer.Ordinal);
        edges.UnionWith(_document.Edges.Where(edge => ports.Contains(edge.SourcePortId) || ports.Contains(edge.TargetPortId)).Select(edge => edge.Id));
        if (nodes.Count == 0 && edges.Count == 0 && groups.Count == 0) return;

        var operations = new List<GhostagramOperation>();
        operations.AddRange(edges.Order(StringComparer.Ordinal).Select(DiagramOperations.RemoveEdge));
        operations.AddRange(ports.Order(StringComparer.Ordinal).Select(DiagramOperations.RemovePort));
        operations.AddRange(nodes.Order(StringComparer.Ordinal).Select(DiagramOperations.RemoveNode));
        operations.AddRange(groups.OrderByDescending(GroupDepth).ThenBy(id => id, StringComparer.Ordinal).Select(DiagramOperations.RemoveGroup));
        operations.Add(DiagramOperations.Select([]));
        await SubmitOperationsAsync(operations, "Deleted selection");
    }

    private Task DeleteEdgesAsync(IEnumerable<string> ids)
    {
        var edges = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        return edges.Length == 0 ? Task.CompletedTask : SubmitOperationsAsync(edges.Select(DiagramOperations.RemoveEdge).ToArray(), "Deleted edge");
    }

    private async Task<bool> SubmitOperationsAsync(IReadOnlyList<GhostagramOperation> operations, string activity, bool recordHistory = true)
    {
        if (operations.Count == 0) return true;
        await _commandGate.WaitAsync();
        _busy = true;
        var previous = _document;
        var baseRevision = _revision;
        try
        {
            var command = new DiagramCommand(DocumentId, ActorId, CommandId("studio"), baseRevision, operations.ToImmutableArray());
            var result = await Commands.SubmitAsync(command, CancellationToken.None);
            if (!result.Accepted && result.Code == "REVISION_CONFLICT" && result.Snapshot is not null && IsAdditiveBatch(operations, previous))
            {
                previous = Deserialize(result.Snapshot);
                _document = previous;
                _revision = result.Snapshot.Revision;
                baseRevision = _revision;
                if (_diagram is not null) await _diagram.ReplaceAsync(_document, _revision);
                command = new DiagramCommand(DocumentId, ActorId, CommandId("studio-retry"), baseRevision, operations.ToImmutableArray());
                result = await Commands.SubmitAsync(command, CancellationToken.None);
            }
            if (!result.Accepted || result.Snapshot is null)
            {
                await AcceptRejectedResultAsync(result, result.Message);
                return false;
            }

            _document = Deserialize(result.Snapshot);
            _revision = result.Revision;
            if (recordHistory)
            {
                _undo.Push(previous);
                _redo.Clear();
            }

            if (_diagram is not null)
            {
                try
                {
                    if (result.Revision == baseRevision + 1)
                        await _diagram.ApplyAsync(baseRevision, result.Revision, operations);
                    else
                        await _diagram.ReplaceAsync(_document, _revision);
                }
                catch
                {
                    await _diagram.ReplaceAsync(_document, _revision);
                }
            }

            _activity = activity;
            return true;
        }
        catch (Exception exception)
        {
            _activity = $"Recovered from a save error: {exception.Message}";
            await ResynchronizeAsync(fetchSnapshot: true);
            return false;
        }
        finally
        {
            _busy = false;
            _commandGate.Release();
        }
    }

    private static bool IsAdditiveBatch(IReadOnlyList<GhostagramOperation> operations, DiagramDocument document)
    {
        if (operations.Count == 0) return false;
        var nodeIds = document.Nodes.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var portIds = document.Ports.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var edgeIds = document.Edges.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var groupIds = document.Groups.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        return operations.All(operation => operation.Type switch
        {
            "node.upsert" => operation.Id is { } id && !nodeIds.Contains(id),
            "port.upsert" => operation.Id is { } id && !portIds.Contains(id),
            "edge.upsert" => operation.Id is { } id && !edgeIds.Contains(id),
            "group.upsert" => operation.Id is { } id && !groupIds.Contains(id),
            _ => false
        });
    }

    private async Task AcceptRejectedResultAsync(DiagramCommandResult? result, string message)
    {
        if (result?.Snapshot is not null)
        {
            _document = Deserialize(result.Snapshot);
            _revision = result.Snapshot.Revision;
        }
        if (_diagram is not null) await _diagram.ReplaceAsync(_document, _revision);
        _activity = $"Change was not saved: {message}";
    }

    private async Task ResynchronizeAsync(bool fetchSnapshot = false)
    {
        try
        {
            if (fetchSnapshot && await Commands.GetSnapshotAsync(DocumentId, CancellationToken.None) is { } snapshot)
            {
                _document = Deserialize(snapshot);
                _revision = snapshot.Revision;
            }
            if (_diagram is not null) await _diagram.ReplaceAsync(_document, _revision);
        }
        catch (Exception exception)
        {
            _activity = $"Diagram recovery is waiting for the next action: {exception.Message}";
        }
    }

    private async Task FitAsync()
    {
        if (_diagram is null || _busy) return;
        await _commandGate.WaitAsync();
        _busy = true;
        try
        {
            var baseRevision = _revision;
            await _diagram.ApplyAsync(baseRevision, baseRevision + 1, [DiagramOperations.Fit(48)]);
            var inspection = await _diagram.InspectAsync();
            var viewport = JsonSerializer.Deserialize<DiagramDocument>(inspection.Model.GetRawText(), JsonOptions)?.Viewport ?? _document.Viewport;
            var operations = new[] { DiagramOperations.SetViewport(viewport) };
            var result = await Commands.SubmitAsync(new DiagramCommand(DocumentId, ActorId, CommandId("fit"), baseRevision, operations.ToImmutableArray()), CancellationToken.None);
            if (!result.Accepted || result.Snapshot is null)
            {
                await AcceptRejectedResultAsync(result, result.Message);
                return;
            }
            _document = Deserialize(result.Snapshot);
            _revision = result.Revision;
            await _diagram.ReplaceAsync(_document, _revision);
            _activity = "Framed diagram";
        }
        catch (Exception exception)
        {
            _activity = $"Unable to frame diagram: {exception.Message}";
            await ResynchronizeAsync(fetchSnapshot: true);
        }
        finally
        {
            _busy = false;
            _commandGate.Release();
        }
    }

    private async Task ExportAsync()
    {
        if (_diagram is null) return;
        try
        {
            _exportedSvg = await _diagram.ExportSvgAsync();
            _isExportOpen = true;
            _activity = "Generated model-derived SVG";
        }
        catch (Exception exception)
        {
            _activity = $"Unable to export SVG: {exception.Message}";
        }
    }

    private async Task ResetAsync()
    {
        var operations = OperationsToTransform(_document, CreateStarterDocument());
        if (await SubmitOperationsAsync(operations, "Restored the starter design")) _exportedSvg = null;
    }

    private async Task UndoAsync()
    {
        if (!_undo.TryPop(out var previous)) return;
        var current = _document;
        _redo.Push(current);
        if (!await SubmitOperationsAsync(OperationsToTransform(current, previous), "Undid change", false))
        {
            _redo.Pop();
            _undo.Push(previous);
        }
    }

    private async Task RedoAsync()
    {
        if (!_redo.TryPop(out var next)) return;
        var current = _document;
        _undo.Push(current);
        if (!await SubmitOperationsAsync(OperationsToTransform(current, next), "Redid change", false))
        {
            _undo.Pop();
            _redo.Push(next);
        }
    }

    private static IReadOnlyList<GhostagramOperation> OperationsToTransform(DiagramDocument current, DiagramDocument target)
    {
        var targetEdges = target.Edges.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var targetPorts = target.Ports.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var targetNodes = target.Nodes.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var targetGroups = target.Groups.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var targetTypes = target.EdgeTypes.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var operations = new List<GhostagramOperation>();
        operations.AddRange(current.Edges.Where(item => !targetEdges.Contains(item.Id)).Select(item => DiagramOperations.RemoveEdge(item.Id)));
        operations.AddRange(current.Ports.Where(item => !targetPorts.Contains(item.Id)).Select(item => DiagramOperations.RemovePort(item.Id)));
        operations.AddRange(current.Nodes.Where(item => !targetNodes.Contains(item.Id)).Select(item => DiagramOperations.RemoveNode(item.Id)));
        operations.AddRange(current.Groups.Where(item => !targetGroups.Contains(item.Id)).Select(item => DiagramOperations.RemoveGroup(item.Id)));
        operations.AddRange(current.EdgeTypes.Where(item => !targetTypes.Contains(item.Id)).Select(item => DiagramOperations.Create("edgeType.remove", new { item.Id }, item.Id)));
        operations.AddRange(target.Groups.Select(DiagramOperations.Upsert));
        operations.AddRange(target.Nodes.Select(DiagramOperations.Upsert));
        operations.AddRange(target.Ports.Select(DiagramOperations.Upsert));
        operations.AddRange(target.EdgeTypes.Select(DiagramOperations.Upsert));
        operations.AddRange(target.Edges.Select(DiagramOperations.Upsert));
        operations.Add(DiagramOperations.Select(target.Selection));
        operations.Add(DiagramOperations.SetViewport(target.Viewport));
        return operations;
    }

    private DiagramEdge StyledEdge(string id, string source, string target, string label) => new(
        id, source, target, label, _edgeConnector,
        Overlays: [new DiagramOverlay("arrow")],
        ConnectorOptions: _edgeConnector == "flowchart" ? new DiagramFlowchartOptions(32, 0) : null,
        Style: new DiagramEdgeStyle("#7455dd", 2.5, null, .9),
        Animation: _edgeAnimated);

    private void SyncEdgeControlsFromSelection(IReadOnlyList<string> selection)
    {
        var edge = _document.Edges.SingleOrDefault(item => selection.Contains(item.Id, StringComparer.Ordinal));
        if (edge is null) return;
        SyncEdgeControls(edge);
    }

    private void SyncEdgeControlsFromDocument()
    {
        if (_document.Edges.FirstOrDefault() is { } edge) SyncEdgeControls(edge);
    }

    private void SyncEdgeControls(DiagramEdge edge)
    {
        _edgeConnector = edge.Connector is "straight" or "bezier" ? edge.Connector : "flowchart";
        _edgeAnimated = AnimationEnabled(edge.Animation);
    }

    private static bool AnimationEnabled(object? animation) => animation switch
    {
        null => false,
        bool value => value,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined } => false,
        JsonElement { ValueKind: JsonValueKind.Object } value when value.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False => false,
        JsonElement { ValueKind: JsonValueKind.Object } => true,
        _ => true
    };

    private int GroupDepth(string id)
    {
        var depth = 0;
        var current = _document.Groups.SingleOrDefault(group => group.Id == id)?.ParentGroupId;
        while (current is not null && depth <= _document.Groups.Count)
        {
            depth++;
            current = _document.Groups.SingleOrDefault(group => group.Id == current)?.ParentGroupId;
        }
        return depth;
    }

    private static string TemplateIcon(string templateId) => templateId switch
    {
        "start" => Icons.Material.Outlined.PlayCircle,
        "task" => Icons.Material.Outlined.TaskAlt,
        "decision" => Icons.Material.Outlined.CallSplit,
        "note" => Icons.Material.Outlined.StickyNote2,
        "group" => Icons.Material.Outlined.FolderCopy,
        _ => Icons.Material.Outlined.Widgets
    };

    private static string? NormalizeIcon(string? icon) => icon switch
    {
        null or "" => null,
        "task" => "mdi:checkbox-marked-circle-outline",
        "decision" => "mdi:source-branch",
        "note" => "mdi:note-text-outline",
        "group" => "mdi:layers-outline",
        "custom" => "mdi:shape-outline",
        _ when icon.Contains(':') => icon,
        _ => "mdi:shape-outline"
    };

    private static PropertyTemplate CreatePropertyTemplate(DraftProperty draft)
    {
        var name = string.IsNullOrWhiteSpace(draft.Name) ? draft.Id : draft.Name.Trim();
        var type = draft.Type is DiagramPropertyTypes.Boolean or DiagramPropertyTypes.Integer or DiagramPropertyTypes.Decimal or DiagramPropertyTypes.Date or DiagramPropertyTypes.DateTime or DiagramPropertyTypes.Enum or DiagramPropertyTypes.Json ? draft.Type : DiagramPropertyTypes.String;
        var options = type == DiagramPropertyTypes.Enum
            ? draft.Options.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray()
            : [];
        return new PropertyTemplate(draft.Id, name, type, ParsePropertyValue(draft.DefaultValue, type), draft.Mode, name, draft.Connectable, options, draft.Direction);
    }

    private static JsonElement? ParsePropertyValue(string? raw, string type)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return type switch
            {
                DiagramPropertyTypes.Boolean when bool.TryParse(raw, out var boolean) => JsonSerializer.SerializeToElement(boolean),
                DiagramPropertyTypes.Integer when long.TryParse(raw, out var integer) => JsonSerializer.SerializeToElement(integer),
                DiagramPropertyTypes.Decimal when decimal.TryParse(raw, out var number) => JsonSerializer.SerializeToElement(number),
                DiagramPropertyTypes.Json => JsonDocument.Parse(raw).RootElement.Clone(),
                _ => JsonSerializer.SerializeToElement(raw)
            };
        }
        catch (JsonException) { return JsonSerializer.SerializeToElement(raw); }
    }

    private static IEnumerable<DiagramPort> PropertyPorts(string nodeId, DiagramNodeProperty property, string direction, int index, string outline)
    {
        var endpoint = new DiagramEndpoint("dot", 10, outline, "#ffffff", 2);
        if (!property.Connectable || property.Mode == DiagramPropertyModes.Hidden) yield break;
        if (direction is "target" or "both")
        {
            yield return new DiagramPort($"{nodeId}-{property.Id}-in", nodeId, "target", Anchor: "left", Endpoint: endpoint, PropertyId: property.Id, Label: property.Label, Order: index);
        }
        if (direction is "source" or "both")
        {
            yield return new DiagramPort($"{nodeId}-{property.Id}-out", nodeId, "source", Anchor: "right", Endpoint: endpoint, PropertyId: property.Id, Label: property.Label, Order: index);
        }
    }

    private static DiagramDocument Deserialize(DiagramSnapshot snapshot) =>
        JsonSerializer.Deserialize<DiagramDocument>(snapshot.Model.GetRawText(), JsonOptions)
        ?? throw new InvalidOperationException("The saved diagram could not be read.");

    private static bool IsEmpty(DiagramDocument document) => document.Nodes.Count == 0 && document.Groups.Count == 0;
    private static DiagramDocument EmptyDocument() => new(DocumentId, [], [], [], [], new DiagramViewport(), [], []);

    private static DiagramDocument CreateStarterDocument() => new(
        DocumentId,
        [
            new DiagramNode("node-1", 128, 160, 156, 72, "Discover", GroupId: "group-1", Icon: "mdi:magnify", Style: new DiagramNodeStyle("#ffffff", "#4177de", "#172033")),
            new DiagramNode("node-2", 412, 160, 172, 72, "Shape the flow", GroupId: "group-1", Icon: "mdi:vector-polyline", Style: new DiagramNodeStyle("#ffffff", "#7455dd", "#172033"))
        ],
        [
            new DiagramPort("node-1-output", "node-1", "source", Anchor: "right", Endpoint: new DiagramEndpoint("dot", 11, "#4177de", "#ffffff", 2)),
            new DiagramPort("node-2-input", "node-2", "target", Anchor: "left", Endpoint: new DiagramEndpoint("dot", 11, "#7455dd", "#ffffff", 2)),
            new DiagramPort("node-2-output", "node-2", "source", Anchor: "right", Endpoint: new DiagramEndpoint("dot", 11, "#7455dd", "#ffffff", 2))
        ],
        [new DiagramEdge("edge-1", "node-1-output", "node-2-input", "explore", "flowchart", Overlays: [new DiagramOverlay("arrow")], ConnectorOptions: new DiagramFlowchartOptions(32, 0), Style: new DiagramEdgeStyle("#7455dd", 2.5))],
        [new DiagramGroup("group-1", 88, 104, 548, 196, "Design loop", Icon: "mdi:layers-outline")],
        new DiagramViewport(0, 0, 1),
        Selection: []);

    private static List<NodeTemplate> CreateBuiltInTemplates() =>
    [
        new("start", "Workflow", "Start", "Entry point for a workflow", 144, 64, "#16875b", "#ecfdf5", "#115e45", "center", [new PortTemplate("output", "right", "source")], false, "mdi:play-circle-outline"),
        new("task", "Workflow", "Task", "Work step with input and output", 168, 72, "#4177de", "#ffffff", "#172033", "center", [new PortTemplate("input", "left", "target"), new PortTemplate("output", "right", "source")], false, "mdi:checkbox-marked-circle-outline"),
        new("decision", "Workflow", "Decision", "Branch a workflow into paths", 172, 88, "#d97706", "#fffaf0", "#7c2d12", "center", [new PortTemplate("input", "left", "target"), new PortTemplate("yes", "right", "source"), new PortTemplate("no", "bottom", "source")], false, "mdi:source-branch"),
        new("note", "Content", "Note", "Context without connection points", 184, 92, "#0f9d79", "#f0fdfa", "#134e4a", "left", [], false, "mdi:note-text-outline"),
        new("group", "Containers", "Group", "Resizable visual boundary", 320, 220, "#7455dd", "#ffffff", "#172033", "left", [], true, "mdi:layers-outline")
    ];

    private static List<PaletteCategory> CreatePaletteCategories() =>
    [
        new("Workflow", true),
        new("Content", true),
        new("Containers", true),
        new("Custom", true)
    ];

    private void AddRegisteredNodeSets()
    {
        foreach (var set in NodeTypes.NodeSets)
        {
            foreach (var descriptor in set.NodeTypes)
            {
                if (_paletteCategories.All(category => !string.Equals(category.Name, descriptor.PaletteGroup, StringComparison.Ordinal)))
                    _paletteCategories.Add(new(descriptor.PaletteGroup, true));
                if (_templates.Any(template => string.Equals(template.RegisteredTypeId, descriptor.TypeId, StringComparison.Ordinal) && template.RegisteredTypeVersion == descriptor.Version)) continue;
                var properties = descriptor.Properties.Select(property => new PropertyTemplate(
                    property.Id,
                    property.Name,
                    property.Type,
                    property.DefaultValue?.Clone(),
                    property.Mode,
                    property.Label,
                    property.Connectable,
                    property.Options ?? [],
                    PropertyDirection(descriptor, property.Id))).ToArray();
                var ports = descriptor.Ports
                    .Where(port => port.PropertyId is null)
                    .Select(port => new PortTemplate(port.Id, PortSide(port.Direction), port.Direction))
                    .ToArray();
                _templates.Add(new(
                    $"registered:{descriptor.TypeId}@{descriptor.Version}",
                    descriptor.PaletteGroup,
                    descriptor.DisplayName,
                    descriptor.Metadata.TryGetValue("description", out var description) && description.ValueKind == JsonValueKind.String
                        ? description.GetString() ?? set.Description ?? "Registered node type"
                        : set.Description ?? "Registered node type",
                    descriptor.Width,
                    descriptor.Height,
                    descriptor.Style?.BorderColor ?? "#5b5bd6",
                    descriptor.Style?.Background ?? "#ffffff",
                    descriptor.Style?.Color ?? "#172033",
                    descriptor.Style?.TextAlign ?? "left",
                    ports,
                    false,
                    NormalizeIcon(descriptor.Icon) ?? "mdi:shape-outline",
                    Properties: properties,
                    RegisteredTypeId: descriptor.TypeId,
                    RegisteredTypeVersion: descriptor.Version));
            }
        }
    }

    private static string PropertyDirection(NodeTypeDescriptor descriptor, string propertyId)
    {
        var directions = descriptor.Ports.Where(port => string.Equals(port.PropertyId, propertyId, StringComparison.Ordinal))
            .Select(port => port.Direction).ToHashSet(StringComparer.Ordinal);
        return directions.Contains("both") || directions.Contains("source") && directions.Contains("target")
            ? "both"
            : directions.Contains("target") ? "target" : "source";
    }

    private static string PortSide(string direction) => direction == "target" ? "left" : "right";

    private static string CommandId(string kind) => $"{kind}-{Guid.NewGuid():N}";
    private static string NextId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
    private static double Snap(double value) => Math.Round(value / GridSize) * GridSize;
    private static bool ValidColor(string? value) => value is not null && value.Length is 4 or 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    private static string NormalizeColor(string? value, string fallback) => ValidColor(value) ? value!.ToLowerInvariant() : fallback;
    private static string NormalizeTextAlign(string? value) => value is "left" or "right" or "center" ? value : "center";
    private static string String(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static string? NullableLabel(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        var label = value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }
    private static bool TryNullableString(JsonElement element, string property, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(property, out var candidate)) return false;
        if (candidate.ValueKind == JsonValueKind.Null) return true;
        if (candidate.ValueKind != JsonValueKind.String) return false;
        value = candidate.GetString();
        return true;
    }
    private static double Number(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.TryGetDouble(out var number) ? number : 0;
    private static bool Boolean(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
    private static IReadOnlyList<string> StringArray(JsonElement element, string property) => element.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToArray() : [];
    private static IReadOnlyList<DiagramPoint> Points(JsonElement element, string property) => element.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().Select(value => new DiagramPoint(Number(value, "x"), Number(value, "y"))).ToArray() : [];
    private static DiagramViewport Viewport(JsonElement element) => element.TryGetProperty("viewport", out var viewport) ? new DiagramViewport(Number(viewport, "x"), Number(viewport, "y"), Number(viewport, "zoom")) : new DiagramViewport();
    private static NodePosition[] NodePositions(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var points) && points.ValueKind == JsonValueKind.Array
            ? points.EnumerateArray().Where(point => point.ValueKind == JsonValueKind.Object).Select(point =>
            {
                var hasGroupId = TryNullableString(point, "groupId", out var groupId);
                return new NodePosition(String(point, "id"), Number(point, "x"), Number(point, "y"), groupId, hasGroupId);
            }).Where(position => !string.IsNullOrWhiteSpace(position.Id)).GroupBy(position => position.Id, StringComparer.Ordinal).Select(group => group.Last()).ToArray()
            : [];
    private static GroupPosition[] GroupPositions(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var points) && points.ValueKind == JsonValueKind.Array
            ? points.EnumerateArray().Where(point => point.ValueKind == JsonValueKind.Object).Select(point => new GroupPosition(String(point, "id"), Number(point, "x"), Number(point, "y"))).Where(item => !string.IsNullOrWhiteSpace(item.Id)).ToArray()
            : [];
    public ValueTask DisposeAsync()
    {
        _eventGate.Dispose();
        _commandGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class NodeDraft
    {
        public string Label { get; set; } = "Human review";
        public double Width { get; set; } = 184;
        public double Height { get; set; } = 80;
        public string Outline { get; set; } = "#d24686";
        public string Background { get; set; } = "#ffffff";
        public string TextColor { get; set; } = "#172033";
        public string TextAlign { get; set; } = "center";
        public string Category { get; set; } = "Custom";
        public string Direction { get; set; } = "both";
        public string Icon { get; set; } = "mdi:shape-outline";
    }

    private sealed class NodeStyleDraft
    {
        public string Outline { get; set; } = "#4177de";
        public string Background { get; set; } = "#ffffff";
        public string TextColor { get; set; } = "#172033";
        public string TextAlign { get; set; } = "center";
    }

    private sealed class DraftPort(string id, string side, string direction)
    {
        public string Id { get; } = id;
        public string Side { get; } = side;
        public string Direction { get; set; } = direction;
    }
    private sealed class DraftProperty(string id)
    {
        public string Id { get; } = id;
        public string Name { get; set; } = "Value";
        public string Type { get; set; } = DiagramPropertyTypes.String;
        public string Mode { get; set; } = DiagramPropertyModes.Display;
        public string DefaultValue { get; set; } = string.Empty;
        public string Options { get; set; } = string.Empty;
        public bool Connectable { get; set; }
        public string Direction { get; set; } = "both";
    }

    private sealed record NodeTemplate(
        string Id,
        string Category,
        string Label,
        string Description,
        double Width,
        double Height,
        string Outline,
        string Background,
        string TextColor,
        string TextAlign,
        IReadOnlyList<PortTemplate> Ports,
        bool IsGroup,
        string Icon,
        bool IsCustom = false,
        IReadOnlyList<PropertyTemplate>? Properties = null,
        string? RegisteredTypeId = null,
        int? RegisteredTypeVersion = null)
    {
        public IReadOnlyList<PropertyTemplate> Properties { get; init; } = Properties ?? [];
    }
    private sealed record PaletteCategory(string Name, bool IsSystem);
    private sealed record PortTemplate(string Id, string Side, string Direction);
    private sealed record PropertyTemplate(string Id, string Name, string Type, JsonElement? Value, string Mode, string? Label, bool Connectable, IReadOnlyList<string> Options, string Direction)
    {
        public DiagramNodeProperty Property => new(Id, Name, Type: Type, Value: Value, Mode: Mode, Label: Label, Connectable: Connectable, Options: Options);
    }
    private sealed record NodePosition(string Id, double X, double Y, string? GroupId, bool HasGroupId);
    private sealed record GroupPosition(string Id, double X, double Y);
}
