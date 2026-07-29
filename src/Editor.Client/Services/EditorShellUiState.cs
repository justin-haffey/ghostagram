namespace Editor.Client.Services;

public enum EditorDrawerMode
{
    Assets,
    Library,
    Inspector,
    Layers,
    Snapshots,
    Validation,
    Review
}

/// <summary>
/// Holds transient editor-shell state. This service intentionally has no dependency on
/// document state, browser storage, or synchronization services.
/// </summary>
public sealed class EditorShellUiState
{
    public const int CompactBreakpoint = 1280;

    public int ViewportWidth { get; private set; }

    public bool UsesCompactLayout => ViewportWidth > 0 && ViewportWidth < CompactBreakpoint;

    public EditorDrawerMode? ActiveDrawer { get; private set; }

    // This event carries presentation-only changes; document mutations remain owned by
    // DiagramEditorState so transient shell layout cannot enter undo/save history.
    public event Action? Changed;

    public void SetViewportWidth(int viewportWidth)
    {
        var normalizedWidth = Math.Max(0, viewportWidth);
        if (ViewportWidth == normalizedWidth)
        {
            return;
        }

        ViewportWidth = normalizedWidth;
        Changed?.Invoke();
    }

    public bool ToggleDrawer(EditorDrawerMode mode)
    {
        ActiveDrawer = ActiveDrawer == mode ? null : mode;
        Changed?.Invoke();
        return ActiveDrawer is not null;
    }

    public bool TryOpenDrawer(string? mode)
    {
        if (!Enum.TryParse<EditorDrawerMode>(mode, true, out var parsedMode))
        {
            return false;
        }

        if (ActiveDrawer != parsedMode)
        {
            ActiveDrawer = parsedMode;
            Changed?.Invoke();
        }

        return true;
    }

    public void CloseDrawer()
    {
        if (ActiveDrawer is null)
        {
            return;
        }

        ActiveDrawer = null;
        Changed?.Invoke();
    }
}
