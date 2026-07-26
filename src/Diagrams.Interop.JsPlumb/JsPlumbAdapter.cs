using Diagrams.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Diagrams.Interop.JsPlumb;

public sealed class JsPlumbAdapter(IJSRuntime jsRuntime) : IJsPlumbAdapter
{
    private const string ModulePath = "/_content/Diagrams.Interop.JsPlumb/js/diagram-editor.js";
    private IJSObjectReference? _module;

    public async Task InitializeAsync(
        ElementReference host,
        DotNetObjectReference<JsPlumbEventBridge> bridge,
        CancellationToken cancellationToken = default)
    {
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
        await _module.InvokeVoidAsync("initialize", cancellationToken, host, bridge);
    }

    public Task RenderDocumentAsync(DiagramDocument document, CancellationToken cancellationToken = default)
        => InvokeVoidAsync("renderDocument", cancellationToken, document);

    public Task ApplyPatchAsync(DiagramDocument document, CancellationToken cancellationToken = default)
        => InvokeVoidAsync("applyPatch", cancellationToken, document);

    public Task SetLayoutAsync(LayoutKind layoutKind, CancellationToken cancellationToken = default)
        => InvokeVoidAsync("setLayout", cancellationToken, layoutKind.ToString());

    public Task SetViewportAsync(ViewportState viewportState, CancellationToken cancellationToken = default)
        => InvokeVoidAsync("setViewport", cancellationToken, viewportState);

    public Task FitToDiagramAsync(CancellationToken cancellationToken = default)
        => InvokeVoidAsync("fitToDiagram", cancellationToken);

    public Task CenterOnSelectionAsync(IReadOnlyCollection<string> nodeIds, CancellationToken cancellationToken = default)
        => InvokeVoidAsync("centerOnSelection", cancellationToken, nodeIds.ToArray());

    public Task DownloadSvgAsync(string fileName, string svgMarkup, CancellationToken cancellationToken = default)
        => InvokeVoidAsync("downloadSvg", cancellationToken, fileName, svgMarkup);

    public Task DownloadPngAsync(string fileName, string svgMarkup, CancellationToken cancellationToken = default)
        => InvokeVoidAsync("downloadPng", cancellationToken, fileName, svgMarkup);

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("dispose");
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
            finally
            {
                _module = null;
            }
        }
    }

    private async Task InvokeVoidAsync(string identifier, CancellationToken cancellationToken, params object?[]? args)
    {
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
        await _module.InvokeVoidAsync(identifier, cancellationToken, args ?? []);
    }
}
