using System.Text.Json;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Ghostagram.Blazor;

/// <summary>
/// Razor control that owns one Ghostagram browser instance. The supplied C# document remains authoritative.
/// Browser events are proposals delivered through <see cref="EventReceived"/>.
/// </summary>
public partial class GhostDiagram
{
    private ElementReference _host;
    private IJSObjectReference? _module;
    private DotNetObjectReference<GhostDiagram>? _self;
    private string? _instanceId;
    private bool _initialized;
    private long _renderedRevision;
    private string? _renderedDocumentId;
    private DiagramDocument? _renderedDocument;
    private long? _incrementallyAppliedRevision;
    private bool _pendingParameterSync;

    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private ILogger<GhostDiagram> Logger { get; set; } = default!;
    [Parameter] public DiagramDocument? Document { get; set; }
    [Parameter] public string? DocumentId { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public long Revision { get; set; }
    [Parameter] public GhostDiagramOptions Options { get; set; } = new();
    [Parameter] public EventCallback<GhostagramEvent> EventReceived { get; set; }
    [Parameter] public string AriaLabel { get; set; } = "Ghostagram diagram";
    private DiagramCompositionContext? _composition;
    private DiagramDocument CurrentDocument => Document ?? _composition?.Build() ?? throw new InvalidOperationException("GhostDiagram requires a Document or declarative DocumentId with ChildContent.");

    protected string CssClass => $"ghostagram-diagram {Options.CssClass}".Trim();
    protected string HostStyle => $"width:100%;height:{Options.Height};min-height:160px;";

    protected override void OnInitialized()
    {
        if (Document is not null && (DocumentId is not null || ChildContent is not null))
            throw new InvalidOperationException("GhostDiagram supports either controlled Document mode or declarative DocumentId + ChildContent mode, not both.");
        if (Document is not null) return;
        if (string.IsNullOrWhiteSpace(DocumentId) || ChildContent is null)
            throw new InvalidOperationException("Declarative GhostDiagram mode requires both DocumentId and ChildContent.");
        _composition = new DiagramCompositionContext(DocumentId);
        _composition.InitializeRoot();
    }

    protected override void OnParametersSet()
    {
        if (!_initialized || Document is null || ReferenceEquals(Document, _renderedDocument)) return;

        // The host normally advances the browser through ApplyAsync and publishes the resulting
        // authoritative document on the following render. Adopt that matching parameter without
        // replacing the canvas a second time.
        if (_incrementallyAppliedRevision == Revision
            && string.Equals(_renderedDocumentId, Document.DocumentId, StringComparison.Ordinal))
        {
            _renderedDocument = Document;
            _incrementallyAppliedRevision = null;
            return;
        }

        // Async parent initialization can finish after the first interactive render. Schedule a
        // full sync even when the late document has the same ID/revision as the empty first value.
        _pendingParameterSync = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_initialized)
        {
            _module = await Js.InvokeAsync<IJSObjectReference>("import", Options.ModulePath ?? "./Ghostagram.Blazor/ghostagram/ghostagram.js");
            _self = DotNetObjectReference.Create(this);
            var hello = await _module.InvokeAsync<GhostagramHello>("create", _host, Options.ToInteropOptions(_self));
            _instanceId = hello.InstanceId;
            _initialized = true;
            await ReplaceAsync(CurrentDocument, Revision);
            return;
        }

        if (!_initialized || !_pendingParameterSync || Document is null) return;
        _pendingParameterSync = false;
        await ReplaceAsync(Document, Revision);
    }

    /// <summary>Replaces the visual state with an authoritative document at the given revision.</summary>
    public async Task<GhostagramResult> ReplaceAsync(DiagramDocument document, long revision, CancellationToken cancellationToken = default)
    {
        var module = RequireModule();
        Document = document;
        Revision = revision;
        var request = new { requestId = Guid.NewGuid().ToString("N"), documentId = document.DocumentId, revision, model = document };
        var result = await module.InvokeAsync<GhostagramResult>("replace", cancellationToken, _instanceId, request);
        ThrowIfFailed(result);
        _renderedRevision = result.RenderedRevision;
        _renderedDocumentId = document.DocumentId;
        _renderedDocument = document;
        _incrementallyAppliedRevision = null;
        return result;
    }

