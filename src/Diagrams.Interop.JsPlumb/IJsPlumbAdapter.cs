using Diagrams.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Diagrams.Interop.JsPlumb;

public interface IJsPlumbAdapter : IAsyncDisposable
{
    Task InitializeAsync(ElementReference host, DotNetObjectReference<JsPlumbEventBridge> bridge, CancellationToken cancellationToken = default);
    Task RenderDocumentAsync(DiagramDocument document, CancellationToken cancellationToken = default);
    Task ApplyPatchAsync(DiagramDocument document, CancellationToken cancellationToken = default);
    Task SetLayoutAsync(LayoutKind layoutKind, CancellationToken cancellationToken = default);
    Task SetViewportAsync(ViewportState viewportState, CancellationToken cancellationToken = default);
    Task FitToDiagramAsync(CancellationToken cancellationToken = default);
    Task CenterOnSelectionAsync(IReadOnlyCollection<string> nodeIds, CancellationToken cancellationToken = default);
    Task DownloadSvgAsync(string fileName, string svgMarkup, CancellationToken cancellationToken = default);
    Task DownloadPngAsync(string fileName, string svgMarkup, CancellationToken cancellationToken = default);
}

