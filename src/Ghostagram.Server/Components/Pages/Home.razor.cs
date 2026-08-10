using System.Collections.Immutable;
using System.Text.Json;
using Ghostagram.Blazor;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Ghostagram.Execution;
using Ghostagram.Server.Layout;
using Ghostagram.Server.Persistence;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Ghostagram.Server.Components.Pages;

public partial class Home : IAsyncDisposable
{
    private const string DefaultDocumentId = "laboratory-design";
    private const string DefaultPaletteCatalogId = "laboratory-palette";
    private const int GridSize = 16;
    private const int MaxDesignedProperties = 17;
    // Keep these layout constants aligned with Ghostagram's nodeLayoutProjection.
    private const double NodeHeaderHeight = 30;
    private const double NodePropertyHeight = 20;
    private const double NodeCompactPropertyHeight = 18;
    private const double NodePropertyGap = 1;
    private const double NodeSectionHeaderHeight = 22;
    private const double NodeBodyBottomPadding = 8;
    private static readonly EdgeMarkerChoice[] EdgeMarkerChoices =
    [
        new("none", "None"),
        new("arrow", "Filled arrow"),
        new("plain-arrow", "Open arrow"),
        new("triangle-open", "Generalization"),
        new("diamond-open", "Aggregation"),
        new("diamond", "Composition"),
        new("erd-one", "Exactly one"),
        new("erd-zero-one", "Zero or one"),
        new("erd-one-many", "One or many"),
        new("erd-zero-many", "Zero or many")
    ];
    private static readonly HashSet<string> EdgeMarkerTypes = EdgeMarkerChoices
        .Where(choice => choice.Id != "none")
        .Select(choice => choice.Id)
        .ToHashSet(StringComparer.Ordinal);

    [Inject] private DiagramCommandService Commands { get; set; } = default!;
    [Inject] private IDocumentCatalog Documents { get; set; } = default!;
    [Inject] private IPaletteCatalogRepository PaletteCatalogs { get; set; } = default!;
    [Inject] private ILaboratoryWorkspaceStore Workspace { get; set; } = default!;
    [Inject] private DiagramLayoutService Layouts { get; set; } = default!;
    [Inject] private INodeTypeRegistry NodeTypes { get; set; } = default!;
    [Inject] private INodeFactory NodeFactory { get; set; } = default!;
    [Inject] private IDocumentChangeNotifier DocumentChanges { get; set; } = default!;
    [Inject] private LaboratoryCircuitState CircuitState { get; set; } = default!;
    [Parameter, SupplyParameterFromQuery(Name = "documentId")] public string? RequestedDocumentId { get; set; }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GhostDiagramOptions _options = new(Height: "100%", GridSize: GridSize, MinZoom: .25, MaxZoom: 2.5, RespectReducedMotion: false, ModulePath: "/ghostagram/ghostagram.js?v=20260809.4");
    private readonly List<PaletteCategory> _paletteCategories = CreatePaletteCategories();
    private readonly List<NodeTemplate> _templates = CreateBuiltInTemplates();
    private readonly List<DraftPort> _draftPorts = [];
    private readonly List<DraftProperty> _draftProperties = [];
    private readonly List<DraftSection> _draftSections = [];
    private readonly HashSet<string> _expandedCategories = new(StringComparer.Ordinal) { "Workflow" };
    private readonly Stack<DiagramDocument> _undo = new();
    private readonly Stack<DiagramDocument> _redo = new();
    private readonly SemaphoreSlim _eventGate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _paletteGate = new(1, 1);
    private readonly string _actorId = $"ghostagram-laboratory-{Guid.NewGuid():N}";

    private DiagramDocument _document = EmptyDocument(DefaultDocumentId);
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
    private bool _isPropertiesEditorOpen;
    private bool _isExportOpen;
    private bool _isDiagramLibraryOpen;
    private bool _isSaveAsOpen;
    private bool _isPaletteLibraryOpen;
    private bool _isPaletteSaveAsOpen;
    private bool _busy;
    private bool _documentDurable;
    private string _activity = "Loading saved design…";
    private string _layoutDirection = "right";
    private string _edgeConnector = "flowchart";
    private string _edgeStartMarker = "none";
    private string _edgeEndMarker = "arrow";
    private bool _edgeAnimated;
    private string? _exportedSvg;
    private string _documentId = DefaultDocumentId;
    private string _documentDisplayName = "Laboratory design";
    private string _saveAsName = string.Empty;
    private IReadOnlyList<DiagramDocumentSummary> _documents = [];
    private string _paletteCatalogId = DefaultPaletteCatalogId;
    private string _paletteCatalogName = "Laboratory palette";
    private string? _paletteCatalogDescription = "Ghostagram node palette";
    private IReadOnlyDictionary<string, JsonElement> _paletteCatalogAttributes = ImmutableDictionary<string, JsonElement>.Empty;
    private string _paletteSaveAsName = string.Empty;
    private long _paletteCatalogRevision;
    private DateTimeOffset _paletteCatalogCreatedUtc = DateTimeOffset.UtcNow;
    private bool _paletteCatalogDurable;
    private bool _paletteLoadFailed;
    private IReadOnlyList<PaletteCatalogSummary> _paletteCatalogSummaries = [];
    private string _paletteGroupName = string.Empty;
    private string? _styleNodeId;
    private string? _propertiesNodeId;
    private long _propertiesEditorBaseRevision;
    private string? _propertiesEditorError;
    private string? _designerError;
    private readonly List<PropertyEditorDraft> _propertyEditorDrafts = [];
    private readonly List<PortEditorDraft> _portEditorDrafts = [];
    private readonly List<SectionEditorDraft> _sectionEditorDrafts = [];
    private PresentationDraft _presentationDraft = new();
    private long _revision;
    private int _templateSequence;
    private int _portSequence;
    private int _propertySequence;
    private long _lastBrowserEventId;
    private IDisposable? _documentSubscription;
    private bool _disposed;

    protected override async Task OnInitializedAsync()
    {
        CircuitState.ConnectionChanged += OnCircuitConnectionChangedAsync;
        await LoadWorkspaceSelectionAsync();
        ApplyRequestedDocumentId();
        AddRegisteredNodeSets();
        await LoadOrCreatePaletteCatalogAsync();
        await LoadOrCreateDocumentAsync();
        await RefreshDocumentCatalogAsync();
        SyncEdgeControlsFromDocument();
        SubscribeToDocumentChanges();
    }

    private string DocumentId => _documentId;

    private void ApplyRequestedDocumentId()
    {
        if (string.IsNullOrWhiteSpace(RequestedDocumentId)) return;
        try
        {
            _documentId = DocumentIdRules.Require(RequestedDocumentId);
        }
        catch (ArgumentException exception)
        {
            _activity = $"Ignored an invalid collaboration link: {exception.Message}";
        }
    }

    private void SubscribeToDocumentChanges()
    {
        if (_disposed) return;
        var subscription = DocumentChanges.Subscribe(DocumentId, _revision, OnAuthoritativeDocumentChangedAsync);
        Interlocked.Exchange(ref _documentSubscription, subscription)?.Dispose();
    }

    private Task OnCircuitConnectionChangedAsync(bool connected, CancellationToken cancellationToken)
    {
        if (_disposed) return Task.CompletedTask;
        if (!connected)
        {
            Interlocked.Exchange(ref _documentSubscription, null)?.Dispose();
            return Task.CompletedTask;
        }

        return InvokeAsync(SubscribeToDocumentChanges);
    }

    private Task OnAuthoritativeDocumentChangedAsync(DiagramChange change, CancellationToken cancellationToken)
    {
        if (_disposed || !string.Equals(change.DocumentId, DocumentId, StringComparison.Ordinal)) return Task.CompletedTask;
        // This circuit already owns the synchronous command result for its own write. Returning here also
        // allows the notifier to advance this view's acknowledgement without deadlocking the command gate.
        if (string.Equals(change.ActorId, _actorId, StringComparison.Ordinal)) return Task.CompletedTask;
        return InvokeAsync(() => ApplyExternalDocumentChangeAsync(change, cancellationToken));
    }

