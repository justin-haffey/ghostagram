namespace Ghostagram.Blazor;

/// <summary>A folder-like grouping presented by <see cref="GhostPalette"/>.</summary>
public sealed record GhostPaletteGroup(string Id, string Label, bool Expanded = false, bool CanDelete = false);

/// <summary>A reusable palette entry. Hosts decide what the item creates when it is dropped.</summary>
public sealed record GhostPaletteItem(
    string Id,
    string GroupId,
    string Label,
    string? Description = null,
    string Accent = "#64748b",
    bool CanDelete = false);

public sealed record GhostPaletteDropRequest(string ItemId, double ClientX, double ClientY);
public sealed record GhostPaletteMoveRequest(string ItemId, string GroupId);
public sealed record GhostPaletteGroupToggleRequest(string GroupId, bool Expanded);
