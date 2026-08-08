using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ghostagram.Blazor;

public partial class GhostPalette
{
    [Inject] private IJSRuntime JavaScript { get; set; } = default!;

    private ElementReference _root;
    private ElementReference _dropTarget;
    private ElementReference _dropOverlay;
    private IJSObjectReference? _module;
    private DotNetObjectReference<GhostPalette>? _selfReference;
    private bool _hasDropTarget;

    [Parameter] public IReadOnlyList<GhostPaletteGroup> Groups { get; set; } = [];
    [Parameter] public IReadOnlyList<GhostPaletteItem> Items { get; set; } = [];
    [Parameter] public string Title { get; set; } = "Palette";
    [Parameter] public string Eyebrow { get; set; } = "Node library";
    [Parameter] public string HelpText { get; set; } = "Drag node types onto the canvas or between folders to organize your library.";
    [Parameter] public string AriaLabel { get; set; } = "Node palette";
    [Parameter] public string TreeAriaLabel { get; set; } = "Node type folders";
    [Parameter] public string? Class { get; set; }
    [Parameter] public string ModulePath { get; set; } = "/Ghostagram.Blazor/ghostagram-palette.js?v=20260808.2";
    [Parameter] public RenderFragment? HeadingContent { get; set; }
    [Parameter] public RenderFragment? HeaderActions { get; set; }
    [Parameter] public RenderFragment? FooterContent { get; set; }
    [Parameter] public RenderFragment<GhostPaletteItem>? ItemIcon { get; set; }
    [Parameter] public EventCallback<GhostPaletteDropRequest> ItemDropped { get; set; }
    [Parameter] public EventCallback<string> ItemInvoked { get; set; }
    [Parameter] public EventCallback<GhostPaletteMoveRequest> ItemMoved { get; set; }
    [Parameter] public EventCallback<string> ItemDeleteRequested { get; set; }
    [Parameter] public EventCallback<string> GroupDeleteRequested { get; set; }
    [Parameter] public EventCallback<GhostPaletteGroupToggleRequest> GroupExpandedChanged { get; set; }

    private string CssClass => $"ghost-palette {Class}".Trim();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _selfReference = DotNetObjectReference.Create(this);
        _module = await JavaScript.InvokeAsync<IJSObjectReference>("import", ModulePath);
        await _module.InvokeVoidAsync("initialize", _root, _selfReference);
        if (_hasDropTarget) await _module.InvokeVoidAsync("setDropTarget", _root, _dropTarget, _dropOverlay);
    }

    /// <summary>Connects this palette to a visual canvas target and optional drop overlay.</summary>
    public async Task AttachDropTargetAsync(ElementReference target, ElementReference overlay = default)
    {
        _dropTarget = target;
        _dropOverlay = overlay;
        _hasDropTarget = true;
        if (_module is not null) await _module.InvokeVoidAsync("setDropTarget", _root, target, overlay);
    }

    [JSInvokable]
    public Task OnPaletteItemDropped(string itemId, double clientX, double clientY) =>
        ItemDropped.InvokeAsync(new GhostPaletteDropRequest(itemId, clientX, clientY));

    [JSInvokable]
    public Task OnPaletteItemInvoked(string itemId) => ItemInvoked.InvokeAsync(itemId);

    [JSInvokable]
    public Task OnPaletteItemMoved(string itemId, string groupId) =>
        ItemMoved.InvokeAsync(new GhostPaletteMoveRequest(itemId, groupId));

    private Task ToggleGroupAsync(GhostPaletteGroup group) =>
        GroupExpandedChanged.InvokeAsync(new GhostPaletteGroupToggleRequest(group.Id, !group.Expanded));

    private Task DeleteItemAsync(string itemId) => ItemDeleteRequested.InvokeAsync(itemId);
    private Task DeleteGroupAsync(string groupId) => GroupDeleteRequested.InvokeAsync(groupId);
    private static string GroupContentId(string groupId) => $"ghost-palette-group-{Uri.EscapeDataString(groupId)}";
    private static string ItemMonogram(GhostPaletteItem item) => string.IsNullOrWhiteSpace(item.Label) ? "?" : item.Label.Trim()[..1].ToUpperInvariant();

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("dispose", _root); }
            catch (JSDisconnectedException) { }
            await _module.DisposeAsync();
        }
        _selfReference?.Dispose();
    }
}