    private async Task ApplyExternalDocumentChangeAsync(DiagramChange change, CancellationToken cancellationToken)
    {
        if (_disposed || change.Revision <= _revision) return;
        await _eventGate.WaitAsync(cancellationToken);
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await Commands.GetSnapshotAsync(DocumentId, cancellationToken);
            if (snapshot is null || snapshot.Revision <= _revision) return;
            _document = Deserialize(snapshot);
            _revision = snapshot.Revision;
            _documentDurable = true;
            _undo.Clear();
            _redo.Clear();
            _exportedSvg = null;
            SyncEdgeControlsFromDocument();
            if (_diagram is not null) await _diagram.ReplaceAsync(_document, _revision);
            _activity = $"Live update from {change.ActorId} · revision {_revision}";
            StateHasChanged();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _activity = $"A live update could not be rendered: {exception.Message}";
            StateHasChanged();
            throw;
        }
        finally
        {
            _commandGate.Release();
            _eventGate.Release();
        }
    }

    private async Task LoadWorkspaceSelectionAsync()
    {
        var state = await Workspace.LoadAsync(CancellationToken.None);
        if (state is null) return;
        // Resolve availability in the dedicated load paths so a corrupt selected artifact enters recovery mode
        // instead of aborting component initialization or being silently replaced.
        _documentId = state.DocumentId;
        _paletteCatalogId = state.PaletteCatalogId;
    }

    private Task PersistWorkspaceSelectionAsync() =>
        Workspace.SaveAsync(new(DocumentId, _paletteCatalogId), CancellationToken.None);

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
                _documentDurable = true;
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
            _documentDurable = false;
            _activity = $"Opened a recoverable local design: {exception.Message}";
        }
    }

    private async Task LoadOrCreatePaletteCatalogAsync()
    {
        try
        {
            var snapshot = await PaletteCatalogs.GetAsync(_paletteCatalogId, CancellationToken.None);
            if (snapshot is null)
            {
                var initial = CapturePaletteCatalog(
                    _paletteCatalogId,
                    _paletteCatalogName,
                    revision: 1,
                    _paletteCatalogCreatedUtc);
                var created = await PaletteCatalogs.CreateAsync(initial, CancellationToken.None);
                if (created.Accepted && created.Snapshot is not null)
                {
                    ApplyPaletteCatalog(created.Snapshot);
                    _paletteCatalogDurable = true;
                }
                else if (await PaletteCatalogs.GetAsync(_paletteCatalogId, CancellationToken.None) is { } current)
                {
                    ApplyPaletteCatalog(current);
                    _paletteCatalogDurable = true;
                }
            }
            else
            {
                ApplyPaletteCatalog(snapshot);
                _paletteCatalogDurable = true;
            }
            _paletteLoadFailed = false;
            await RefreshPaletteCatalogsAsync();
        }
        catch (Exception exception)
        {
            _paletteCatalogDurable = false;
            _paletteLoadFailed = true;
            _activity = $"Opened the default palette after a saved palette error: {exception.Message}";
        }
    }

    private async Task RefreshPaletteCatalogsAsync() =>
        _paletteCatalogSummaries = await PaletteCatalogs.ListAsync(CancellationToken.None);

    private async Task<bool> PersistPaletteCatalogAsync()
    {
        await _paletteGate.WaitAsync();
        try
        {
            if (!_paletteCatalogDurable || _paletteLoadFailed)
            {
                _activity = _paletteLoadFailed
                    ? "The saved palette could not be read, so it was left untouched. Use Save as to recover this palette under a new name."
                    : "This palette has unsaved changes. Use Save as to preserve them under a new name.";
                return false;
            }

            var snapshot = CapturePaletteCatalog(
                _paletteCatalogId,
                _paletteCatalogName,
                _paletteCatalogRevision + 1,
                _paletteCatalogCreatedUtc);
            var result = await PaletteCatalogs.TrySaveAsync(snapshot, _paletteCatalogRevision, CancellationToken.None);
            if (!result.Accepted)
            {
                _paletteCatalogDurable = false;
                _activity = result.Code == "REVISION_CONFLICT"
                    ? "This palette changed in another session. Open the latest version or use Save as to preserve your local changes."
                    : $"Palette change could not be saved: {result.Message}";
                return false;
            }

            ApplyCommittedPaletteSnapshot(result.Snapshot ?? snapshot);
            _paletteCatalogDurable = true;
            return true;
        }
        catch (Exception exception)
        {
            _paletteCatalogDurable = false;
            _activity = $"Palette change could not be saved: {exception.Message}";
            return false;
        }
        finally { _paletteGate.Release(); }
    }

    private async Task PersistPaletteAndRenderAsync()
    {
        await PersistPaletteCatalogAsync();
        await InvokeAsync(StateHasChanged);
    }

    private PaletteCatalogSnapshot CapturePaletteCatalog(string catalogId, string name, long revision, DateTimeOffset createdUtc)
    {
        var empty = ImmutableDictionary<string, JsonElement>.Empty;
        var now = DateTimeOffset.UtcNow;
        var groups = _paletteCategories.Where(category => !category.IsSystem).Select((category, index) =>
            new PaletteGroupSnapshot(
                category.Name,
                category.PersistedGroup?.Label ?? category.Name,
                category.PersistedGroup?.Description,
                category.PersistedGroup?.Icon ?? "mdi:folder-outline",
                index,
                CloneMetadata(category.PersistedGroup?.Metadata))).ToArray();
        var customNodes = _templates.Where(template => template.IsCustom).Select(template =>
        {
            if (template.PersistedDefinition is { } persisted)
            {
                return persisted with
                {
                    Label = template.Label,
                    Description = template.Description,
                    Width = template.Width,
                    Height = template.Height,
                    IsGroup = template.IsGroup,
                    Icon = template.Icon,
                    Style = new(template.Outline, template.Background, template.TextColor, template.TextAlign),
                    Metadata = WithTemplateMetadata(template, persisted.Metadata)
                };
            }

            return new PaletteNodeDefinitionSnapshot(
                template.Id,
                template.Label,
                template.Description,
                template.Width,
                template.Height,
                template.IsGroup,
                template.Icon,
                new(template.Outline, template.Background, template.TextColor, template.TextAlign),
                template.Ports.Select((port, index) => new PalettePortDefinitionSnapshot(
                    port.Id, port.Side, port.Direction, port.Side, null, null, index, null, empty)).ToArray(),
                template.Properties.Select((property, index) => new PalettePropertyDefinitionSnapshot(
                    property.Id, property.Name, property.Type, property.Value?.Clone(), property.Mode, property.Label,
                    property.Connectable, property.Options.ToArray(), property.Direction, index, PropertyMetadata(property))).ToArray(),
                WithTemplateMetadata(template, empty));
        }).ToArray();
        var placements = _paletteCategories.SelectMany(category => _templates
            .Where(template => string.Equals(template.Category, category.Name, StringComparison.Ordinal))
            .Select((template, index) => new PaletteItemPlacementSnapshot(template.Id, template.Category, index))).ToArray();
        return new(
            PaletteCatalogSchema.CurrentVersion,
            catalogId,
            new(name, _paletteCatalogDescription, revision, createdUtc, now, CloneMetadata(_paletteCatalogAttributes)),
            groups,
            customNodes,
            placements,
            _expandedCategories.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            _paletteOpen,
            _palettePinned);
    }

    private void ApplyPaletteCatalog(PaletteCatalogSnapshot snapshot)
    {
        _paletteCategories.RemoveAll(category => !category.IsSystem);
        _templates.RemoveAll(template => template.IsCustom);

        var names = _paletteCategories.Select(category => category.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var group in snapshot.CustomGroups.OrderBy(group => group.Order).ThenBy(group => group.Id, StringComparer.Ordinal))
        {
            if (!names.Add(group.Id)) continue;
            _paletteCategories.Add(new(group.Id, false, group));
        }

        var placementByItem = snapshot.Placements.ToDictionary(placement => placement.ItemId, StringComparer.Ordinal);
        foreach (var node in snapshot.CustomNodes)
        {
            if (_templates.Any(template => string.Equals(template.Id, node.Id, StringComparison.Ordinal))) continue;
            var category = placementByItem.TryGetValue(node.Id, out var placement) && names.Contains(placement.GroupId)
                ? placement.GroupId
                : "Custom";
            _templates.Add(new(
                node.Id,
                category,
                node.Label,
                node.Description,
                node.Width,
                node.Height,
                node.Style.BorderColor,
                node.Style.Background,
                node.Style.Color,
                node.Style.TextAlign,
                node.Ports.Where(port => port.PropertyId is null).OrderBy(port => port.Order).Select(port => new PortTemplate(port.Id, port.Side, port.Direction)).ToArray(),
                node.IsGroup,
                NormalizeIcon(node.Icon) ?? "mdi:shape-outline",
                true,
                node.Properties.OrderBy(property => property.Order).Select(property => new PropertyTemplate(
                    property.Id,
                    property.Name,
                    property.Type,
                    property.DefaultValue?.Clone(),
                    property.Mode,
                    property.Label,
                    property.Connectable,
                    property.Options.ToArray(),
                    property.Direction,
                    ReadPropertySectionId(property.Metadata),
                    ReadPropertyEditor(property.Metadata))).ToArray(),
                ReadTemplateSections(node.Metadata),
                ReadTemplatePresentation(node.Metadata),
                PersistedDefinition: node));
        }

        for (var index = 0; index < _templates.Count; index++)
        {
            if (!placementByItem.TryGetValue(_templates[index].Id, out var placement) || !names.Contains(placement.GroupId)) continue;
            _templates[index] = _templates[index] with { Category = placement.GroupId };
        }

        var categoryOrder = _paletteCategories.Select((category, index) => (category.Name, index))
            .ToDictionary(item => item.Name, item => item.index, StringComparer.Ordinal);
        var originalOrder = _templates.Select((template, index) => (template.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        _templates.Sort((left, right) =>
        {
            var categoryComparison = categoryOrder.GetValueOrDefault(left.Category, int.MaxValue)
                .CompareTo(categoryOrder.GetValueOrDefault(right.Category, int.MaxValue));
            if (categoryComparison != 0) return categoryComparison;
            var leftOrder = placementByItem.TryGetValue(left.Id, out var leftPlacement) ? leftPlacement.Order : int.MaxValue;
            var rightOrder = placementByItem.TryGetValue(right.Id, out var rightPlacement) ? rightPlacement.Order : int.MaxValue;
            var placementComparison = leftOrder.CompareTo(rightOrder);
            return placementComparison != 0 ? placementComparison : originalOrder[left.Id].CompareTo(originalOrder[right.Id]);
        });

        _expandedCategories.Clear();
        foreach (var groupId in snapshot.ExpandedGroupIds.Where(names.Contains)) _expandedCategories.Add(groupId);
        _paletteOpen = snapshot.IsOpen;
        _palettePinned = snapshot.IsPinned;
        _paletteCatalogId = snapshot.CatalogId;
        _paletteCatalogName = snapshot.Catalog.Name;
        _paletteCatalogDescription = snapshot.Catalog.Description;
        _paletteCatalogAttributes = CloneMetadata(snapshot.Catalog.Attributes);
        _paletteCatalogRevision = snapshot.Catalog.Revision;
        _paletteCatalogCreatedUtc = snapshot.Catalog.CreatedAtUtc;
        _draft.Category = names.Contains(_draft.Category) ? _draft.Category : "Custom";
        UpdateTemplateSequence();
    }

    private void ApplyCommittedPaletteSnapshot(PaletteCatalogSnapshot snapshot)
    {
        _paletteCatalogRevision = snapshot.Catalog.Revision;
        _paletteCatalogCreatedUtc = snapshot.Catalog.CreatedAtUtc;
        _paletteCatalogDescription = snapshot.Catalog.Description;
        _paletteCatalogAttributes = CloneMetadata(snapshot.Catalog.Attributes);
        var definitions = snapshot.CustomNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        for (var index = 0; index < _templates.Count; index++)
        {
            if (_templates[index].IsCustom && definitions.TryGetValue(_templates[index].Id, out var definition))
                _templates[index] = _templates[index] with { PersistedDefinition = definition };
        }
        var groups = snapshot.CustomGroups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        for (var index = 0; index < _paletteCategories.Count; index++)
        {
            if (!_paletteCategories[index].IsSystem && groups.TryGetValue(_paletteCategories[index].Name, out var group))
                _paletteCategories[index] = _paletteCategories[index] with { PersistedGroup = group };
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> CloneMetadata(IReadOnlyDictionary<string, JsonElement>? metadata) =>
        metadata is null
            ? ImmutableDictionary<string, JsonElement>.Empty
            : metadata.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, JsonElement> WithTemplateMetadata(NodeTemplate template, IReadOnlyDictionary<string, JsonElement>? metadata)
    {
        var result = CloneMetadata(metadata).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (template.Sections.Count == 0) result.Remove("ghostagram.sections");
        else result["ghostagram.sections"] = JsonSerializer.SerializeToElement(template.Sections, JsonOptions);
        if (template.Presentation is null) result.Remove("ghostagram.presentation");
        else result["ghostagram.presentation"] = JsonSerializer.SerializeToElement(template.Presentation, JsonOptions);
        return result;
    }

    private static IReadOnlyDictionary<string, JsonElement> PropertyMetadata(PropertyTemplate property)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (property.SectionId is not null) result["ghostagram.sectionId"] = JsonSerializer.SerializeToElement(property.SectionId);
        if (property.Editor is not null) result["ghostagram.editor"] = JsonSerializer.SerializeToElement(property.Editor, JsonOptions);
        return result;
    }

    private static IReadOnlyList<DiagramNodeSection> ReadTemplateSections(IReadOnlyDictionary<string, JsonElement> metadata) =>
        metadata.TryGetValue("ghostagram.sections", out var value)
            ? JsonSerializer.Deserialize<IReadOnlyList<DiagramNodeSection>>(value.GetRawText(), JsonOptions) ?? []
            : [];

    private static DiagramNodePresentation? ReadTemplatePresentation(IReadOnlyDictionary<string, JsonElement> metadata) =>
        metadata.TryGetValue("ghostagram.presentation", out var value)
            ? JsonSerializer.Deserialize<DiagramNodePresentation>(value.GetRawText(), JsonOptions)
            : null;

    private static string? ReadPropertySectionId(IReadOnlyDictionary<string, JsonElement> metadata) =>
        metadata.TryGetValue("ghostagram.sectionId", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DiagramPropertyEditor? ReadPropertyEditor(IReadOnlyDictionary<string, JsonElement> metadata) =>
        metadata.TryGetValue("ghostagram.editor", out var value)
            ? JsonSerializer.Deserialize<DiagramPropertyEditor>(value.GetRawText(), JsonOptions)
            : null;

    private static DiagramNodePresentation? ClonePresentation(DiagramNodePresentation? presentation) => presentation is null
        ? null
        : presentation with { CollapsedSectionIds = presentation.CollapsedSectionIds?.ToArray() };

    private void UpdateTemplateSequence()
    {
        _templateSequence = 0;
        foreach (var template in _templates.Where(template => template.IsCustom && template.Id.StartsWith("custom-", StringComparison.Ordinal)))
            if (int.TryParse(template.Id.AsSpan("custom-".Length), out var sequence)) _templateSequence = Math.Max(_templateSequence, sequence);
    }

    private async Task RefreshDocumentCatalogAsync()
    {
        _documents = await Documents.ListAsync(CancellationToken.None);
        var current = _documents.FirstOrDefault(item => string.Equals(item.DocumentId, DocumentId, StringComparison.Ordinal));
        if (current is not null) _documentDisplayName = current.DisplayName;
    }

    private Task SaveCurrentAsync()
    {
        _activity = _documentDurable
            ? $"{_documentDisplayName} is saved at revision {_revision}"
            : $"{_documentDisplayName} is open in recovery mode and is not saved. Use Save as to preserve it under a new name.";
        return Task.CompletedTask;
    }

    private void OpenSaveAs()
    {
        _saveAsName = $"{_documentDisplayName} copy";
        _isSaveAsOpen = true;
    }

    private void CloseSaveAs() => _isSaveAsOpen = false;

    private async Task OpenDiagramLibraryAsync()
    {
        await RefreshDocumentCatalogAsync();
        _isDiagramLibraryOpen = true;
    }

    private void CloseDiagramLibrary() => _isDiagramLibraryOpen = false;

    private async Task SaveAsAsync()
    {
        var name = _saveAsName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _activity = "Enter a name for the diagram";
            return;
        }

        var documentId = SlugifyDocumentId(name);
        if (_documents.Any(item => string.Equals(item.DocumentId, documentId, StringComparison.Ordinal)))
        {
            _activity = $"A diagram named {name} already exists";
            return;
        }

        await _eventGate.WaitAsync();
        await _commandGate.WaitAsync();
        _busy = true;
        try
        {
            DiagramCommandResult result;
            if (_documentDurable)
            {
                result = await Commands.CloneAsync(DocumentId, documentId, name, CancellationToken.None);
            }
            else
            {
                var target = _document with { DocumentId = documentId };
                result = await Commands.CreateFromSnapshotAsync(
                    documentId,
                    name,
                    JsonSerializer.SerializeToElement(target, JsonOptions),
                    CancellationToken.None);
            }
            if (!result.Accepted || result.Snapshot is null)
            {
                _activity = $"Diagram was not saved: {result.Message}";
                return;
            }

            _documentId = documentId;
            _documentDisplayName = name;
            _document = Deserialize(result.Snapshot);
            _revision = result.Revision;
            SubscribeToDocumentChanges();
            _documentDurable = true;
            _undo.Clear();
            _redo.Clear();
            if (_diagram is not null) await _diagram.ReplaceAsync(_document, _revision);
            await RefreshDocumentCatalogAsync();
            _isSaveAsOpen = false;
            _activity = $"Saved and opened {name}";
            await PersistWorkspaceSelectionAsync();
        }
        catch (Exception exception)
        {
            _activity = $"Diagram was not saved: {exception.Message}";
        }
        finally
        {
            _busy = false;
            _commandGate.Release();
            _eventGate.Release();
        }
    }

    private async Task OpenDocumentAsync(string documentId)
    {
        if (_busy || string.Equals(documentId, DocumentId, StringComparison.Ordinal))
        {
            _isDiagramLibraryOpen = false;
            return;
        }

        await _eventGate.WaitAsync();
        await _commandGate.WaitAsync();
        _busy = true;
        try
        {
            var snapshot = await Commands.GetSnapshotAsync(documentId, CancellationToken.None);
            if (snapshot is null)
            {
                _activity = "That saved diagram is no longer available";
                await RefreshDocumentCatalogAsync();
                return;
            }

            var document = Deserialize(snapshot);
            _documentId = documentId;
            _documentDisplayName = _documents.FirstOrDefault(item => item.DocumentId == documentId)?.DisplayName ?? documentId;
            _document = document;
            _revision = snapshot.Revision;
            SubscribeToDocumentChanges();
            _documentDurable = true;
            _undo.Clear();
            _redo.Clear();
            _exportedSvg = null;
            SyncEdgeControlsFromDocument();
            if (_diagram is not null) await _diagram.ReplaceAsync(_document, _revision);
            _isDiagramLibraryOpen = false;
            _activity = $"Opened {_documentDisplayName}";
            await PersistWorkspaceSelectionAsync();
        }
        catch (Exception exception)
        {
            _activity = $"Diagram could not be opened: {exception.Message}";
        }
        finally
        {
            _busy = false;
            _commandGate.Release();
            _eventGate.Release();
        }
    }

    private async Task SavePaletteAsync()
    {
        if (await PersistPaletteCatalogAsync())
            _activity = $"{_paletteCatalogName} is saved at revision {_paletteCatalogRevision}";
    }

    private async Task OpenPaletteSaveAsAsync()
    {
        await RefreshPaletteCatalogsAsync();
        _paletteSaveAsName = $"{_paletteCatalogName} copy";
        _isPaletteSaveAsOpen = true;
    }

    private void ClosePaletteSaveAs() => _isPaletteSaveAsOpen = false;

    private async Task OpenPaletteLibraryAsync()
    {
        await RefreshPaletteCatalogsAsync();
        _isPaletteLibraryOpen = true;
    }

    private void ClosePaletteLibrary() => _isPaletteLibraryOpen = false;

    private async Task SavePaletteAsAsync()
    {
        var name = _paletteSaveAsName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _activity = "Enter a name for the palette";
            return;
        }
        var catalogId = SlugifyPaletteCatalogId(name);
        if (_paletteCatalogSummaries.Any(item => string.Equals(item.CatalogId, catalogId, StringComparison.Ordinal)))
        {
            _activity = $"A palette named {name} already exists";
            return;
        }

        await _paletteGate.WaitAsync();
        try
        {
            var created = DateTimeOffset.UtcNow;
            var snapshot = CapturePaletteCatalog(catalogId, name, 1, created);
            var result = await PaletteCatalogs.CreateAsync(snapshot, CancellationToken.None);
            if (!result.Accepted || result.Snapshot is null)
            {
                _activity = $"Palette was not saved: {result.Message}";
                return;
            }
            ApplyPaletteCatalog(result.Snapshot);
            _paletteCatalogDurable = true;
            _paletteLoadFailed = false;
            await RefreshPaletteCatalogsAsync();
            _isPaletteSaveAsOpen = false;
            _activity = $"Saved and opened {name}";
            await PersistWorkspaceSelectionAsync();
        }
        catch (Exception exception)
        {
            _activity = $"Palette was not saved: {exception.Message}";
        }
        finally { _paletteGate.Release(); }
    }

    private async Task OpenPaletteCatalogAsync(string catalogId)
    {
        if (string.Equals(catalogId, _paletteCatalogId, StringComparison.Ordinal))
        {
            _isPaletteLibraryOpen = false;
            return;
        }

        await _paletteGate.WaitAsync();
        try
        {
            var snapshot = await PaletteCatalogs.GetAsync(catalogId, CancellationToken.None);
            if (snapshot is null)
            {
                _activity = "That saved palette is no longer available";
                await RefreshPaletteCatalogsAsync();
                return;
            }
            ApplyPaletteCatalog(snapshot);
            _paletteCatalogDurable = true;
            _paletteLoadFailed = false;
            _isPaletteLibraryOpen = false;
            _activity = $"Opened {_paletteCatalogName}";
            await PersistWorkspaceSelectionAsync();
        }
        catch (Exception exception)
        {
            _activity = $"Palette could not be opened: {exception.Message}";
        }
        finally { _paletteGate.Release(); }
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

    private async Task TogglePalette()
    {
        _paletteOpen = !_paletteOpen;
        await PersistPaletteCatalogAsync();
    }

    private async Task TogglePalettePin()
    {
        _palettePinned = !_palettePinned;
        _paletteOpen = true;
        await PersistPaletteCatalogAsync();
    }

    private async Task SetCategoryExpandedAsync(string category, bool expanded)
    {
        if (expanded) _expandedCategories.Add(category);
        else _expandedCategories.Remove(category);
        await PersistPaletteCatalogAsync();
    }

    private Task SetCategoryExpanded(GhostPaletteGroupToggleRequest request) => SetCategoryExpandedAsync(request.GroupId, request.Expanded);

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
    private void CloseNodePropertiesEditor() => _isPropertiesEditorOpen = false;
    private void CloseExport() => _isExportOpen = false;

    private bool HasSelectedGroups => _document.Groups.Any(group => _document.Selection.Contains(group.Id, StringComparer.Ordinal));
    private bool HasSelectedEdges => _document.Edges.Any(edge => _document.Selection.Contains(edge.Id, StringComparer.Ordinal));
    private bool CanEditSelectedNode => SelectedNode() is not null;
    private string EdgeApplyLabel => HasSelectedEdges ? "Apply selected" : "Apply all edges";
    private string StyleNodeLabel => _document.Nodes.SingleOrDefault(node => node.Id == _styleNodeId)?.Label ?? "Node style";
    private string PropertiesNodeLabel => _document.Nodes.SingleOrDefault(node => node.Id == _propertiesNodeId)?.Label ?? "Node properties";
    private bool IsRegisteredPropertiesLocked => _document.Nodes.SingleOrDefault(node => node.Id == _propertiesNodeId)?.TypeId is not null;
    private int ConnectionsToRemove => BuildPropertiesEditPlan()?.RemovedEdgeIds.Count ?? 0;
    private IReadOnlyList<GhostPaletteGroup> PaletteGroups => _paletteCategories
        .Select(category => new GhostPaletteGroup(
            category.Name,
            category.PersistedGroup?.Label ?? category.Name,
            _expandedCategories.Contains(category.Name),
            !category.IsSystem))
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

    private async Task AddPaletteGroup()
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
        await PersistPaletteCatalogAsync();
    }

    private async Task RemovePaletteGroup(string name)
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
        await PersistPaletteCatalogAsync();
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
        return PersistPaletteAndRenderAsync();
    }

    private async Task DeleteCustomTemplate(string templateId)
    {
        var template = _templates.SingleOrDefault(item => item.Id == templateId && item.IsCustom);
        if (template is null) return;
        _templates.Remove(template);
        _activity = $"Deleted custom node type {template.Label} from the palette";
        await PersistPaletteCatalogAsync();
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

    private void OpenNodePropertiesEditor()
    {
        var node = SelectedNode();
        if (node is null) return;
        _propertiesNodeId = node.Id;
        _propertiesEditorBaseRevision = _revision;
        _propertiesEditorError = null;
        _propertyEditorDrafts.Clear();
        _propertyEditorDrafts.AddRange(node.Properties.OrderBy(property => PropertyOrder(node, property.Id)).Select(property => new PropertyEditorDraft(property.Id, property)
        {
            Name = property.Name, Type = property.Type, Mode = property.Mode, Value = PropertyValueText(property.Value),
            Options = string.Join(", ", property.Options ?? []), Connection = PropertyConnection(node, property.Id),
            SectionId = property.SectionId, EditorKind = property.Editor?.Kind ?? DiagramPropertyEditorKinds.Auto,
            Placeholder = property.Editor?.Placeholder ?? string.Empty,
            Minimum = property.Editor?.Minimum?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            Maximum = property.Editor?.Maximum?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            Step = property.Editor?.Step?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
        }));
        _sectionEditorDrafts.Clear();
        _sectionEditorDrafts.AddRange((node.Sections ?? []).OrderBy(section => section.Order).Select(section => new SectionEditorDraft(section.Id, section)
        {
            Title = section.Title,
            ParentSectionId = section.ParentSectionId,
            Collapsible = section.Collapsible,
            Collapsed = node.Presentation?.CollapsedSectionIds?.Contains(section.Id, StringComparer.Ordinal) == true
        }));
        _presentationDraft = new PresentationDraft
        {
            Enabled = node.Presentation is not null,
            DisplayMode = node.Presentation?.DisplayMode ?? DiagramNodeDisplayModes.Expanded,
            ExpandedHeight = node.Presentation?.ExpandedHeight ?? node.Height
        };
        _portEditorDrafts.Clear();
        _portEditorDrafts.AddRange(_document.Ports.Where(port => port.NodeId == node.Id && port.PropertyId is null).OrderBy(port => port.Order).Select(port => new PortEditorDraft(port.Id, port)
        {
            Label = port.Label ?? port.Id, Side = PortSideFromAnchor(port.Anchor), Direction = port.Direction
        }));
        _isPropertiesEditorOpen = true;
    }

    private void AddEditorProperty()
    {
        if (IsRegisteredPropertiesLocked || _propertyEditorDrafts.Count >= MaxDesignedProperties) return;
        _propertyEditorDrafts.Add(new PropertyEditorDraft(NextPropertiesEditorId("property")) { Name = "Property" });
    }

    private void AddEditorSection()
    {
        if (IsRegisteredPropertiesLocked) return;
        var id = NextPropertiesEditorId("section");
        _sectionEditorDrafts.Add(new SectionEditorDraft(id) { Title = "Section" });
        _presentationDraft.Enabled = true;
    }
    private void RemoveEditorSection(string id)
    {
        if (IsRegisteredPropertiesLocked) return;
        _sectionEditorDrafts.RemoveAll(section => section.Id == id);
        foreach (var property in _propertyEditorDrafts.Where(property => property.SectionId == id)) property.SectionId = null;
        foreach (var section in _sectionEditorDrafts.Where(section => section.ParentSectionId == id)) section.ParentSectionId = null;
    }
    private void RemoveEditorProperty(string id) => _propertyEditorDrafts.RemoveAll(property => property.Id == id);
    private void MoveEditorProperty(string id, int delta)
    {
        var index = _propertyEditorDrafts.FindIndex(property => property.Id == id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _propertyEditorDrafts.Count) return;
        (_propertyEditorDrafts[index], _propertyEditorDrafts[target]) = (_propertyEditorDrafts[target], _propertyEditorDrafts[index]);
    }
    private void AddEditorPort()
    {
        if (!IsRegisteredPropertiesLocked) _portEditorDrafts.Add(new PortEditorDraft(NextPropertiesEditorId("port")) { Label = "Connection" });
    }
    private void RemoveEditorPort(string id) => _portEditorDrafts.RemoveAll(port => port.Id == id);

    private async Task ApplyNodePropertiesAsync()
    {
        _propertiesEditorError = null;
        if (_propertiesEditorBaseRevision != _revision) { _propertiesEditorError = "The diagram changed while this editor was open. Reopen it to apply against the latest revision."; return; }
        var plan = BuildPropertiesEditPlan();
        if (plan is null) return;
        if (plan.Error is not null) { _propertiesEditorError = plan.Error; return; }
        if (await SubmitOperationsAsync(plan.Operations, $"Updated {PropertiesNodeLabel} properties and connections")) _isPropertiesEditorOpen = false;
    }

    private PropertiesEditPlan? BuildPropertiesEditPlan()
    {
        var node = _document.Nodes.SingleOrDefault(item => item.Id == _propertiesNodeId);
        if (node is null) return null;
        if (_propertyEditorDrafts.Count > MaxDesignedProperties) return PropertiesEditPlan.Invalid("A node can have at most 17 properties.");
        if (_propertyEditorDrafts.Any(property => string.IsNullOrWhiteSpace(property.Name))) return PropertiesEditPlan.Invalid("Every property needs a name.");
        if (_propertyEditorDrafts.GroupBy(property => property.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)) return PropertiesEditPlan.Invalid("Property names must be unique.");
        var locked = node.TypeId is not null;
        if (_propertyEditorDrafts.GroupBy(property => property.Id, StringComparer.Ordinal).Any(group => group.Count() > 1) || _portEditorDrafts.GroupBy(port => port.Id, StringComparer.Ordinal).Any(group => group.Count() > 1)) return PropertiesEditPlan.Invalid("Property and connection point IDs must be unique.");
        var originalPropertyIds = node.Properties.Select(property => property.Id).ToHashSet(StringComparer.Ordinal);
        var desiredProperties = new List<DiagramNodeProperty>();
        foreach (var draft in _propertyEditorDrafts)
        {
            var validation = ValidateEditorProperty(draft);
            if (validation.Error is not null) return PropertiesEditPlan.Invalid(validation.Error);
            var original = draft.Original;
            desiredProperties.Add(locked && original is not null
                ? original with { Value = validation.Value }
                : original is null
                    ? new DiagramNodeProperty(draft.Id, draft.Name.Trim(), draft.Type.Trim(), validation.Value, draft.Mode, draft.Name.Trim(), Connectable: draft.Connection != "none", Options: validation.Options, SectionId: draft.SectionId, Editor: CreateEditor(draft))
                    : original with
                    {
                        Name = draft.Name.Trim(),
                        Label = original.Label is not null && string.Equals(original.Label, original.Name, StringComparison.Ordinal) ? draft.Name.Trim() : original.Label,
                        Type = draft.Type.Trim(),
                        Value = validation.Value,
                        Mode = draft.Mode,
                        Connectable = draft.Connection != "none",
                        Options = validation.Options,
                        SectionId = draft.SectionId,
                        Editor = CreateEditor(draft)
                    });
        }
        if (locked && (!originalPropertyIds.SetEquals(desiredProperties.Select(property => property.Id)))) return PropertiesEditPlan.Invalid("Registered node schema changes are not allowed.");
        var desiredSections = locked ? node.Sections ?? [] : BuildDesiredSections();
        var sectionError = ValidateSections(desiredSections, desiredProperties);
        if (sectionError is not null) return PropertiesEditPlan.Invalid(sectionError);
        var presentation = BuildPresentation(node, desiredProperties, desiredSections);
        if (presentation.Error is not null) return PropertiesEditPlan.Invalid(presentation.Error);
        var currentPorts = _document.Ports.Where(port => port.NodeId == node.Id).ToArray();
        var desiredPorts = locked ? currentPorts : BuildDesiredPorts(node, desiredProperties);
        var desiredPortIds = desiredPorts.Select(port => port.Id).ToHashSet(StringComparer.Ordinal);
        var removedPortIds = currentPorts.Where(port => !desiredPortIds.Contains(port.Id)).Select(port => port.Id).ToHashSet(StringComparer.Ordinal);
        var incompatibleEdges = _document.Edges.Where(edge => !EdgeRolesRemainCompatible(edge, desiredPorts)).Select(edge => edge.Id);
        var removedEdges = _document.Edges.Where(edge => removedPortIds.Contains(edge.SourcePortId) || removedPortIds.Contains(edge.TargetPortId)).Select(edge => edge.Id).Concat(incompatibleEdges).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var height = presentation.Height;
        var updatedNode = node with { Properties = desiredProperties, Sections = desiredSections.Count == 0 ? null : desiredSections, Presentation = presentation.Value };
        updatedNode = updatedNode with { Height = height };
        var operations = new List<GhostagramOperation>();
        operations.AddRange(removedEdges.Select(DiagramOperations.RemoveEdge));
        operations.AddRange(removedPortIds.Order(StringComparer.Ordinal).Select(DiagramOperations.RemovePort));
        operations.Add(DiagramOperations.Upsert(updatedNode));
        operations.AddRange(desiredPorts.Select(DiagramOperations.Upsert));
        return new PropertiesEditPlan(operations, removedEdges, null);
    }

    private DiagramPort[] BuildDesiredPorts(DiagramNode node, IReadOnlyList<DiagramNodeProperty> properties)
    {
        var existing = _document.Ports.Where(port => port.NodeId == node.Id).ToDictionary(port => port.Id, StringComparer.Ordinal);
        var result = new List<DiagramPort>();
        var matchedPortIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties.Select((value, index) => (value, index)))
        {
            var connection = _propertyEditorDrafts[property.index].Connection;
            var existingBound = existing.Values.Where(port => port.PropertyId == property.value.Id).ToArray();
            if (connection == "both" && existingBound.Length == 1 && existingBound[0].Direction == "both")
            {
                var currentBoth = existingBound[0];
                matchedPortIds.Add(currentBoth.Id);
                result.Add(currentBoth with
                {
                    PropertyId = property.value.Id,
                    Label = UpdatedPropertyPortLabel(currentBoth, _propertyEditorDrafts[property.index].Original, property.value),
                    Order = property.index
                });
                continue;
            }
            foreach (var generated in PropertyPorts(node.Id, property.value, connection, property.index, node.Style?.BorderColor ?? "#4177de"))
            {
                var lane = generated.Direction;
                var current = existing.Values.FirstOrDefault(port => !matchedPortIds.Contains(port.Id) && port.PropertyId == property.value.Id && PortAllows(port.Direction, lane));
                if (current is not null) matchedPortIds.Add(current.Id);
                result.Add(current is not null
                    ? current with
                    {
                        Direction = generated.Direction,
                        PropertyId = generated.PropertyId,
                        Label = UpdatedPropertyPortLabel(current, _propertyEditorDrafts[property.index].Original, property.value),
                        Order = generated.Order
                    }
                    : generated);
            }
        }
        foreach (var draft in _portEditorDrafts.Select((value, index) => (value, index)))
        {
            var port = new DiagramPort(draft.value.Id, node.Id, draft.value.Direction, Anchor: draft.value.Side, Endpoint: new DiagramEndpoint("dot", 10, node.Style?.BorderColor ?? "#4177de", "#ffffff", 2), Label: draft.value.Label.Trim(), Order: properties.Count + draft.index);
            result.Add(existing.TryGetValue(port.Id, out var current)
                ? current with
                {
                    Direction = port.Direction,
                    Anchor = string.Equals(draft.value.Side, draft.value.OriginalSide, StringComparison.Ordinal) ? current.Anchor : port.Anchor,
                    Label = string.Equals(draft.value.Label, draft.value.OriginalLabel, StringComparison.Ordinal) ? current.Label : port.Label,
                    Order = port.Order
                }
                : port);
        }
        return result.ToArray();
    }

    private static PropertyValidation ValidateEditorProperty(PropertyEditorDraft draft)
    {
        if (!KnownPropertyTypes.Contains(draft.Type))
        {
            if (draft.Original is null) return PropertyValidation.Invalid($"{draft.Name}: custom types must originate from an existing property.");
            if (!string.Equals(draft.Type, draft.Original.Type, StringComparison.Ordinal)) return PropertyValidation.Invalid($"{draft.Name}: custom property types cannot be converted in this editor.");
            return new(draft.Original.Value?.Clone(), draft.Original.Options ?? [], null);
        }
        if (draft.Mode is not (DiagramPropertyModes.Display or DiagramPropertyModes.Edit or DiagramPropertyModes.DisplayAndEdit or DiagramPropertyModes.Hidden)) return PropertyValidation.Invalid($"{draft.Name}: mode is not supported.");
        var editorError = ValidateEditor(draft);
        if (editorError is not null) return PropertyValidation.Invalid($"{draft.Name}: {editorError}");
        var options = draft.Type == DiagramPropertyTypes.Enum ? draft.Options.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray() : [];
        if (draft.Type == DiagramPropertyTypes.Enum && options.Length == 0) return PropertyValidation.Invalid($"{draft.Name}: an enum needs at least one choice.");
        if (string.IsNullOrWhiteSpace(draft.Value)) return draft.Original?.Required == true ? PropertyValidation.Invalid($"{draft.Name} is required.") : new(null, options, null);
        var raw = draft.Value;
        if (draft.Type == DiagramPropertyTypes.Boolean) return bool.TryParse(raw, out var boolean) ? new(JsonSerializer.SerializeToElement(boolean), options, null) : PropertyValidation.Invalid($"{draft.Name}: expected true or false.");
        if (draft.Type == DiagramPropertyTypes.Integer) return long.TryParse(raw, out var integer) ? new(JsonSerializer.SerializeToElement(integer), options, null) : PropertyValidation.Invalid($"{draft.Name}: expected an integer.");
        if (draft.Type == DiagramPropertyTypes.Decimal) return decimal.TryParse(raw, out var decimalValue) ? new(JsonSerializer.SerializeToElement(decimalValue), options, null) : PropertyValidation.Invalid($"{draft.Name}: expected a decimal number.");
        if (draft.Type == DiagramPropertyTypes.Date && DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var date)) return new(JsonSerializer.SerializeToElement(date.ToString("yyyy-MM-dd")), options, null);
        if (draft.Type == DiagramPropertyTypes.Date) return PropertyValidation.Invalid($"{draft.Name}: expected an ISO date.");
        if (draft.Type == DiagramPropertyTypes.DateTime && DateTimeOffset.TryParseExact(raw, ["O", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"], null, System.Globalization.DateTimeStyles.RoundtripKind, out var dateTime)) return new(JsonSerializer.SerializeToElement(dateTime.ToString("O")), options, null);
        if (draft.Type == DiagramPropertyTypes.DateTime) return PropertyValidation.Invalid($"{draft.Name}: expected an ISO date/time.");
        if (draft.Type == DiagramPropertyTypes.Json) { try { return new(JsonDocument.Parse(raw).RootElement.Clone(), options, null); } catch (JsonException) { return PropertyValidation.Invalid($"{draft.Name}: expected valid JSON."); } }
        if (draft.Type == DiagramPropertyTypes.Enum && !options.Contains(raw, StringComparer.Ordinal)) return PropertyValidation.Invalid($"{draft.Name}: value must be one of its choices.");
        return new(JsonSerializer.SerializeToElement(raw), options, null);
    }

    private static DiagramPropertyEditor? CreateEditor(PropertyEditorDraft draft)
    {
        if (draft.EditorKind == DiagramPropertyEditorKinds.Auto && string.IsNullOrWhiteSpace(draft.Placeholder) && string.IsNullOrWhiteSpace(draft.Minimum) && string.IsNullOrWhiteSpace(draft.Maximum) && string.IsNullOrWhiteSpace(draft.Step)) return null;
        return new DiagramPropertyEditor(
            draft.EditorKind,
            string.IsNullOrWhiteSpace(draft.Placeholder) ? null : draft.Placeholder.Trim(),
            ParseOptionalDecimal(draft.Minimum),
            ParseOptionalDecimal(draft.Maximum),
            ParseOptionalDecimal(draft.Step));
    }

    private static string? ValidateEditor(PropertyEditorDraft draft)
    {
        var compatible = draft.EditorKind switch
        {
            DiagramPropertyEditorKinds.Auto => true,
            DiagramPropertyEditorKinds.Text or DiagramPropertyEditorKinds.Multiline or DiagramPropertyEditorKinds.Color => draft.Type == DiagramPropertyTypes.String,
            DiagramPropertyEditorKinds.Toggle => draft.Type == DiagramPropertyTypes.Boolean,
            DiagramPropertyEditorKinds.Number or DiagramPropertyEditorKinds.Range => draft.Type is DiagramPropertyTypes.Integer or DiagramPropertyTypes.Decimal,
            DiagramPropertyEditorKinds.Date => draft.Type == DiagramPropertyTypes.Date,
            DiagramPropertyEditorKinds.DateTime => draft.Type == DiagramPropertyTypes.DateTime,
            DiagramPropertyEditorKinds.Select => draft.Type == DiagramPropertyTypes.Enum,
            DiagramPropertyEditorKinds.Json => draft.Type == DiagramPropertyTypes.Json,
            _ => false
        };
        if (!compatible) return "editor is not compatible with the selected type";
        if (!string.IsNullOrWhiteSpace(draft.Minimum) && ParseOptionalDecimal(draft.Minimum) is null || !string.IsNullOrWhiteSpace(draft.Maximum) && ParseOptionalDecimal(draft.Maximum) is null || !string.IsNullOrWhiteSpace(draft.Step) && ParseOptionalDecimal(draft.Step) is null)
            return "numeric editor settings must be decimal numbers";
        var minimum = ParseOptionalDecimal(draft.Minimum);
        var maximum = ParseOptionalDecimal(draft.Maximum);
        var step = ParseOptionalDecimal(draft.Step);
        if (minimum is not null && maximum is not null && minimum > maximum) return "minimum cannot exceed maximum";
        if (step is <= 0) return "step must be positive";
        if ((minimum is not null || maximum is not null || step is not null) && draft.EditorKind is not (DiagramPropertyEditorKinds.Number or DiagramPropertyEditorKinds.Range)) return "bounds are supported only by number and range editors";
        return null;
    }

    private static decimal? ParseOptionalDecimal(string? value) => decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : null;

    private IReadOnlyList<DiagramNodeSection> BuildDesiredSections() => _sectionEditorDrafts.Select((section, index) => new DiagramNodeSection(section.Id, section.Title.Trim(), section.ParentSectionId, index, section.Collapsible)).ToArray();

    private static string? ValidateSections(IReadOnlyList<DiagramNodeSection> sections, IReadOnlyList<DiagramNodeProperty> properties)
    {
        if (sections.Any(section => string.IsNullOrWhiteSpace(section.Id) || string.IsNullOrWhiteSpace(section.Title))) return "Every section needs a title.";
        if (sections.GroupBy(section => section.Id, StringComparer.Ordinal).Any(group => group.Count() > 1)) return "Section IDs must be unique.";
        var byId = sections.ToDictionary(section => section.Id, StringComparer.Ordinal);
        foreach (var section in sections)
        {
            if (section.ParentSectionId is { } parent && (!byId.ContainsKey(parent) || parent == section.Id)) return $"{section.Title}: select a valid parent section.";
            var seen = new HashSet<string>(StringComparer.Ordinal) { section.Id };
            var parentId = section.ParentSectionId;
            var depth = 0;
            while (parentId is not null)
            {
                if (!seen.Add(parentId)) return $"{section.Title}: sections cannot contain a parent cycle.";
                if (!byId.TryGetValue(parentId, out var parentSection)) return $"{section.Title}: select a valid parent section.";
                parentId = parentSection.ParentSectionId;
                if (++depth > 4) return $"{section.Title}: sections can be nested at most four levels.";
            }
        }
        var ids = sections.Select(section => section.Id).ToHashSet(StringComparer.Ordinal);
        var ungrouped = properties.FirstOrDefault(property => property.SectionId is not null && !ids.Contains(property.SectionId));
        return ungrouped is null ? null : $"{ungrouped.Name}: select an existing section.";
    }

    private (DiagramNodePresentation? Value, double Height, string? Error) BuildPresentation(
        DiagramNode node,
        IReadOnlyList<DiagramNodeProperty> properties,
        IReadOnlyList<DiagramNodeSection> sections)
    {
        if (!_presentationDraft.Enabled && sections.Count == 0)
        {
            var minimum = ProjectNodeMinimumHeight(properties, sections, DiagramNodeDisplayModes.Expanded, []);
            return (null, GridCeilingDimension(Math.Max(node.Height, minimum), 32), null);
        }

        var collapsed = _sectionEditorDrafts.Where(section => section.Collapsed).Select(section => section.Id).ToArray();
        return ResolvePresentationGeometry(
            node,
            properties,
            sections,
            _presentationDraft.DisplayMode,
            collapsed,
            _presentationDraft.ExpandedHeight,
            allowRequestedExpandedHeight: true);
    }

    private static (DiagramNodePresentation? Value, double Height, string? Error) ResolvePresentationGeometry(
        DiagramNode node,
        IReadOnlyList<DiagramNodeProperty> properties,
        IReadOnlyList<DiagramNodeSection> sections,
        string displayMode,
        IReadOnlyList<string>? collapsedSectionIds,
        double? requestedExpandedHeight,
        bool allowRequestedExpandedHeight)
    {
        if (displayMode is not (DiagramNodeDisplayModes.Expanded or DiagramNodeDisplayModes.Compact or DiagramNodeDisplayModes.Collapsed))
            return (null, 0, "Choose a valid node display mode.");

        var collapsed = collapsedSectionIds ?? [];
        if (collapsed.Any(string.IsNullOrWhiteSpace) || collapsed.Distinct(StringComparer.Ordinal).Count() != collapsed.Count)
            return (null, 0, "Collapsed section IDs must be unique and non-empty.");
        var collapsible = sections.Where(section => section.Collapsible).Select(section => section.Id).ToHashSet(StringComparer.Ordinal);
        if (collapsed.Any(id => !collapsible.Contains(id))) return (null, 0, "Only collapsible sections can be collapsed.");

        if (requestedExpandedHeight is not null && (!double.IsFinite(requestedExpandedHeight.Value) || requestedExpandedHeight <= 0))
            return (null, 0, "Expanded height must be a positive number.");
        var collapsedIds = collapsed.Order(StringComparer.Ordinal).ToArray();
        var expandedMinimum = GridCeilingDimension(ProjectNodeMinimumHeight(properties, sections, DiagramNodeDisplayModes.Expanded, collapsedIds), 32);
        var authoritativeExpanded = AuthoritativeExpandedHeight(node, properties, sections, collapsedIds);
        var expandedHeight = GridCeilingDimension(Math.Max(allowRequestedExpandedHeight && requestedExpandedHeight is { } requested ? requested : authoritativeExpanded, expandedMinimum), 32);
        var minimum = GridCeilingDimension(ProjectNodeMinimumHeight(properties, sections, displayMode, collapsedIds), 32);
        var height = displayMode switch
        {
            DiagramNodeDisplayModes.Expanded => Math.Max(expandedHeight, minimum),
            DiagramNodeDisplayModes.Compact => minimum,
            _ => GridCeilingDimension(NodeHeaderHeight, 32)
        };
        return (new DiagramNodePresentation(displayMode, expandedHeight, collapsedIds.Length == 0 ? null : collapsedIds), height, null);
    }

    private static double AuthoritativeExpandedHeight(
        DiagramNode node,
        IReadOnlyList<DiagramNodeProperty> properties,
        IReadOnlyList<DiagramNodeSection> sections,
        IReadOnlyList<string> collapsedSectionIds)
    {
        var stored = node.Presentation?.ExpandedHeight;
        var current = node.Presentation?.DisplayMode == DiagramNodeDisplayModes.Expanded ? Math.Max(node.Height, stored ?? node.Height) : stored ?? node.Height;
        var minimum = ProjectNodeMinimumHeight(properties, sections, DiagramNodeDisplayModes.Expanded, collapsedSectionIds);
        return GridCeilingDimension(Math.Max(double.IsFinite(current) && current > 0 ? current : minimum, minimum), 32);
    }

    private static double ProjectNodeMinimumHeight(
        IReadOnlyList<DiagramNodeProperty> properties,
        IReadOnlyList<DiagramNodeSection> sections,
        string displayMode,
        IReadOnlyList<string> collapsedSectionIds)
    {
        if (displayMode == DiagramNodeDisplayModes.Collapsed) return NodeHeaderHeight;
        var compact = displayMode == DiagramNodeDisplayModes.Compact;
        var visible = properties.Where(property => property.Mode != DiagramPropertyModes.Hidden).ToArray();
        var collapsed = collapsedSectionIds.ToHashSet(StringComparer.Ordinal);
        var bySection = visible.Where(property => property.SectionId is not null).GroupBy(property => property.SectionId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var children = sections.OrderBy(section => section.Order).ThenBy(section => section.Id, StringComparer.Ordinal)
            .GroupBy(section => section.ParentSectionId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var cursor = NodeHeaderHeight;
        void AddProperties(IEnumerable<DiagramNodeProperty> source)
        {
            foreach (var property in source)
                cursor += ProjectPropertyHeight(property, compact) + NodePropertyGap;
        }
        void AddSection(DiagramNodeSection section)
        {
            cursor += NodeSectionHeaderHeight + NodePropertyGap;
            if (collapsed.Contains(section.Id)) return;
            if (bySection.TryGetValue(section.Id, out var sectionProperties)) AddProperties(sectionProperties);
            if (children.TryGetValue(section.Id, out var childSections)) foreach (var child in childSections) AddSection(child);
        }

        AddProperties(visible.Where(property => property.SectionId is null));
        if (children.TryGetValue(string.Empty, out var rootSections)) foreach (var section in rootSections) AddSection(section);
        return visible.Length > 0 || sections.Count > 0
            ? Math.Max(NodeHeaderHeight, cursor + NodeBodyBottomPadding - NodePropertyGap)
            : NodeHeaderHeight;
    }

    private static double ProjectPropertyHeight(DiagramNodeProperty property, bool compact)
    {
        if (compact) return NodeCompactPropertyHeight;
        var kind = property.Editor?.Kind is { } explicitKind and not DiagramPropertyEditorKinds.Auto
            ? explicitKind
            : property.Type switch
            {
                DiagramPropertyTypes.Boolean => DiagramPropertyEditorKinds.Toggle,
                DiagramPropertyTypes.Integer or DiagramPropertyTypes.Decimal => DiagramPropertyEditorKinds.Number,
                DiagramPropertyTypes.Date => DiagramPropertyEditorKinds.Date,
                DiagramPropertyTypes.DateTime => DiagramPropertyEditorKinds.DateTime,
                DiagramPropertyTypes.Enum => DiagramPropertyEditorKinds.Select,
                DiagramPropertyTypes.Json => DiagramPropertyEditorKinds.Json,
                _ => DiagramPropertyEditorKinds.Text
            };
        return kind is DiagramPropertyEditorKinds.Multiline or DiagramPropertyEditorKinds.Json ? 48 : kind == DiagramPropertyEditorKinds.Range ? 28 : NodePropertyHeight;
    }
    private static readonly HashSet<string> KnownPropertyTypes = [DiagramPropertyTypes.String, DiagramPropertyTypes.Boolean, DiagramPropertyTypes.Integer, DiagramPropertyTypes.Decimal, DiagramPropertyTypes.Date, DiagramPropertyTypes.DateTime, DiagramPropertyTypes.Enum, DiagramPropertyTypes.Json];
    private static bool IsCustomPropertyType(string type) => !KnownPropertyTypes.Contains(type);
    private static string PropertyValueText(JsonElement? value) => value is null ? string.Empty : value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString() ?? string.Empty : value.Value.GetRawText();
    private int PropertyOrder(DiagramNode node, string propertyId) => _document.Ports.Where(port => port.NodeId == node.Id && port.PropertyId == propertyId).Select(port => port.Order).DefaultIfEmpty(int.MaxValue).Min();
    private string PropertyConnection(DiagramNode node, string propertyId)
    {
        var directions = _document.Ports.Where(port => port.NodeId == node.Id && port.PropertyId == propertyId).Select(port => port.Direction).ToHashSet(StringComparer.Ordinal);
        return directions.Contains("both") || directions.Contains("source") && directions.Contains("target") ? "both" : directions.Contains("source") ? "source" : directions.Contains("target") ? "target" : "none";
    }
    private static string PortSideFromAnchor(object? anchor) => anchor is string value && value is "left" or "right" or "top" or "bottom" ? value : "right";
    private bool EdgeRolesRemainCompatible(DiagramEdge edge, IEnumerable<DiagramPort> desiredPorts)
    {
        var ports = desiredPorts.ToDictionary(port => port.Id, StringComparer.Ordinal);
        return (!ports.TryGetValue(edge.SourcePortId, out var source) || PortAllows(source.Direction, "source"))
            && (!ports.TryGetValue(edge.TargetPortId, out var target) || PortAllows(target.Direction, "target"));
    }
    private static bool PortAllows(string direction, string role) => direction == "both" || direction == role;
    private static string? UpdatedPropertyPortLabel(DiagramPort port, DiagramNodeProperty? original, DiagramNodeProperty desired)
    {
        if (original is null) return desired.Label;
        var wasDerived = string.Equals(port.Label, original.Label, StringComparison.Ordinal)
            || string.Equals(port.Label, original.Name, StringComparison.Ordinal);
        return wasDerived ? desired.Label : port.Label;
    }
    private string NextPropertiesEditorId(string prefix)
    {
        var ids = _document.Nodes.Select(node => node.Id).Concat(_document.Ports.Select(port => port.Id)).Concat(_propertyEditorDrafts.Select(property => property.Id)).Concat(_portEditorDrafts.Select(port => port.Id)).ToHashSet(StringComparer.Ordinal);
        string id; do { id = prefix == "property" ? $"property-{Guid.NewGuid():N}" : $"{_propertiesNodeId}-{prefix}-{Guid.NewGuid():N}"; } while (!ids.Add(id));
        return id;
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
        var templateWidth = GridFloorDimension(template.Width, template.IsGroup ? 80 : 48);
        var templateHeight = GridCeilingDimension(template.Height, template.IsGroup ? 64 : 32);

        if (template.IsGroup)
        {
            var group = new DiagramGroup(
                NextId("group"),
                Snap(point.X - templateWidth / 2),
                Snap(point.Y - templateHeight / 2),
                templateWidth,
                templateHeight,
                template.Label,
                Icon: template.Icon);
            await SubmitOperationsAsync([DiagramOperations.Upsert(group)], $"Added {template.Label}");
        }
        else
        {
            var id = NextId("node");
            var x = Snap(point.X - templateWidth / 2);
            var y = Snap(point.Y - templateHeight / 2);
            var groupId = DiagramGroupMembership.ResolveGroupId(_document.Groups, x, y, templateWidth, templateHeight);
            if (template.RegisteredTypeId is not null)
            {
                var created = NodeFactory.Create(new(id, template.RegisteredTypeId, x, y, template.RegisteredTypeVersion, GroupId: groupId));
                var createdNode = FitNodeToGrid(created.Node);
                var styledPorts = created.Ports.Select(port => port with
                {
                    Endpoint = new DiagramEndpoint("dot", 10, template.Outline, "#ffffff", 2)
                });
                await SubmitOperationsAsync(
                    [DiagramOperations.Upsert(createdNode), .. styledPorts.Select(DiagramOperations.Upsert)],
                    $"Added {template.Label}");
            }
            else
            {
                var persistedProperties = template.PersistedDefinition?.Properties
                    .OrderBy(property => property.Order)
                    .ToArray();
                var properties = persistedProperties is null
                    ? template.Properties.Select(property => property.Property with { Value = property.Value?.Clone() }).ToArray()
                    : persistedProperties.Select(property => new DiagramNodeProperty(
                        property.Id,
                        property.Name,
                        property.Type,
                        property.DefaultValue?.Clone(),
                        property.Mode,
                        property.Label,
                        Connectable: property.Connectable,
                        Options: property.Options.ToArray(),
                        Metadata: property.Metadata.Count == 0 ? null : JsonSerializer.SerializeToElement(property.Metadata, JsonOptions),
                        SectionId: ReadPropertySectionId(property.Metadata),
                        Editor: ReadPropertyEditor(property.Metadata))).ToArray();
                var node = FitNodeToGrid(new DiagramNode(id, x, y, templateWidth, templateHeight, template.Label, GroupId: groupId, Icon: template.Icon,
                    Style: new DiagramNodeStyle(template.Background, template.Outline, template.TextColor, template.TextAlign), Properties: properties,
                    Sections: template.Sections.Count == 0 ? null : template.Sections.Select(section => section with { }).ToArray(),
                    Presentation: ClonePresentation(template.Presentation)));
                var persistedPorts = template.PersistedDefinition?.Ports.OrderBy(port => port.Order).ToArray();
                var ports = persistedPorts is null
                    ? template.Ports.Select((port, index) => new DiagramPort(
                        $"{id}-{port.Id}-{index + 1}", id, port.Direction, Anchor: port.Side,
                        Endpoint: new DiagramEndpoint("dot", 11, template.Outline, "#ffffff", 2))).ToArray()
                    : persistedPorts.Select((port, index) => new DiagramPort(
                        $"{id}-{port.Id}-{index + 1}",
                        id,
                        port.Direction,
                        Anchor: port.Anchor ?? port.Side,
                        Endpoint: port.Endpoint is null
                            ? new DiagramEndpoint("dot", 11, template.Outline, "#ffffff", 2)
                            : new DiagramEndpoint(port.Endpoint.Type, port.Endpoint.Size, port.Endpoint.Stroke, port.Endpoint.Fill, port.Endpoint.StrokeWidth),
                        PropertyId: port.PropertyId,
                        Label: port.Label,
                        Order: port.Order)
                    {
                        ExtensionData = CloneMetadata(port.Metadata).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    }).ToArray();
                var operations = new List<GhostagramOperation> { DiagramOperations.Upsert(node) };
                operations.AddRange(ports.Select(DiagramOperations.Upsert));
                var boundPropertyIds = persistedPorts?.Where(port => port.PropertyId is not null)
                    .Select(port => port.PropertyId!)
                    .ToHashSet(StringComparer.Ordinal) ?? [];
                operations.AddRange(properties.Where(property => property.Connectable && !boundPropertyIds.Contains(property.Id)).SelectMany((property, index) =>
                {
                    var direction = persistedProperties?.SingleOrDefault(templateProperty => templateProperty.Id == property.Id)?.Direction
                        ?? template.Properties.Single(templateProperty => templateProperty.Id == property.Id).Direction;
                    return PropertyPorts(id, property, direction, index, template.Outline);
                }).Select(DiagramOperations.Upsert));
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
    private void SetDraftOrganized(bool value)
    {
        _draft.IsOrganized = value;
        if (value && _draftSections.Count == 0) AddDraftSection();
    }
    private void AddDraftSection()
    {
        _draft.IsOrganized = true;
        _draftSections.Add(new DraftSection($"section-{++_propertySequence}") { Title = _draftSections.Count == 0 ? "Details" : "Section" });
    }
    private void RemoveDraftSection(string id)
    {
        _draftSections.RemoveAll(section => section.Id == id);
        foreach (var property in _draftProperties.Where(property => property.SectionId == id)) property.SectionId = null;
        foreach (var section in _draftSections.Where(section => section.ParentSectionId == id)) section.ParentSectionId = null;
    }
    private int PreviewSectionDepth(string id)
    {
        var depth = 0;
        var section = _draftSections.SingleOrDefault(item => item.Id == id);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (section?.ParentSectionId is { } parent && seen.Add(parent))
        {
            depth++;
            section = _draftSections.SingleOrDefault(item => item.Id == parent);
            if (depth == 4) break;
        }
        return depth;
    }

    private async Task AddCustomTemplate()
    {
        _designerError = null;
        var label = string.IsNullOrWhiteSpace(_draft.Label) ? "Custom node" : _draft.Label.Trim();
        var outline = NormalizeColor(_draft.Outline, "#d24686");
        var background = NormalizeColor(_draft.Background, "#ffffff");
        var textColor = NormalizeColor(_draft.TextColor, "#172033");
        var textAlign = NormalizeTextAlign(_draft.TextAlign);
        var category = _paletteCategories.Any(item => item.Name == _draft.Category) ? _draft.Category : "Custom";
        var ports = _draftPorts.Select((port, index) => new PortTemplate($"port-{index + 1}", port.Side, port.Direction)).ToArray();
        var properties = _draftProperties.Select(CreatePropertyTemplate).ToArray();
        var sections = _draft.IsOrganized ? _draftSections.Select((section, index) => new DiagramNodeSection(section.Id, section.Title.Trim(), section.ParentSectionId, index, section.Collapsible)).ToArray() : [];
        var sectionError = ValidateSections(sections, properties.Select(property => property.Property).ToArray());
        if (sectionError is not null) { _designerError = sectionError; return; }
        foreach (var property in _draftProperties)
        {
            var editorError = ValidateEditor(property);
            if (editorError is not null) { _designerError = $"{property.Name}: {editorError}"; return; }
        }
        var minimumPropertyHeight = ProjectNodeMinimumHeight(
            properties.Select(property => property.Property).ToArray(),
            sections,
            DiagramNodeDisplayModes.Expanded,
            []);
        var width = GridFloorDimension(Math.Clamp(_draft.Width, 96, 360), 96);
        var height = GridCeilingDimension(Math.Clamp(Math.Max(_draft.Height, minimumPropertyHeight), 48, 420), 48, 416);
        var presentation = sections.Length == 0 ? null : new DiagramNodePresentation(DiagramNodeDisplayModes.Expanded, height);
        _templates.Add(new NodeTemplate($"custom-{++_templateSequence}", category, label, "Custom component",
            width, height, outline, background, textColor, textAlign,
            ports, false, NormalizeIcon(_draft.Icon) ?? "mdi:shape-outline", true, properties, Sections: sections, Presentation: presentation));
        _draft = new NodeDraft();
        _draftPorts.Clear();
        _draftProperties.Clear();
        _draftSections.Clear();
        _isDesignerOpen = false;
        _paletteOpen = true;
        _expandedCategories.Add(category);
        _activity = $"Added {label} to {category}";
        await PersistPaletteCatalogAsync();
    }

    private async Task OnGhostagramEvent(GhostagramEvent envelope)
    {
        if (_diagram is null || envelope.Origin != "browser" || string.IsNullOrWhiteSpace(envelope.Type)) return;
        await _eventGate.WaitAsync();
        try
        {
            if (envelope.DocumentId is not null && !string.Equals(envelope.DocumentId, DocumentId, StringComparison.Ordinal)) return;
            if (envelope.RenderRevision != _revision) return;
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
            case "node.presentationRequested": await CommitNodePresentationAsync(envelope.Payload); break;
            case "nodes.move.commit": await CommitNodePositionsAsync(envelope.Payload, "nodes", "Moved nodes"); break;
            case "selection.move.commit": await CommitNodeAndGroupPositionsAsync(envelope.Payload, "Moved selection"); break;
            case "group.move.commit": await CommitGroupMoveAsync(envelope.Payload); break;
            case "group.resize.commit": await CommitGroupAsync(envelope.Payload, group => group with { Width = Number(envelope.Payload, "width"), Height = Number(envelope.Payload, "height") }, "Resized group"); break;
            case "group.visibilityRequested": await SetGroupVisibilityAsync(envelope.Payload); break;
            case "group.label.commit": await CommitGroupAsync(envelope.Payload, group => group with { Label = NullableLabel(envelope.Payload, "label") }, "Updated group label"); break;
            case "edge.createRequested": await CreateEdgeAsync(envelope.Payload); break;
            case "edge.reconnectRequested": await ReconnectEdgeAsync(envelope.Payload); break;
            case "edge.label.commit": await CommitEdgeAsync(envelope.Payload, edge => edge with { Label = EdgeLabel(envelope.Payload, "label") }, "Updated edge label"); break;
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

    private async Task CommitNodePresentationAsync(JsonElement payload)
    {
        var node = _document.Nodes.SingleOrDefault(item => item.Id == String(payload, "nodeId"));
        if (node is null || !payload.TryGetProperty("presentation", out var rawPresentation)) return;
        DiagramNodePresentation? presentation;
        try { presentation = JsonSerializer.Deserialize<DiagramNodePresentation>(rawPresentation.GetRawText(), JsonOptions); }
        catch (JsonException) { return; }
        if (presentation is null || presentation.ExpandedHeight is not { } proposedExpandedHeight || !double.IsFinite(proposedExpandedHeight) || proposedExpandedHeight <= 0) return;
        var proposedCollapsed = presentation.CollapsedSectionIds ?? [];
        if (proposedCollapsed.Any(string.IsNullOrWhiteSpace) || proposedCollapsed.Distinct(StringComparer.Ordinal).Count() != proposedCollapsed.Count) return;
        var collapsed = proposedCollapsed.ToHashSet(StringComparer.Ordinal);
        if (TryNullableString(payload, "sectionId", out var sectionId) && sectionId is not null)
        {
            var section = (node.Sections ?? []).SingleOrDefault(item => item.Id == sectionId);
            if (section is null || !section.Collapsible) return;
            var requestedCollapsed = Boolean(payload, "collapsed");
            if (collapsed.Contains(sectionId) != requestedCollapsed) return;
        }
        var validSections = (node.Sections ?? []).Where(section => section.Collapsible).Select(section => section.Id).ToHashSet(StringComparer.Ordinal);
        if (collapsed.Any(id => !validSections.Contains(id))) return;
        if (presentation.DisplayMode is not (DiagramNodeDisplayModes.Expanded or DiagramNodeDisplayModes.Compact or DiagramNodeDisplayModes.Collapsed)) return;
        var geometry = ResolvePresentationGeometry(
            node,
            node.Properties,
            node.Sections ?? [],
            presentation.DisplayMode,
            collapsed.Order(StringComparer.Ordinal).ToArray(),
            requestedExpandedHeight: null,
            allowRequestedExpandedHeight: false);
        if (geometry.Error is not null || geometry.Value is null) return;
        await SubmitOperationsAsync([DiagramOperations.Upsert(node with { Height = geometry.Height, Presentation = geometry.Value })], "Updated node presentation");
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
            Overlays = WithEdgeMarkers(edge, _edgeStartMarker, _edgeEndMarker),
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

    private Task OnEdgeStartMarkerChangedAsync(string value)
    {
        _edgeStartMarker = NormalizeEdgeMarker(value, "none");
        return ApplySelectedEdgeMarkersAsync();
    }

    private Task OnEdgeEndMarkerChangedAsync(string value)
    {
        _edgeEndMarker = NormalizeEdgeMarker(value, "arrow");
        return ApplySelectedEdgeMarkersAsync();
    }

    private async Task ApplySelectedEdgeMarkersAsync()
    {
        var edges = SelectedEdges();
        if (edges.Length == 0) return;
        var operations = edges
            .Select(edge => DiagramOperations.Upsert(edge with { Overlays = WithEdgeMarkers(edge, _edgeStartMarker, _edgeEndMarker) }))
            .ToArray();
        await SubmitOperationsAsync(operations, $"Updated markers on {SelectedEdgeDescription(edges.Length)}");
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
                DocumentId, _actorId, CommandId("layout"), _revision, GhostLayeredLayoutStrategy.AlgorithmName, 42, false,
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
            var command = new DiagramCommand(DocumentId, _actorId, CommandId("studio"), baseRevision, operations.ToImmutableArray());
            var result = await Commands.SubmitAsync(command, CancellationToken.None);
            if (!result.Accepted && result.Code == "REVISION_CONFLICT" && result.Snapshot is not null && IsAdditiveBatch(operations, previous))
            {
                previous = Deserialize(result.Snapshot);
                _document = previous;
                _revision = result.Snapshot.Revision;
                baseRevision = _revision;
                if (_diagram is not null) await _diagram.ReplaceAsync(_document, _revision);
                command = new DiagramCommand(DocumentId, _actorId, CommandId("studio-retry"), baseRevision, operations.ToImmutableArray());
                result = await Commands.SubmitAsync(command, CancellationToken.None);
            }
            if (!result.Accepted || result.Snapshot is null)
            {
                await AcceptRejectedResultAsync(result, result.Message);
                return false;
            }

            _document = Deserialize(result.Snapshot);
            _revision = result.Revision;
            _documentDurable = true;
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
            _documentDurable = false;
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
            _documentDurable = true;
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
                _documentDurable = true;
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
            var result = await Commands.SubmitAsync(new DiagramCommand(DocumentId, _actorId, CommandId("fit"), baseRevision, operations.ToImmutableArray()), CancellationToken.None);
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
        var operations = OperationsToTransform(_document, CreateStarterDocument(DocumentId));
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
        Overlays: WithEdgeMarkers([], _edgeStartMarker, _edgeEndMarker),
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
        var overlays = EffectiveOverlays(edge);
        _edgeStartMarker = MarkerAt(overlays, 0);
        _edgeEndMarker = MarkerAt(overlays, 1);
        _edgeAnimated = AnimationEnabled(edge.Animation);
    }

    private IReadOnlyList<DiagramOverlay> EffectiveOverlays(DiagramEdge edge) =>
        edge.Overlays ?? _document.EdgeTypes.SingleOrDefault(type => type.Id == edge.Type)?.Overlays ?? [];

    private IReadOnlyList<DiagramOverlay> WithEdgeMarkers(DiagramEdge edge, string startMarker, string endMarker) =>
        WithEdgeMarkers(EffectiveOverlays(edge), startMarker, endMarker);

    private static IReadOnlyList<DiagramOverlay> WithEdgeMarkers(IEnumerable<DiagramOverlay> overlays, string startMarker, string endMarker)
    {
        var result = overlays.Where(overlay => !EdgeMarkerTypes.Contains(overlay.Type)).ToList();
        startMarker = NormalizeEdgeMarker(startMarker, "none");
        endMarker = NormalizeEdgeMarker(endMarker, "none");
        if (startMarker != "none") result.Add(new DiagramOverlay(startMarker, Location: 0));
        if (endMarker != "none") result.Add(new DiagramOverlay(endMarker, Location: 1));
        return result;
    }

    private static string MarkerAt(IEnumerable<DiagramOverlay> overlays, double location) =>
        overlays.FirstOrDefault(overlay => EdgeMarkerTypes.Contains(overlay.Type) && (overlay.Location ?? 1) == location)?.Type ?? "none";

    private static string NormalizeEdgeMarker(string? marker, string fallback) =>
        marker == "none" || (marker is not null && EdgeMarkerTypes.Contains(marker)) ? marker : fallback;

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
        return new PropertyTemplate(draft.Id, name, type, ParsePropertyValue(draft.DefaultValue, type), draft.Mode, name, draft.Connectable, options, draft.Direction, draft.SectionId, CreateEditor(draft));
    }

    private static DiagramPropertyEditor? CreateEditor(DraftProperty draft)
    {
        if (draft.EditorKind == DiagramPropertyEditorKinds.Auto && string.IsNullOrWhiteSpace(draft.Placeholder) && string.IsNullOrWhiteSpace(draft.Minimum) && string.IsNullOrWhiteSpace(draft.Maximum) && string.IsNullOrWhiteSpace(draft.Step)) return null;
        return new DiagramPropertyEditor(draft.EditorKind, string.IsNullOrWhiteSpace(draft.Placeholder) ? null : draft.Placeholder.Trim(), ParseOptionalDecimal(draft.Minimum), ParseOptionalDecimal(draft.Maximum), ParseOptionalDecimal(draft.Step));
    }
    private static string? ValidateEditor(DraftProperty draft) => ValidateEditor(new PropertyEditorDraft(draft.Id)
    {
        Type = draft.Type,
        EditorKind = draft.EditorKind,
        Placeholder = draft.Placeholder,
        Minimum = draft.Minimum,
        Maximum = draft.Maximum,
        Step = draft.Step
    });

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
    private static DiagramDocument EmptyDocument(string documentId) => new(documentId, [], [], [], [], new DiagramViewport(), [], []);

    private static DiagramDocument CreateStarterDocument(string documentId = DefaultDocumentId) => new(
        documentId,
        [
            new DiagramNode("node-1", 128, 160, 160, 64, "Discover", GroupId: "group-1", Icon: "mdi:magnify", Style: new DiagramNodeStyle("#ffffff", "#4177de", "#172033")),
            new DiagramNode("node-2", 416, 160, 160, 64, "Shape the flow", GroupId: "group-1", Icon: "mdi:vector-polyline", Style: new DiagramNodeStyle("#ffffff", "#7455dd", "#172033"))
        ],
        [
            new DiagramPort("node-1-output", "node-1", "source", Anchor: "right", Endpoint: new DiagramEndpoint("dot", 11, "#4177de", "#ffffff", 2)),
            new DiagramPort("node-2-input", "node-2", "target", Anchor: "left", Endpoint: new DiagramEndpoint("dot", 11, "#7455dd", "#ffffff", 2)),
            new DiagramPort("node-2-output", "node-2", "source", Anchor: "right", Endpoint: new DiagramEndpoint("dot", 11, "#7455dd", "#ffffff", 2))
        ],
        [new DiagramEdge("edge-1", "node-1-output", "node-2-input", "explore", "flowchart", Overlays: [new DiagramOverlay("arrow")], ConnectorOptions: new DiagramFlowchartOptions(32, 0), Style: new DiagramEdgeStyle("#7455dd", 2.5))],
        [new DiagramGroup("group-1", 96, 96, 544, 192, "Design loop", Icon: "mdi:layers-outline")],
        new DiagramViewport(0, 0, 1),
        Selection: []);

    private static List<NodeTemplate> CreateBuiltInTemplates() =>
    [
        new("start", "Workflow", "Start", "Entry point for a workflow", 144, 64, "#16875b", "#ecfdf5", "#115e45", "center", [new PortTemplate("output", "right", "source")], false, "mdi:play-circle-outline"),
        new("task", "Workflow", "Task", "Work step with input and output", 160, 64, "#4177de", "#ffffff", "#172033", "center", [new PortTemplate("input", "left", "target"), new PortTemplate("output", "right", "source")], false, "mdi:checkbox-marked-circle-outline"),
        new("decision", "Workflow", "Decision", "Branch a workflow into paths", 160, 80, "#d97706", "#fffaf0", "#7c2d12", "center", [new PortTemplate("input", "left", "target"), new PortTemplate("yes", "right", "source"), new PortTemplate("no", "bottom", "source")], false, "mdi:source-branch"),
        new("note", "Content", "Note", "Context without connection points", 176, 80, "#0f9d79", "#f0fdfa", "#134e4a", "left", [], false, "mdi:note-text-outline"),
        new("group", "Containers", "Group", "Resizable visual boundary", 320, 208, "#7455dd", "#ffffff", "#172033", "left", [], true, "mdi:layers-outline")
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
            foreach (var descriptor in set.NodeTypes
                .GroupBy(type => type.TypeId, StringComparer.Ordinal)
                .Select(versions => versions.OrderByDescending(type => type.Version).First()))
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
                    PropertyDirection(descriptor, property.Id),
                    property.SectionId,
                    property.Editor)).ToArray();
                var ports = descriptor.Ports
                    .Where(port => port.PropertyId is null)
                    .Select(port => new PortTemplate(port.Id, port.Anchor ?? PortSide(port.Direction), port.Direction))
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
                    Sections: descriptor.Sections.Select(section => new DiagramNodeSection(section.Id, section.Title, section.ParentSectionId, section.Order, section.Collapsible)).ToArray(),
                    Presentation: ClonePresentation(descriptor.Presentation),
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
    private static string SlugifyDocumentId(string name)
    {
        var characters = name.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray();
        var slug = string.Join('-', new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 96) slug = slug[..96].TrimEnd('-');
        return string.IsNullOrWhiteSpace(slug) ? $"diagram-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}" : slug;
    }

    private static string SlugifyPaletteCatalogId(string name)
    {
        var characters = name.Trim().ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray();
        var slug = string.Join('-', new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 96) slug = slug[..96].TrimEnd('-');
        return string.IsNullOrWhiteSpace(slug) ? $"palette-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}" : slug;
    }
    private static string NextId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
    private static double Snap(double value) => Math.Round(value / GridSize) * GridSize;
    private static double GridFloorDimension(double value, double minimum) => Math.Max(minimum, Math.Floor(value / GridSize) * GridSize);
    private static double GridCeilingDimension(double value, double minimum, double maximum = double.PositiveInfinity) =>
        Math.Min(maximum, Math.Max(minimum, Math.Ceiling(value / GridSize) * GridSize));
    private static DiagramNode FitNodeToGrid(DiagramNode node)
    {
        var width = GridFloorDimension(node.Width, 48);
        var height = GridCeilingDimension(node.Height, 32);
        var presentation = node.Presentation is null
            ? null
            : node.Presentation with { ExpandedHeight = GridCeilingDimension(node.Presentation.ExpandedHeight ?? height, 32) };
        return node with { Width = width, Height = height, Presentation = presentation };
    }
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
    private static string EdgeLabel(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
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
        _disposed = true;
        CircuitState.ConnectionChanged -= OnCircuitConnectionChangedAsync;
        Interlocked.Exchange(ref _documentSubscription, null)?.Dispose();
        _eventGate.Dispose();
        _commandGate.Dispose();
        _paletteGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class NodeDraft
    {
        public string Label { get; set; } = "Human review";
        public double Width { get; set; } = 176;
        public double Height { get; set; } = 80;
        public string Outline { get; set; } = "#d24686";
        public string Background { get; set; } = "#ffffff";
        public string TextColor { get; set; } = "#172033";
        public string TextAlign { get; set; } = "center";
        public string Category { get; set; } = "Custom";
        public string Direction { get; set; } = "both";
        public string Icon { get; set; } = "mdi:shape-outline";
        public bool IsOrganized { get; set; }
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
        public string? SectionId { get; set; }
        public string EditorKind { get; set; } = DiagramPropertyEditorKinds.Auto;
        public string Placeholder { get; set; } = string.Empty;
        public string Minimum { get; set; } = string.Empty;
        public string Maximum { get; set; } = string.Empty;
        public string Step { get; set; } = string.Empty;
    }
    private sealed class DraftSection(string id)
    {
        public string Id { get; } = id;
        public string Title { get; set; } = "Section";
        public string? ParentSectionId { get; set; }
        public bool Collapsible { get; set; } = true;
        public bool Collapsed { get; set; }
    }
    private sealed class PropertyEditorDraft(string id, DiagramNodeProperty? original = null)
    {
        public string Id { get; } = id;
        public DiagramNodeProperty? Original { get; } = original;
        public string Name { get; set; } = "Property";
        public string Type { get; set; } = DiagramPropertyTypes.String;
        public string Mode { get; set; } = DiagramPropertyModes.Display;
        public string Value { get; set; } = string.Empty;
        public string Options { get; set; } = string.Empty;
        public string Connection { get; set; } = "none";
        public string? SectionId { get; set; }
        public string EditorKind { get; set; } = DiagramPropertyEditorKinds.Auto;
        public string Placeholder { get; set; } = string.Empty;
        public string Minimum { get; set; } = string.Empty;
        public string Maximum { get; set; } = string.Empty;
        public string Step { get; set; } = string.Empty;
    }
    private sealed class SectionEditorDraft(string id, DiagramNodeSection? original = null)
    {
        public string Id { get; } = id;
        public DiagramNodeSection? Original { get; } = original;
        public string Title { get; set; } = "Section";
        public string? ParentSectionId { get; set; }
        public bool Collapsible { get; set; } = true;
        public bool Collapsed { get; set; }
    }
    private sealed class PresentationDraft
    {
        public bool Enabled { get; set; }
        public string DisplayMode { get; set; } = DiagramNodeDisplayModes.Expanded;
        public double ExpandedHeight { get; set; } = 112;
    }
    private sealed class PortEditorDraft(string id, DiagramPort? original = null)
    {
        public string Id { get; } = id;
        public string? OriginalLabel { get; } = original?.Label;
        public string OriginalSide { get; } = original is null ? "right" : PortSideFromAnchor(original.Anchor);
        public string Label { get; set; } = "Connection";
        public string Side { get; set; } = "right";
        public string Direction { get; set; } = "both";
    }
    private sealed record PropertiesEditPlan(IReadOnlyList<GhostagramOperation> Operations, IReadOnlyList<string> RemovedEdgeIds, string? Error)
    {
        public static PropertiesEditPlan Invalid(string error) => new([], [], error);
    }
    private sealed record PropertyValidation(JsonElement? Value, IReadOnlyList<string> Options, string? Error)
    {
        public static PropertyValidation Invalid(string error) => new(null, [], error);
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
        IReadOnlyList<DiagramNodeSection>? Sections = null,
        DiagramNodePresentation? Presentation = null,
        string? RegisteredTypeId = null,
        int? RegisteredTypeVersion = null,
        PaletteNodeDefinitionSnapshot? PersistedDefinition = null)
    {
        public IReadOnlyList<PropertyTemplate> Properties { get; init; } = Properties ?? [];
        public IReadOnlyList<DiagramNodeSection> Sections { get; init; } = Sections ?? [];
    }
    private sealed record PaletteCategory(string Name, bool IsSystem, PaletteGroupSnapshot? PersistedGroup = null);
    private sealed record PortTemplate(string Id, string Side, string Direction);
    private sealed record PropertyTemplate(string Id, string Name, string Type, JsonElement? Value, string Mode, string? Label, bool Connectable, IReadOnlyList<string> Options, string Direction, string? SectionId = null, DiagramPropertyEditor? Editor = null)
    {
        public DiagramNodeProperty Property => new(Id, Name, Type: Type, Value: Value, Mode: Mode, Label: Label, Connectable: Connectable, Options: Options, SectionId: SectionId, Editor: Editor);
    }
    private sealed record NodePosition(string Id, double X, double Y, string? GroupId, bool HasGroupId);
    private sealed record GroupPosition(string Id, double X, double Y);
    private sealed record EdgeMarkerChoice(string Id, string Label);
}
