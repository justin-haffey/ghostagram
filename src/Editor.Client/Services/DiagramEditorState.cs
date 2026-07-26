using System.Collections.Immutable;
using Diagrams.Core.Abstractions;
using Diagrams.Core.Commands;
using Diagrams.Core.Exports;
using Diagrams.Core.Models;
using Diagrams.Core.Routing;
using Diagrams.Core.Templates;
using Diagrams.Interop.JsPlumb;

namespace Editor.Client.Services;

public sealed partial class DiagramEditorState(
    IDocumentCatalogRepository repository,
    IDocumentSerializer serializer,
    IDiagramLayoutEngine layoutEngine,
    DiagramTemplateCatalog templates,
    DiagramStencilCatalog stencils,
    PortPresetCatalog portPresets,
    SvgExportRenderer svgRenderer,
    ICommandHistory history,
    IValidationEngine validationEngine,
    IDocumentSyncService syncService)
{
    private DiagramGraphStore? _store;
    private CancellationTokenSource? _autosaveCts;
    private bool _catalogInitialized;
    private LibraryItemDefinition? _clipboard;

    public event Action? Changed;

    public IReadOnlyList<DocumentSummary> Documents { get; private set; } = [];
    public IReadOnlyList<DocumentSnapshotSummary> Snapshots { get; private set; } = [];
    public IReadOnlyList<LibraryItemDefinition> LibraryItems { get; private set; } = [];
    public IReadOnlyList<ValidationIssue> ValidationIssues { get; private set; } = [];

    public ImmutableHashSet<string> SelectedNodeIds { get; private set; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);
    public ImmutableHashSet<string> SelectedEdgeIds { get; private set; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);
    public ImmutableHashSet<string> SelectedGroupIds { get; private set; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);

    public string? PrimarySelectionKind { get; private set; }
    public string? PrimarySelectionId { get; private set; }
    public string ActiveLayerId { get; private set; } = DiagramLayerCatalog.DefaultLayerId;
    public string SyncStatus { get; private set; } = "Local-first";
    public string StencilSearch { get; private set; } = string.Empty;
    public string LibrarySearch { get; private set; } = string.Empty;
    public bool ServerSyncEnabled { get; private set; }
    public bool ReviewMode { get; private set; }
    public bool PresentationMode { get; private set; }
    public long KnownServerRevision { get; private set; }

    public DiagramDocument Document => _store?.Snapshot ?? templates.Create(TemplateKind.Flowchart);
    public IReadOnlyList<TemplateDefinition> TemplateDefinitions => templates.GetBuiltIns();
    public IReadOnlyList<StencilDefinition> StencilDefinitions => stencils.GetAll();
    public IReadOnlyList<StencilDefinition> FilteredStencilDefinitions => string.IsNullOrWhiteSpace(StencilSearch)
        ? StencilDefinitions
        : StencilDefinitions.Where(stencil =>
            stencil.DisplayName.Contains(StencilSearch, StringComparison.OrdinalIgnoreCase)
            || stencil.Description.Contains(StencilSearch, StringComparison.OrdinalIgnoreCase)
            || stencil.Category.Contains(StencilSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
    public IReadOnlyList<LibraryItemDefinition> FilteredLibraryItems => string.IsNullOrWhiteSpace(LibrarySearch)
        ? LibraryItems
        : LibraryItems.Where(item =>
            item.Name.Contains(LibrarySearch, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(LibrarySearch, StringComparison.OrdinalIgnoreCase)).ToArray();
    public IReadOnlyList<PortPresetDefinition> PortPresets => portPresets.GetAll();
    public IReadOnlyList<LayoutDescriptor> Layouts => layoutEngine.GetSupportedLayouts();
    public IReadOnlyList<LayerDefinition> Layers => Document.Layers.OrderBy(layer => layer.Order).ToArray();
    public DiagramNode? SelectedNode => SelectedNodeIds.Count == 1 ? Document.FindNode(SelectedNodeIds.First()) : null;
    public DiagramEdge? SelectedEdge => SelectedEdgeIds.Count == 1 ? Document.FindEdge(SelectedEdgeIds.First()) : null;
    public DiagramGroup? SelectedGroup => SelectedGroupIds.Count == 1 ? Document.FindGroup(SelectedGroupIds.First()) : null;
    public bool CanEditSelectedEdgeWaypoints => SelectedEdge is not null && EdgeRouteResolver.SupportsManualWaypoints(SelectedEdge);
    public string? SelectedNodeId => SelectedNode?.Id;
    public string? SelectedEdgeId => SelectedEdge?.Id;
    public string? SelectedGroupId => SelectedGroup?.Id;
    public IReadOnlyList<DiagramNode> SelectedNodes => SelectedNodeIds.Select(id => Document.FindNode(id)).OfType<DiagramNode>().ToArray();
    public IReadOnlyList<DiagramGroup> SelectedGroups => SelectedGroupIds.Select(id => Document.FindGroup(id)).OfType<DiagramGroup>().ToArray();
    public IReadOnlyList<InspectorFieldDefinition> SelectedInspectorFields => SelectedNode is null
        ? []
        : stencils.GetInspectorFields(SelectedNode.StencilKey);
    public IReadOnlyList<CommentThread> RelevantComments => Document.CommentThreads
        .Where(thread => thread.Target.Id is null || thread.Target.Id == PrimarySelectionId)
        .OrderByDescending(thread => thread.Comments.LastOrDefault()?.CreatedUtc ?? DateTimeOffset.MinValue)
        .ToArray();
    public IReadOnlyList<DiagramAnnotation> RelevantAnnotations => Document.Annotations
        .Where(annotation => annotation.Target.Id is null || annotation.Target.Id == PrimarySelectionId)
        .OrderByDescending(annotation => annotation.CreatedUtc)
        .ToArray();
    public bool CanUndo => history.CanUndo;
    public bool CanRedo => history.CanRedo;
    public bool HasSelection => SelectedNodeIds.Count > 0 || SelectedEdgeIds.Count > 0 || SelectedGroupIds.Count > 0;
    public string SelectionTagText => string.Join(", ", ResolveSelectionTags());
    public string? SelectionLinkUri => SelectedNode?.LinkUri ?? SelectedGroup?.LinkUri ?? SelectedEdge?.LinkUri;

    public string ExportJson() => serializer.Serialize(Document);
    public string ExportSvg() => svgRenderer.Render(Document);

    public void SetStencilSearch(string query)
    {
        StencilSearch = query;
        NotifyChanged(save: false);
    }

    public void SetLibrarySearch(string query)
    {
        LibrarySearch = query;
        NotifyChanged(save: false);
    }

    public void ToggleReviewMode()
    {
        ReviewMode = !ReviewMode;
        NotifyChanged(save: false);
    }

    public void TogglePresentationMode()
    {
        PresentationMode = !PresentationMode;
        NotifyChanged(save: false);
    }

    public void SetServerSyncEnabled(bool enabled)
    {
        ServerSyncEnabled = enabled;
        SyncStatus = enabled ? $"Server sync enabled (rev {KnownServerRevision})" : "Local-first";
        NotifyChanged(save: false);
    }

    public void SetActiveLayer(string layerId)
    {
        ActiveLayerId = layerId;
        NotifyChanged(save: false);
    }

    private void LoadDocument(DiagramDocument document)
    {
        ReplaceStore(document, resetHistory: true);
        ActiveLayerId = document.Layers.FirstOrDefault(layer => layer.IsVisible && !layer.IsLocked)?.Id ?? DiagramLayerCatalog.DefaultLayerId;
        KnownServerRevision = document.Revision;
        ClearSelection();
        UpdateDerivedState();
    }

    private DiagramDocument Apply(IDiagramCommand command, bool save = true, bool trackHistory = true)
    {
        EnsureStore();
        var before = _store!.Snapshot;
        var after = _store.Apply(command);
        AfterMutation(before, after, save, trackHistory);
        return after;
    }

    private DiagramDocument Mutate(Func<DiagramDocument, DiagramDocument> mutate, bool save = true, bool trackHistory = true)
    {
        EnsureStore();
        var before = _store!.Snapshot;
        var updated = mutate(before);
        var after = ReferenceEquals(updated, before) ? before : updated.Touch();
        ReplaceStore(after, resetHistory: false);
        AfterMutation(before, after, save, trackHistory);
        return after;
    }

    private void AfterMutation(DiagramDocument before, DiagramDocument after, bool save, bool trackHistory)
    {
        if (!ReferenceEquals(before, after) && trackHistory)
        {
            history.Record(before, after);
        }

        PruneSelection();
        UpdateDerivedState();

        if (save)
        {
            ScheduleAutosave();
        }

        NotifyChanged(save: false);
    }

    private void ReplaceStore(DiagramDocument document, bool resetHistory)
    {
        _store = new DiagramGraphStore(document, stencils, layoutEngine);
        if (resetHistory)
        {
            history.Reset(document);
        }
    }

    private void EnsureStore()
    {
        if (_store is null)
        {
            LoadDocument(templates.Create(TemplateKind.Flowchart));
        }
    }

    private void UpdateDerivedState()
    {
        ValidationIssues = validationEngine.Validate(Document);
    }

    private void PruneSelection()
    {
        SelectedNodeIds = SelectedNodeIds.Where(id => Document.FindNode(id) is not null).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        SelectedEdgeIds = SelectedEdgeIds.Where(id => Document.FindEdge(id) is not null).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        SelectedGroupIds = SelectedGroupIds.Where(id => Document.FindGroup(id) is not null).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        if (PrimarySelectionKind is null || PrimarySelectionId is null)
        {
            return;
        }

        var stillExists = PrimarySelectionKind switch
        {
            "node" => SelectedNodeIds.Contains(PrimarySelectionId),
            "edge" => SelectedEdgeIds.Contains(PrimarySelectionId),
            "group" => SelectedGroupIds.Contains(PrimarySelectionId),
            _ => false
        };

        if (!stillExists)
        {
            PrimarySelectionKind = null;
            PrimarySelectionId = null;
        }
    }

    private void SetSelection(
        string? kind,
        string? id,
        ImmutableHashSet<string> nodeIds,
        ImmutableHashSet<string> groupIds,
        ImmutableHashSet<string> edgeIds)
    {
        SelectedNodeIds = nodeIds;
        SelectedGroupIds = groupIds;
        SelectedEdgeIds = edgeIds;
        PrimarySelectionKind = kind;
        PrimarySelectionId = id;
        NotifyChanged(save: false);
    }

    private void ClearSelection()
        => SetSelection(null, null, ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase), ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase), ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase));

    private void NotifyChanged(bool save = true)
    {
        if (save)
        {
            ScheduleAutosave();
        }

        Changed?.Invoke();
    }

    private void ScheduleAutosave()
    {
        _autosaveCts?.Cancel();
        _autosaveCts?.Dispose();
        _autosaveCts = new CancellationTokenSource();
        _ = DebouncedSaveAsync(_autosaveCts.Token);
    }

    private async Task DebouncedSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            await SaveAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private AnnotationTarget ResolvePrimaryTarget()
        => new(
            PrimarySelectionKind ?? "document",
            PrimarySelectionId,
            SelectedNode?.Bounds is not null
                ? new DiagramPoint(SelectedNode.Bounds.CenterX, SelectedNode.Bounds.Y - 24)
                : null);

    private DiagramBounds SnapBounds(DiagramBounds bounds)
    {
        var grid = Math.Max(1, Document.Canvas.GridSize);
        return bounds with
        {
            X = Math.Round(bounds.X / grid) * grid,
            Y = Math.Round(bounds.Y / grid) * grid
        };
    }

    private DiagramPoint SnapPoint(DiagramPoint point)
    {
        var grid = Math.Max(1, Document.Canvas.GridSize);
        return point with
        {
            X = Math.Round(point.X / grid) * grid,
            Y = Math.Round(point.Y / grid) * grid
        };
    }

    private static ImmutableHashSet<string> ParseTags(string tags)
        => tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

    private IEnumerable<string> ResolveSelectionTags()
    {
        if (SelectedNode is not null)
        {
            return SelectedNode.Tags.OrderBy(tag => tag);
        }

        if (SelectedGroup is not null)
        {
            return SelectedGroup.Tags.OrderBy(tag => tag);
        }

        if (SelectedEdge is not null)
        {
            return SelectedEdge.Tags.OrderBy(tag => tag);
        }

        return [];
    }

    private static ConnectorKind ResolveConnector(TemplateKind templateKind) => templateKind switch
    {
        TemplateKind.Flowchart or TemplateKind.DataFlow or TemplateKind.BpmnLite => ConnectorKind.Flowchart,
        TemplateKind.StateDependency or TemplateKind.MindMap => ConnectorKind.StateMachine,
        TemplateKind.OrgChart or TemplateKind.Erd or TemplateKind.UmlSequence => ConnectorKind.Straight,
        _ => ConnectorKind.Bezier
    };

    private static EdgeAnimationKind ResolveAnimation(TemplateKind templateKind) => templateKind switch
    {
        TemplateKind.Flowchart or TemplateKind.DataFlow or TemplateKind.NetworkTopology or TemplateKind.C4Context => EdgeAnimationKind.Flow,
        TemplateKind.StateDependency or TemplateKind.BpmnLite => EdgeAnimationKind.Pulse,
        _ => EdgeAnimationKind.None
    };

    private static EdgeMarkers ResolveMarkers(PortRole sourceRole, PortRole targetRole)
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