    /// <summary>Applies one atomic, revision-checked operation batch.</summary>
    public async Task<GhostagramResult> ApplyAsync(long baseRevision, long revision, IEnumerable<GhostagramOperation> operations, CancellationToken cancellationToken = default)
    {
        var request = new { requestId = Guid.NewGuid().ToString("N"), documentId = CurrentDocument.DocumentId, baseRevision, revision, ops = operations };
        var result = await RequireModule().InvokeAsync<GhostagramResult>("apply", cancellationToken, _instanceId, request);
        ThrowIfFailed(result);
        _renderedRevision = result.RenderedRevision;
        _renderedDocumentId = CurrentDocument.DocumentId;
        _incrementallyAppliedRevision = result.RenderedRevision;
        return result;
    }

    public Task<GhostagramResult> FitAsync(double padding = 32, CancellationToken cancellationToken = default) =>
        ApplyAsync(_renderedRevision, _renderedRevision + 1, [DiagramOperations.Fit(padding)], cancellationToken);

    public Task<GhostagramInspection> InspectAsync(CancellationToken cancellationToken = default) =>
        RequireModule().InvokeAsync<GhostagramInspection>("inspect", cancellationToken, _instanceId).AsTask();

    /// <summary>Converts browser client coordinates to the current Ghostagram document coordinate system.</summary>
    public Task<GhostagramCanvasPoint> ClientToCanvasAsync(double clientX, double clientY, CancellationToken cancellationToken = default) =>
        RequireModule().InvokeAsync<GhostagramCanvasPoint>("clientToCanvas", cancellationToken, _instanceId, clientX, clientY).AsTask();

    /// <summary>Returns the visible canvas center in the current document coordinate system.</summary>
    public Task<GhostagramCanvasPoint> CanvasCenterAsync(CancellationToken cancellationToken = default) =>
        RequireModule().InvokeAsync<GhostagramCanvasPoint>("canvasCenter", cancellationToken, _instanceId).AsTask();

    /// <summary>Projects a browser point and reports whether it is inside the live canvas.</summary>
    public Task<GhostagramCanvasHit> HitTestClientPointAsync(double clientX, double clientY, CancellationToken cancellationToken = default) =>
        RequireModule().InvokeAsync<GhostagramCanvasHit>("hitTestClientPoint", cancellationToken, _instanceId, clientX, clientY).AsTask();

    public Task<string> ExportSvgAsync(CancellationToken cancellationToken = default) =>
        RequireModule().InvokeAsync<string>("exportSvg", cancellationToken, _instanceId).AsTask();

    /// <summary>Exports the complete visible diagram as a PNG data URL.</summary>
    public Task<string> ExportPngAsync(CancellationToken cancellationToken = default) =>
        RequireModule().InvokeAsync<string>("exportPng", cancellationToken, _instanceId).AsTask();

    /// <summary>Copies the currently visible canvas region as a PNG image.</summary>
    public Task CopyViewportPngAsync(CancellationToken cancellationToken = default) =>
        RequireModule().InvokeVoidAsync("copyViewportPng", cancellationToken, _instanceId).AsTask();

    [JSInvokable]
    public async Task OnGhostagramEvent(GhostagramEvent envelope)
    {
        if (!EventReceived.HasDelegate) return;
        try
        {
            await EventReceived.InvokeAsync(envelope);
        }
        catch (Exception exception)
        {
            // Browser events are proposals. A host callback failure must not terminate the live diagram circuit.
            Logger.LogWarning(exception, "Ignoring Ghostagram event {EventType} ({EventId}) after host callback failure.", envelope.Type, envelope.EventId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null && _instanceId is not null)
        {
            try { await _module.InvokeVoidAsync("dispose", _instanceId); }
            catch (JSDisconnectedException) { }
        }
        _self?.Dispose();
        if (_module is not null) await _module.DisposeAsync();
    }

    private IJSObjectReference RequireModule() => _initialized && _module is not null && _instanceId is not null
        ? _module : throw new InvalidOperationException("GhostDiagram is not initialized. Invoke methods after first render.");
    private static void ThrowIfFailed(GhostagramResult result)
    {
        if (!result.Ok) throw new InvalidOperationException($"Ghostagram {result.Problem?.Code ?? "UNKNOWN"}: {result.Problem?.Message ?? "The browser operation failed."}");
    }
}
